using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RegexDebug.RegexDev
{
	internal class RegexParse
	{
		private static readonly bool[] InlineOptionThirdCharsLookup = new bool[128];
		static RegexParse()
		{
			foreach (char c in "+-imnsxIMNSX")
				InlineOptionThirdCharsLookup[c] = true;
		}

		internal static bool IsWordChar(char ch)
		{
			// Mask of Unicode categories that combine to form [\w]
			const int WordCategoriesMask =
				1 << (int)UnicodeCategory.UppercaseLetter |
				1 << (int)UnicodeCategory.LowercaseLetter |
				1 << (int)UnicodeCategory.TitlecaseLetter |
				1 << (int)UnicodeCategory.ModifierLetter |
				1 << (int)UnicodeCategory.OtherLetter |
				1 << (int)UnicodeCategory.NonSpacingMark |
				1 << (int)UnicodeCategory.DecimalDigitNumber |
				1 << (int)UnicodeCategory.ConnectorPunctuation;

			// Bitmap for whether each character 0 through 127 is in [\w]
			ReadOnlySpan<byte> ascii = new byte[]
			{
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x03,
				0xFE, 0xFF, 0xFF, 0x87, 0xFE, 0xFF, 0xFF, 0x07
			};

			// If the char is ASCII, look it up in the bitmap. Otherwise, query its Unicode category.
			int chDiv8 = ch >> 3;
			return (uint)chDiv8 < (uint)ascii.Length ?
				(ascii[chDiv8] & (1 << (ch & 0x7))) != 0 :
				(WordCategoriesMask & (1 << (int)CharUnicodeInfo.GetUnicodeCategory(ch))) != 0;
		}

		internal static void GetUnitPattern(IRegexNode d, StringBuilder sb)
		{
			if (d is Reline line)
			{
				GetUnitPattern(line.AlternationNodes[0], sb);
				for (var i = 1; i < line.AlternationNodes.Count; i++)
				{
					sb.Append("|");
					GetUnitPattern(line.AlternationNodes[i], sb);
				}
			}
			else if (d is ReSingle single)
			{
				sb.Append(single.pattern);
			}
			else if (d is RePanel panel)
			{
				for (var i = 0; i < panel.GroupingConstruct.Count; i++)
				{
					sb.Append("(");
					sb.Append(panel.GroupingConstruct[i]);
				}
				if (panel.addBracket) sb.Append("(");
				for (var i = 0; i < panel.SequenceNodes.Count; i++)
				{
					GetUnitPattern(panel.SequenceNodes[i], sb);
				}
				sb.Append(panel.Quantifier);
				if (panel.addBracket) sb.Append(")");
				for (var i = 0; i < panel.GroupingConstruct.Count; i++)
					sb.Append(")");
			}
			else if (d is ReCondition condition)
			{
				sb.Append("(?");
				if (condition.conditionGroup.Length > 0)
				{
					sb.Append("(");
					sb.Append(condition.conditionGroup);
					sb.Append(")");
				}
				else
				{
					if (condition.pattern2dotNET5.c1Cover) sb.Append("(?:");
					GetUnitPattern(condition.condition1, sb);
					if (condition.pattern2dotNET5.c1Cover) sb.Append(")");
				}
				if (condition.pattern2dotNET5.c2Cover) sb.Append("(?:");
				GetUnitPattern(condition.condition2, sb);
				if (condition.pattern2dotNET5.c2Cover) sb.Append(")");
				if (condition.HaveNoBanch)
					sb.Append("|");
				if (condition.pattern2dotNET5.c3Cover) sb.Append("(?:");
				sb.Append(condition.pattern2dotNET5.c3AddOptions);
				GetUnitPattern(condition.condition3, sb);
				if (condition.pattern2dotNET5.c3Cover) sb.Append(")");
				sb.Append(")");
			}
			else throw new Exception("条件分支不可能到这里");
		}

		/// <summary>
		/// Parse regex pattern into AST
		/// The following actions may be performed:
		///○ Simplify quantifiers
		///○ Move quantifiers after ignored whitespace, inline comments, and end-of-line comments
		///○ Simplify redundant '|' in conditional constructs
		///○ Cross-platform compatible conversion(Reference: https://github.com/dotnet/runtime/issues/111633)
		///
		/// A regex that can run on .NET framework, but cannot run on .NET5+ platform, 
		/// such as `(?(exp)(?i)x|y)`, will be converted to a regex`(?(exp)(?:(?i)x)|(?:(?i)y)` that can run on .NET5+ platform, 
		/// so there is param patterndotNET5, could be used for verification
		/// </summary>
		/// <param name="pattern"></param>
		/// <param name="style">style of ast, 1 : convert ast for NET5+ platform</param>
		/// <param name="patterndotNET5">Equivalent regex that can run on the NET5+platform</param>
		/// <returns></returns>
		/// <exception cref="Exception"></exception>
		public IRegexNode ParseAST(string pattern, int style, out string patterndotNET5)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			if (pattern is null || pattern.Length == 0)
			{
				patterndotNET5 = "";
				return new ReSingle();
			}
			var mh = SourceGeneration.ParsePatternRegex().Match(pattern);
			if (!mh.Success)
			{
				new Regex(pattern); //let .net regex engine return the pattern error message itself
				throw new Exception("正则表达式解析出错");
			}
			stopwatch.Stop();
			regexMatchTime = stopwatch.Elapsed.TotalMilliseconds;

			//Get captures，ratio of Count should be 1 : 1 : 1 : 1+
			var prefixs = mh.Groups["prefix"].Captures;
			var q1s = mh.Groups["q1"].Captures;
			var q2s = mh.Groups["q2"].Captures;
			var InLineComments = mh.Groups["InLineComment"].Captures;

			var groupingNumbers = mh.Groups["groupNumber"].Captures;
			var groupingNumberIndex = 0;
			var groupingNumberCode = 0;

			var blockLen = prefixs.Count;
			if (q1s.Count != blockLen || q2s.Count != blockLen || InLineComments.Count < blockLen)
				throw new Exception("Internal error in regexdev");//prefix:q1:q2:InLineComment = 1:1:1:1+

			#region Build a set of group names and numbers, to identify back-references and `condition expression` of `conditional construct`
			var numGroupCount = mh.Groups["groupCount"].Captures.Count;
			var groupsHashSet = new HashSet<string>(numGroupCount + mh.Groups["groupName"].Captures.Count);
			for (var i = 1; i <= numGroupCount; i++)
				groupsHashSet.Add(i.ToString());
			var namedCaptures = mh.Groups["groupName"].Captures;
			for (var i = 0; i < namedCaptures.Count; i++)
				groupsHashSet.Add(namedCaptures[i].Value);

			var NamedGroupNumberMap = new Dictionary<string, string>(groupsHashSet.Count - numGroupCount);//Store the correspondence between named groups and numerical numbers
			//var NumericGroupNameMap = new Dictionary<string, string>(numGroupCount);//Store the named capture group corresponding to the numerical number
			var namedGroupNumberIndex = numGroupCount + 1;
			for (var i = 0; i < namedCaptures.Count; i++)
			{
				if (namedCaptures[i].ValueSpan[0] >= '0' && namedCaptures[i].ValueSpan[0] <= '9') continue;//skip numeric names
				if (NamedGroupNumberMap.ContainsKey(namedCaptures[i].Value)) continue;
				var numberStr = namedGroupNumberIndex.ToString();
				while (groupsHashSet.Contains(numberStr))//avoid conflict with existing group numbers
				{
					namedGroupNumberIndex++;
					numberStr = namedGroupNumberIndex.ToString();
				}
				NamedGroupNumberMap.Add(namedCaptures[i].Value, numberStr);
				NamedGroupNumberMap.Add(numberStr, namedCaptures[i].Value);//this should not containsKey, cause numberStr is newly generated
				/*if (!NumericGroupNameMap.ContainsKey(numberStr))
					NumericGroupNameMap.Add(numberStr, namedCaptures[i].Value);*/
				groupsHashSet.Add(numberStr);//add the group number of named capture group into hashset
			}
			#endregion

			var conditionDirectContainsOption = false;//Whether the conditional construct directly contains inline options, used to determine whether the pattern needs to be converted for .NET5+

			Stack<RegexPrefix> stack = new Stack<RegexPrefix>();
			stack.Push(new RegexPrefix("("));//add a start prefix, virtual left bracket, root prefix

			List<string> InlineOptions = new List<string>();//use to store inline options directly contained in conditional construct

			var IgnoreWhiteSpaceCommentIndex = 0;//Index for ignoring whitespace and comments, cause it maybe much more than prefix
			for (var i = 0; i < blockLen; i++)
			{
				var prefix = prefixs[i].ValueSpan;
				var q1capture = q1s[i];
				var quantifier = "";
				if (q1capture.Length > 0)
				{
					quantifier = q1capture.Value;
					if (q2s[i].Length > 0) quantifier += "?";
					#region simplify quantifier
					quantifier = quantifier.QuantifierSimplify();
					#endregion
				}


				if (prefix.Length > 0)
				{
					//process prefix, includes (, ), (?> (?:, character-classes,Inline options, etc...
					if (prefix[0] == '(')
					{
						if (prefix[prefix.Length - 1] == ')')
							stack.Peek().RegexNodeList.Add(new ReSingle(prefix.ToString()) { singleType = SingleType.InlineOptions });//Inline regex options
						else
						{
							var regexPrefixType = new RegexPrefix(prefix);//group constructure[(?>,(?=], left bracket[(], conditional construct[(?]
							if (prefix.Length == 1) //prefix == "("
							{
								if (groupingNumbers[groupingNumberIndex++].Length == 0)
								{
									groupingNumberCode++;
									regexPrefixType.GroupingNumber = groupingNumberCode;
								}
							}

							stack.Push(regexPrefixType);
						}
						IgnoreWhiteSpaceCommentIndex++;
					}
					else if (prefix[0] == ')')
					{
						var leftBracket = stack.Pop();
						var isALternation = leftBracket.ALternationList.Count > 0;//Is it a alternative constructure
						var list = leftBracket.RegexNodeList;
						var listList = leftBracket.ALternationList;

						IRegexNode result;
						if (leftBracket.Type == RegexPrefixType.Condition)
						{
							if (listList.Count > 1) { new Regex(pattern); throw new Exception("Too many | in (?()|)"); }
							if (list.Count > 0) listList.Add(list);

							var c1 = listList[0][0] as RePanel;//must be
							var c2 = (listList[0].Count == 2 && listList[0][1] is RePanel) ?
									(RePanel)listList[0][1] :
									new RePanel(listList[0][1..], "");
							var c3 = listList.Count == 2 ?
								(
									(listList[1].Count == 1 && listList[1][0] is RePanel) ?
										(RePanel)listList[1][0]
										:
										new RePanel(listList[1], "")
								)
								: new RePanel(new List<IRegexNode>(), "");

							var group = "";
							if (c1.addBracket == true && c1.SequenceNodes.Count == 1 && c1.SequenceNodes[0] is ReSingle s)
							{
								//Determine whether the `condition expression` of ` conditional construct` is a capture group
								if (s.pattern.Length == 0||!IsWordChar(s.pattern[0])) goto ConvertRegex;

								if (s.pattern[0] >= '0' && s.pattern[0] <= '9')
								{
									if (groupsHashSet.Contains(s.pattern)) group = s.pattern;
									else new Regex(pattern);//let .net regex engine return the pattern error message itself
								}
								else if (groupsHashSet.Contains(s.pattern))
									group = s.pattern;
							}

							ConvertRegex:

							#region In .NET5+, conditional construct cannot directly contain inline options and regex options, need to cover something in the condition parts
							//(?(?n:())(?i)(?x)[a-z]|[A-Z]) => (?(?:(?n()))(?:(?i)(?x)[a-z])|(?:(?ix)[A-Z]))
							(bool c1Cover, bool c2Cover, bool c3Cover, string c3AddOptions) = (false, false, false, "");
							InlineOptions.Clear();
							var hasOuterOptions = false;
							if (group.Length == 0)
							{
								//No matter in .net framework or net5+, inline options are not allowed directly as the `condition expression` of `conditional constructs`
								hasOuterOptions = c1.GroupingConstruct.Count > 0 && InlineOptionThirdCharsLookup[c1.GroupingConstruct[0][1]];
								if (hasOuterOptions)
								{
									if (style == 1)
									{
										if (c1 is RePanel)
										{
											c1 = new RePanel(c1, "");
											c1.GroupingConstruct.Insert(0, "?:");
										}
									}
									else c1Cover = true;
								}
								if (hasOuterOptions) conditionDirectContainsOption = true;
								hasOuterOptions = false;
							}
							if (c2.GroupingConstruct.Count > 0)
							{
								if (InlineOptionThirdCharsLookup[c2.GroupingConstruct[0][1]])
									hasOuterOptions = true;
							}
							else if (c2.addBracket) { }
							else foreach (IRegexNode unit in c2.SequenceNodes)
								{
									if (unit is RePanel rp)//quantifier structure,  maybe `(?i:exp)+`
									{
										if (rp.GroupingConstruct.Count > 0 && InlineOptionThirdCharsLookup[rp.GroupingConstruct[0][1]])
											hasOuterOptions = true;
									}
									else if (unit is ReSingle rs)
									{
										if (rs.singleType == SingleType.InlineOptions)
										{
											hasOuterOptions = true;
											InlineOptions.Add(rs.pattern);
										}
									}
								}

							if (hasOuterOptions)
							{
								//get the inline options of condition2 which can effect condition3.
								Func<List<string>, string> GetC2OptionsCanEffectC3 = (InlineOptions) =>
								{
									var optionOpen = new StringBuilder(5);
									var optionClose = new StringBuilder(5);
									HashSet<char> existOptions = new HashSet<char>(5);
									for (var k = InlineOptions.Count - 1; k >= 0; k--)
									{
										if (existOptions.Count == 5) break;
										//var isOpen = true;
										var tmp = new List<char>(5);
										for (var j = InlineOptions[k].Length - 1; j >= 0; j--)
										{
											var op = InlineOptions[k][j];
											if (op == '+' || op == '?')
											{
												//isOpen = true;
												optionOpen.Append(new string(tmp.ToArray()));
												tmp.Clear();
												if (existOptions.Count == 5) break;
											}
											else if (op == '-')
											{
												//isOpen = false;
												optionClose.Append(new string(tmp.ToArray()));
												tmp.Clear();
												if (existOptions.Count == 5) break;
											}
											else if (InlineOptionThirdCharsLookup[op])
											{
												var ch = char.ToUpper(op);
												if (existOptions.Contains(ch)) continue;
												existOptions.Add(ch);
												tmp.Add(op);
											}
										}
									}
									var openStr = string.Join("", optionOpen.ToString().Reverse());
									var closeStr = optionClose.Length == 0 ? "" : "-" + string.Join("", optionClose.ToString().Reverse());
									return $"(?{openStr}{closeStr})";
								};

								if (style == 1)
								{
									c2 = new RePanel(c2, "");
									c2.GroupingConstruct.Insert(0, "?:");
									if (c3.SequenceNodes.Count > 0 && InlineOptions.Count > 0)
									{
										var c3AddOptions_tmp = GetC2OptionsCanEffectC3(InlineOptions);

										var condition2Condition3InlineOptions = new ReSingle(c3AddOptions_tmp) { singleType = SingleType.InlineOptions };
										if (c3.GroupingConstruct.Count == 0 && c3.addBracket == false)
											c3.SequenceNodes.Insert(0, condition2Condition3InlineOptions);
										else c3 = new RePanel(new List<IRegexNode>() { condition2Condition3InlineOptions, c3 }, "");
										c3 = new RePanel(c3, "");
										c3.GroupingConstruct.Insert(0, "?:");
									}
								}
								else
								{
									c2Cover = true;
									if (c3.SequenceNodes.Count > 0 && InlineOptions.Count > 0)
									{
										c3Cover = true;

										c3AddOptions = GetC2OptionsCanEffectC3(InlineOptions);
									}
								}
								hasOuterOptions = false;

								conditionDirectContainsOption = true;
							}
							else if (c3.GroupingConstruct.Count > 0)
							{
								if (InlineOptionThirdCharsLookup[c3.GroupingConstruct[0][1]])
									hasOuterOptions = true;
							}
							else if (c3.addBracket) { }
							else foreach (IRegexNode unit in c3.SequenceNodes)
								{
									if (unit is RePanel rp)//quantifier structure,  maybe `(?i:exp)+`
									{
										if (rp.GroupingConstruct.Count > 0 && InlineOptionThirdCharsLookup[rp.GroupingConstruct[0][1]])
											hasOuterOptions = true;
									}
									else if (unit is ReSingle rs)
									{
										if (rs.singleType == SingleType.InlineOptions)
											hasOuterOptions = true;
									}
								}

							if (hasOuterOptions)
							{
								if (style == 1)
								{
									c3 = new RePanel(c3, "");
									c3.GroupingConstruct.Insert(0, "?:");
								}
								else c3Cover = true;
							}
							if (hasOuterOptions) conditionDirectContainsOption = true;
							#endregion
							if (group.Length > 0)
							{
								var condition = new ReCondition(group, c2, c3, "") { HaveNoBanch = listList.Count == 2 };
								condition.pattern2dotNET5 = (c1Cover, c2Cover, c3Cover, c3AddOptions);

								if (NamedGroupNumberMap.ContainsKey(group)) condition.conditionGroup2RdName = NamedGroupNumberMap[group];
								//else if (NumericGroupNameMap.ContainsKey(group)) condition.conditionGroup2RdName = NumericGroupNameMap[group];

								result = condition;
							}
							else
							{
								var invalidateNearestNumberedGroup = false;
								if (c1 is RePanel panel)
								{
									if (panel.GroupingConstruct.Count > 0)
										invalidateNearestNumberedGroup = true;//if there is a input to match, tell regexdedv that should invalidates the subsequent nearest Numbered Group, issue#111632
									else if (panel.Quantifier.Length > 0)
									{
										//occurs situation like `(?(condition)+)`, that should not happen in right regex
									}
								}
								var condition = new ReCondition(c1, c2, c3, "") { HaveNoBanch = listList.Count == 2, InvalidateNearestNumberedGroup = invalidateNearestNumberedGroup };
								condition.pattern2dotNET5 = (c1Cover, c2Cover, c3Cover, c3AddOptions);

								result = condition;
							}

						}
						else
						{
							if (isALternation)
							{
								if (list.Count == 0) list.Add(new ReSingle());//`exp|exp|`
								listList.Add(list);
								list = new List<IRegexNode>();
								foreach (var l in listList)
								{
									if (l.Count == 1)
									{
										list.Add(l[0]);
										continue;
									}
									var panel = new RePanel(l, "");
									list.Add(panel);
								}

								var line = new Reline(list);//add a alternation construct
								result = line;

								/*if (leftBracket.Type == RegexPrefixType.LeftBracket)
								{
									result = new RePanel(line, "") { addBracket = true, GroupingNumber = leftBracket.GroupingIndex };//add a bracket

								}
								else
								{
									var panel = new RePanel(line, "");//add a group construct
									panel.GroupingConstruct.Insert(0, leftBracket.Pattern);

									panel.SetGroupAnotherdName(NamedGroupNumberMap, leftBracket.Pattern);

									result = panel;
								}*/

							}
							else
							{
								if (list.Count == 0) list.Add(new ReSingle());//To avoid not matching when the panel(nested structure) has grouping structures but is empty

								if (list.Count == 1) result = list[0];
								else result = new RePanel(list, "");//Concatenation/Sequence regex nodes

								/*if (leftBracket.Type == RegexPrefixType.LeftBracket)
								{
									result = new RePanel(list, "") { addBracket = true, GroupingNumber = leftBracket.GroupingIndex };//add a bracket
								}
								else
								{
									var panel = new RePanel(list, "") { GroupingConstruct = { leftBracket.Pattern } };//add a group construct

									panel.SetGroupAnotherdName(NamedGroupNumberMap, leftBracket.Pattern);

									result = panel;
								}*/

							}

							if (leftBracket.Type == RegexPrefixType.LeftBracket)
							{
								result = new RePanel(result, "") { addBracket = true, GroupingNumber = leftBracket.GroupingNumber };//add a bracket

							}
							else
							{
								var panel = new RePanel(result, "");//add a group construct
								panel.GroupingConstruct.Insert(0, leftBracket.Pattern);

								panel.SetGroupAnotherName(NamedGroupNumberMap, leftBracket.Pattern);

								result = panel;
							}

						}

						if (quantifier.Length > 0)
						{
							result = new RePanel(result, quantifier);//add a quantifier
						}

						stack.Peek().RegexNodeList.Add(result);

						for (; InLineComments[IgnoreWhiteSpaceCommentIndex].Length > 0; IgnoreWhiteSpaceCommentIndex++)
						{
							var sc = InLineComments[IgnoreWhiteSpaceCommentIndex].Value;
							if (sc[0] == '(')
								stack.Peek().RegexNodeList.Add(new ReSingle(sc) { singleType = SingleType.InlineComment });
							else if (sc[0] == '#')
								stack.Peek().RegexNodeList.Add(new ReSingle(sc) { singleType = SingleType.EndofLineComment });
							else stack.Peek().RegexNodeList.Add(new ReSingle(sc));//Ignore White Space
						}
						IgnoreWhiteSpaceCommentIndex++;
					}
					else if (prefix[0] == '|')
					{
						var x = stack.Pop();
						if (x.RegexNodeList.Count == 0) x.RegexNodeList.Add(new ReSingle());//`|exp|exp`
						x.ALternationList.Add(x.RegexNodeList);
						x.RegexNodeList = new();
						stack.Push(x);
						IgnoreWhiteSpaceCommentIndex++;
					}
					else
					{
						//Actual matching characters, anchors, backreferences, escape sequences, etc.
						#region process pattern like \5166abc+
						if (prefix[0] == '\\' && '0' <= prefix[1] && prefix[1] <= '9')
						{
							var exp = prefix.ToString();
							var result = new List<IRegexNode>();
							var number = SourceGeneration.GetBeginNumberRegex().Match(exp, 1);
							if (groupsHashSet.Contains(number.Value))
							{
								//\number is backreference
								var groupNumber = number.Value;
								if (groupNumber.Length + 1 == exp.Length)
								{
									var single = new ReSingle(exp) { IsReference = true };
									if (quantifier.Length > 0)
									{
										var panel = new RePanel(single, quantifier);//(?'12')\12+
										result.Add(panel);
									}
									else result.Add(single);
								}
								else
								{
									var nextString = exp.Substring(1 + groupNumber.Length);
									result.Add(new ReSingle("\\" + groupNumber) { IsReference = true });

									var lastContent = new ReSingle(nextString);
									if (quantifier.Length > 0)
									{
										var panel = new RePanel(lastContent, quantifier);//(?'12')\12a+
										result.Add(panel);
									}
									else result.Add(lastContent);
								}
							}
							else if (number.Length == 1) { /*new Regex(pattern);*/ throw new Exception($"Invalid pattern '{pattern}' at offset {prefixs[i].Index + prefixs[i].Length}：reference to undefined group number {number}。"); }
							else
							{
								//\number is not backreference
								//if \number is not backreference, number at least has two digits
								var asciiNumber = "";//octal digits (up to 3 digits)
								var OctalLen = 0;
								for (var j = 0; j < number.Length && j < 3; j++)
								{
									if (number.Value[j] > '7') break;
									OctalLen++;
								}
								if (OctalLen == 0) throw new Exception($"Invalid pattern '{pattern}' at offset {prefixs[i].Index + 2}：unrecognized escape sequence \\{number.Value[0]}。");
								asciiNumber = number.Value[0..OctalLen];

								if (asciiNumber.Length + 1 == exp.Length)
								{
									var lastContent = new ReSingle(exp) { IsReference = false };//need to mark that this single's \number is not a backreference
									if (quantifier.Length > 0)
									{
										var panel = new RePanel(lastContent, quantifier);//\516+
										result.Add(panel);
									}
									else result.Add(lastContent);
								}
								else
								{
									result.Add(new ReSingle("\\" + asciiNumber) { IsReference = false });//need to mark that this single's \number is not a backreference
									var nextString = exp.Substring(1 + asciiNumber.Length);
									if (nextString.Length == 1 || quantifier.Length == 0)
									{
										var lastContent = new ReSingle(nextString);
										if (quantifier.Length > 0)
										{
											var panel = new RePanel(lastContent, quantifier);//\12a+
											result.Add(panel);
										}
										else result.Add(lastContent);
									}
									else
									{
										var next1 = nextString.Remove(nextString.Length - 1, 1);
										result.Add(new ReSingle(next1));
										var lastContent = new ReSingle(nextString[nextString.Length - 1].ToString());
										var panel = new RePanel(lastContent, quantifier);//\12a+
										result.Add(panel);
									}
								}
							}
							stack.Peek().RegexNodeList.AddRange(result);
						}
						#endregion
						else
						{
							var single = new ReSingle(prefix.ToString());
							if (quantifier.Length > 0)
							{
								var result = new RePanel(single, quantifier);//\d+...
								stack.Peek().RegexNodeList.Add(result);
							}
							else stack.Peek().RegexNodeList.Add(single);
						}

						for (; InLineComments[IgnoreWhiteSpaceCommentIndex].Length > 0; IgnoreWhiteSpaceCommentIndex++)
						{
							var sc = InLineComments[IgnoreWhiteSpaceCommentIndex].Value;
							if (sc[0] == '(')
								stack.Peek().RegexNodeList.Add(new ReSingle(sc) { singleType = SingleType.InlineComment });
							else if (sc[0] == '#')
								stack.Peek().RegexNodeList.Add(new ReSingle(sc) { singleType = SingleType.EndofLineComment });
							else stack.Peek().RegexNodeList.Add(new ReSingle(sc));//Ignore White Space
						}
						IgnoreWhiteSpaceCommentIndex++;
					}
				}
				else
				{
					//if prefix is "", there must be comments and ignoreSpace
					for (; InLineComments[IgnoreWhiteSpaceCommentIndex].Length > 0; IgnoreWhiteSpaceCommentIndex++)
					{
						var sc = InLineComments[IgnoreWhiteSpaceCommentIndex].Value;
						if (sc[0] == '(')
							stack.Peek().RegexNodeList.Add(new ReSingle(sc) { singleType = SingleType.InlineComment });
						else if (sc[0] == '#')
							stack.Peek().RegexNodeList.Add(new ReSingle(sc) { singleType = SingleType.EndofLineComment });
						else stack.Peek().RegexNodeList.Add(new ReSingle(sc));//Ignore White Space
					}
					IgnoreWhiteSpaceCommentIndex++;
				}
			}


			IRegexNode rootNode;

			#region precess root prefix
			if (stack.Count > 1) throw new Exception("there should only be one current regular expression BEGIN prefix left at the top of the stack");//if code logic is right, this should not happen
			var rootPrefix = stack.Peek();
			var rootISALternation = rootPrefix.ALternationList.Count > 0;
			var listRoot = rootPrefix.RegexNodeList;
			var listListRoot = rootPrefix.ALternationList;
			List<IRegexNode> ast;
			if (rootISALternation)
			{
				if (listRoot.Count == 0) listRoot.Add(new ReSingle());//`exp|exp|`
				listListRoot.Add(listRoot);
				listRoot = new List<IRegexNode>();
				foreach (var l in listListRoot)
				{
					if (l.Count == 1)
					{
						listRoot.Add(l[0] is Reline ? new RePanel(l[0], "") : l[0]);
						continue;
					}
					var panel = new RePanel(l, "");
					listRoot.Add(panel);
				}
				ast = new List<IRegexNode>() { new Reline(listRoot) };
			}
			else ast = listRoot;

			rootNode = ast.Count == 1 ? ast[0] : new RePanel(ast, "");
			#endregion

			if (conditionDirectContainsOption)
			{
				//if conditon express directly contains regex options, the pattern need to be converted for .NET5+[issues#111632](https://github.com/dotnet/runtime/issues/111632)
				var sb = new StringBuilder();
				//for (var i = 0; i < ast.Count; i++)
				{
					GetUnitPattern(rootNode, sb);
				}
				patterndotNET5 = sb.ToString();
			}
			else patterndotNET5 = pattern;

			return rootNode;
		}

		/// <summary>
		/// could write different logic to parse c# regex by the regex[ParsePatternRegex]
		/// </summary>
		/// <param name="pattern"></param>
		/// <param name="style"></param>
		/// <param name="patterndotNET5"></param>
		/// <returns></returns>
		/// <exception cref="Exception"></exception>
		public IRegexNode Parse(string pattern, int style, out string patterndotNET5)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			if (pattern is null || pattern.Length == 0)
			{
				patterndotNET5 = "";
				return new ReSingle();
			}
			var mh = SourceGeneration.ParsePatternRegex().Match(pattern);
			if (!mh.Success)
			{
				new Regex(pattern); //let .net regex engine return the pattern error message itself
				throw new Exception("正则表达式解析出错");
			}
			stopwatch.Stop();
			regexMatchTime = stopwatch.Elapsed.TotalMilliseconds;

			//Get captures，ratio of Count should be 1 : 1 : 1 : 1+
			var prefixs = mh.Groups["prefix"].Captures;
			var q1s = mh.Groups["q1"].Captures;
			var q2s = mh.Groups["q2"].Captures;
			var InLineComments = mh.Groups["InLineComment"].Captures;

			var blockLen = prefixs.Count;
			if (q1s.Count != blockLen || q2s.Count != blockLen || InLineComments.Count < blockLen)
				throw new Exception("Internal error in regexdev");//prefix:q1:q2:InLineComment = 1:1:1:1+

			#region Build a set of group names and numbers, to identify back-references and `condition expression` of `conditional construct`
			var numGroupCount = mh.Groups["groupCount"].Captures.Count;
			var groupsHashSet = new HashSet<string>(numGroupCount + mh.Groups["groupName"].Captures.Count);
			for (var i = 1; i <= numGroupCount; i++)
				groupsHashSet.Add(i.ToString());
			var namedCaptures = mh.Groups["groupName"].Captures;
			for (var i = 0; i < namedCaptures.Count; i++)
				groupsHashSet.Add(namedCaptures[i].Value);

			var NamedGroupNumberMap = new Dictionary<string, string>(groupsHashSet.Count - numGroupCount);//Store the correspondence between named groups and numerical numbers
			//var NumericGroupNameMap = new Dictionary<string, string>(numGroupCount);//Store the named capture group corresponding to the numerical number
			var namedGroupNumberIndex = numGroupCount + 1;
			for (var i = 0; i < namedCaptures.Count; i++)
			{
				if (namedCaptures[i].ValueSpan[0] >= '0' && namedCaptures[i].ValueSpan[0] <= '9') continue;//skip numeric names
				if (NamedGroupNumberMap.ContainsKey(namedCaptures[i].Value)) continue;
				var numberStr = namedGroupNumberIndex.ToString();
				while (groupsHashSet.Contains(numberStr))//avoid conflict with existing group numbers
				{
					namedGroupNumberIndex++;
					numberStr = namedGroupNumberIndex.ToString();
				}
				NamedGroupNumberMap.Add(namedCaptures[i].Value, numberStr);
				NamedGroupNumberMap.Add(numberStr, namedCaptures[i].Value);//this should not containsKey, cause numberStr is newly generated
				/*if (!NumericGroupNameMap.ContainsKey(numberStr))
					NumericGroupNameMap.Add(numberStr, namedCaptures[i].Value);*/
				groupsHashSet.Add(numberStr);//add the group number of named capture group into hashset
			}
			#endregion

			var conditionDirectContainsOption = false;//Whether the conditional construct directly contains inline options, used to determine whether the pattern needs to be converted for .NET5+

			List<string> InlineOptions = new List<string>();//use to store inline options directly contained in conditional construct

			var IgnoreWhiteSpaceCommentIndex = 0;//Index for ignoring whitespace and comments, cause it maybe much more than prefix
			for (var i = 0; i < blockLen; i++)
			{
				var prefix = prefixs[i].ValueSpan;

				if (prefix.Length > 0)
				{
					//process prefix, includes (, ), (?> (?:, character-classes,Inline options, etc...
					if (prefix[0] == '(')
					{
						if (prefix[prefix.Length - 1] == ')')
						{
							//Inline regex options
						}
						else
						{
							//group constructure[(?>,(?=], left bracket[(], conditional construct[(?]
						}
					}
					else if (prefix[0] == ')')
					{
						//process right bracket
					}
					else if (prefix[0] == '|')
					{
						//process alternation
					}
					else
					{
						//Actual matching characters, anchors, backreferences, escape sequences, etc.
						#region process pattern like \5166abc+
						if (prefix[0] == '\\' && '0' <= prefix[1] && prefix[1] <= '9')
						{
							//process backreference or octal escape
						}
						#endregion
						else
						{
							//process characters, anchor, etc.
						}

					}

				}

				#region process quantifier if not empty
				var q1capture = q1s[i];
				var quantifier = "";
				if (q1capture.Length > 0)
				{
					quantifier = q1capture.Value;
					if (q2s[i].Length > 0) quantifier += "?";
					#region simplify quantifier
					//try to simplify quantifier
					#endregion
					//add quantifier to the last added node
				}
				#endregion

				#region process comments and ignoreSpace, there is unless one empty as a separator
				for (; InLineComments[IgnoreWhiteSpaceCommentIndex].Length > 0; IgnoreWhiteSpaceCommentIndex++)
				{
					var sc = InLineComments[IgnoreWhiteSpaceCommentIndex].Value;
					if (sc[0] == '(')
					{
						//Inline Comment
					}
					else if (sc[0] == '#')
					{
						//End of Line Comment
					}
					else
					{
						//Ignore White Space
					}
				}
				IgnoreWhiteSpaceCommentIndex++;
				#endregion
			}


			IRegexNode rootNode = new ReSingle();

			#region precess root prefix
			List<IRegexNode> ast = new List<IRegexNode>();
			//TODO: process rootPrefix to ast
			#endregion

			if (conditionDirectContainsOption)
			{
				//if conditon express directly contains regex options, the pattern need to be converted for .NET5+[issues#111632](https://github.com/dotnet/runtime/issues/111632)
				patterndotNET5 = pattern;//Need to be converted for .NET5+
			}
			else patterndotNET5 = pattern;

			return rootNode;
		}

		/// <summary>
		/// Record time taken by ParsePatternRegex to match the target regex
		/// </summary>
		public double regexMatchTime { get; set; } = 0d;
	}

	internal partial class SourceGeneration
	{
		[GeneratedRegex(@"(?<=^\\)\d+")]
		internal static partial Regex GetBeginNumberRegex();

		/// <summary>
		/// this regex is used to parse the target regex(user regex), decomposes the target regex(user regex) into repeating units with the following structures with a ratio of 1:1:1:1+:
		///○ Prefix(in Group#prefix): Actual matching characters, anchors, group construct prefixes[(, (?>, (?=, etc.], inline comments, closing parenthesis ')','|', etc
		///○ Quantifier 1(in  group#q1): Basic quantifiers such as +, *, ?, {num1,num2}
		///○ Quantifier 2(in group#q2): Lazy matching symbol '?'
		///○ Comment(in group#InLineComment): Inline comments, end-of-line comments, invalid whitespace(at least one empty comment as a separator)
		///
		/// record the group names by group#groupName and count numberic group by group#groupCount for back-references and `condition expression` of `conditional construct`
		/// During parsing, this regex also:
		///   - Tracks numbered and named capture groups
		///   - Assigns incremental indices to capturing parentheses
		///   - Marks non-capturing groups explicitly
		/// This information is required to correctly resolve:
		///   - Whether or not is a back reference (\516, \k<name>)
		///   - Whether or not is a group condition expression (?(cond)yes|no)
		/// and mark number of the bracket`()`, every `()` has a corresponding capture in group #p with an automatically incremented index( by group#groupNumber).
		///    capture == "" means current `()` is a capturing group, otherwise it is not a capturing group or invalid by `(?(?=))`, `(?(?>))` etc.
		/// </summary>
		/// <returns></returns>
		//[GeneratedRegex(@"(?ns)^(?>(?>(?'prefix'(?'open'\()(?(bracketX)(?'bracketX'))(?(bracketN)(?'bracketN'))(?>\?(?>(?>(?'quote'')|<)(?>(?'groupName'(?'onegroup'\w+))?(?'onegroup'-\w+)?)(?'-onegroup')(?'-onegroup')?(?((?'-quote'))'|>)|[>:]|<?[=!]|(?=\(([^?](?(openN)|(?'alternationConstructBracket'))|))|(?'openOption')(?>(?>\+(?'-openOption')?(?'openOption')|-(?'-openOption')?|[IMSims]+|[Xx]+(?(openOption)(?>(?'-openXOption')?(?'-closeXOption')?(?'openXOption'))|(?'-openXOption')?(?'-closeXOption')?(?'closeXOption'))|[Nn]+(?(openOption)(?>(?'-openNOption')?(?'-closeNOption')?(?'openNOption'))|(?'-openNOption')?(?'-closeNOption')?(?'closeNOption')))+)(?>(?'-open'\))(?>(?'-bracketX')|)(?>(?'-bracketN')|)|:)(?'-openOption')?(?>(?'-openXOption')(?(openX)|(?(bracketX)(?(?<=(?!\k'bracketX')(?>(?!\().*?))(?'-bracketX')|(?<=(?'bracketX'\().*?))|(?<=(?'bracketX'\().*?))(?'openX'))|(?'-closeXOption')(?(openX)(?(?<=(?!\k'bracketX')(?>(?!\().*?))(?'-bracketX')|(?<=(?'bracketX'\().*?))(?'-openX'))|)(?>(?'-openNOption')(?(openN)|(?(bracketN)(?(?<=(?!\k'bracketN')(?>(?!\().*?))(?'-bracketN')|(?<=(?'bracketN'\().*?))|(?<=(?'bracketN'\().*?))(?'openN'))|(?'-closeNOption')(?(openN)(?(?<=(?!\k'bracketN')(?>(?!\().*?))(?'-bracketN')|(?<=(?'bracketN'\().*?))(?'-openN'))|))|(?=[^?])(?(openN)|(?>(?'-alternationConstructBracket')|(?'groupCount')))))(?'q1')(?'q2')(?'InLineComment')|(?'prefix'\)(?'-open')(?(bracketX)(?(?<!\k'bracketX')(?'-bracketX')(?'-bracketX')?((?'-openX')|(?'openX'))|(?'-bracketX')))(?(bracketN)(?(?<!\k'bracketN')(?'-bracketN')(?'-bracketN')?((?'-openN')|(?'openN'))|(?'-bracketN')))|(?'open1'\[)(?>\^?)(?>(?'charrange'[]-](-([^][\\]|\\(?'ASCII2'[0-7]{1,3})|\\[^DdWwSsPpuxc]|\\u[0-9A-Fa-f]{4}|\\x[0-9A-Fa-f]{2}|\\c[A-Za-z[\\\]^_]))?)?)(?>(?>(?'open1'-\[)(?>(?'-charrange')?\^?)(?>(?'charrange'[]-](-([^][\\]|\\(?'ASCII2'[0-7]{1,3})|\\[^DdWwSsPpuxc]|\\u[0-9A-Fa-f]{4}|\\x[0-9A-Fa-f]{2}|\\c[A-Za-z[\\\]^_]))?)?)|(([^]\\]|\\(?'ASCII1'[0-7]{1,3})|\\[^DdWwSsPpuxc]|\\u[0-9A-Fa-f]{4}|\\x[0-9A-Fa-f]{2}|\\c[A-Za-z[\\\]^_])(-([^][\\]|\\(?'ASCII2'[0-7]{1,3})|\\[^DdWwSsPpuxc]|\\u[0-9A-Fa-f]{4}|\\x[0-9A-Fa-f]{2}|\\c[A-Za-z[\\\]^_]))?)|\\(?>[DdWwSs]|[Pp]\{[A-Za-z\d-]+\})|(?'-charrange'))+)\-?(?>(?'-open1'\])+)(?(open1)(?!))|\\(k(<|(?<quote>'))([0-9]+|(?![0-9]+)\w+)(?((?'-quote'))'|>)|[dDwWsSAzZGbB]|[0-9]+((?>[^[+*?()\\|\t\r\n\f]|\\[^Pp\dkcdDwWsSAzZGbBux])(?![*+?]|\{\d+(,\d*)?\}))*|[Pp]\{[A-Za-z\d-]+\}|u([A-Fa-f0-9]{4})|x([A-Fa-f0-9]{2})|c([A-Za-z[\\\]^_])))(?(openX)(?'InLineComment'\(\?#(?>[^)]*)\)|[ \t\r\n\f]+|#[^\r\n]*)*(?'q1'([*+?]|\{\d+(,\d*)?\})?)(?'InLineComment'\(\?#(?>[^)]*)\)|[ \t\r\n\f]+|#[^\r\n]*)*(?'q2'\??)|(?'InLineComment'\(\?#(?>[^)]*)\))*(?'q1'([*+?]|\{\d+(,\d*)?\})?)(?'InLineComment'\(\?#(?>[^)]*)\))*(?'q2'\??))(?'InLineComment')|(?(openX)(?>(?>(?'prefix'[^+*?()\\|^$# \t\r\n\f]|\\.))(?>(?>(?>(?'InLineComment'\(\?#(?>[^)]*)\)|[ \t\r\n\f]+|#[^\r\n]*)+)(?'q1'([*+?]|\{\d+(,\d*)?\})?)|(?'q1'[*+?]|\{\d+(,\d*)?\}))(?'InLineComment'\(\?#(?>[^)]*)\)|[ \t\r\n\f]+|#[^\r\n]*)*(?'q2'\??)(?'InLineComment'))|(?'prefix'((?>[^[+*?()\\|^$#]|\\[^Pp\dkcuxdDwWsSAzZGbB])(?!(?>(?>\(\?#(?>[^)]*)\)|[ \t\r\n\f]+|#[^\r\n]*)*)(?>[*+?]|\{\d+(,\d*)?\}))[ \t\r\n\f]*)+)(?'q1')(?'q2')(?'InLineComment'))|(?>(?>(?'prefix'[^+*?()\\^$|]|\\.))(?>(?>(?'InLineComment'\(\?#(?>[^)]*)\))*)(?'q1'[*+?]|\{\d+(,\d*)?\})(?'InLineComment'\(\?#(?>[^)]*)\))*(?'q2'\??)(?'InLineComment'))|(?'prefix'((?>[^[+*?()\\|^$]|\\[^Pp\dkcuxdDwWsSAzZGbB])(?!(?>(?>\(\?#(?>[^)]*)\))*)(?>[*+?]|\{\d+(,\d*)?\})))+)(?'q1')(?'q2')(?'InLineComment')))|(?'prefix'[$^|])(?'q1')(?'q2')(?'InLineComment')|((?'InLineComment'\(\?#(?>[^)]*)\))(?'prefix')(?'InLineComment')(?'q1')(?'q2'))+|(?(openX)|(?!))(?'InLineComment'#[^\r\n]*)(?'InLineComment'[ \t\r\n\f]+|\(\?#(?>[^)]*)\)|#[^\r\n]*)*(?'q1')(?'q2')(?'prefix')(?'InLineComment'))+)(?!(?'-open'))$")]
		[GeneratedRegex(@"(?ns)^(?>(?>(?'prefix'(?'open'\()(?(bracketX)(?'bracketX'))(?(bracketN)(?'bracketN'))(?>\?(?>(?>(?'quote'')|<)(?>(?'groupName'(?'onegroup'\w+))?(?'onegroup'-\w+)?)(?'-onegroup')(?'-onegroup')?(?((?'-quote'))'|>)|[>:]|<?[=!]|(?=\((?'-conditionExpress')?([^?](?(openN)|(?'conditionExpress'))|(?'InvalidateNearestNumberedGroup')))|(?'openOption')(?>(?>\+(?'-openOption')?(?'openOption')|-(?'-openOption')?|[IMSims]+|[Xx]+(?(openOption)(?>(?'-openXOption')?(?'-closeXOption')?(?'openXOption'))|(?'-openXOption')?(?'-closeXOption')?(?'closeXOption'))|[Nn]+(?(openOption)(?>(?'-openNOption')?(?'-closeNOption')?(?'openNOption'))|(?'-openNOption')?(?'-closeNOption')?(?'closeNOption')))+)(?>(?'-open'\))(?>(?'-bracketX')|)(?>(?'-bracketN')|)|:)(?'-openOption')?(?>(?'-openXOption')(?(openX)|(?(bracketX)(?(?<=(?!\k'bracketX')(?>(?!\().*?))(?'-bracketX')|(?<=(?'bracketX'\().*?))|(?<=(?'bracketX'\().*?))(?'openX'))|(?'-closeXOption')(?(openX)(?(?<=(?!\k'bracketX')(?>(?!\().*?))(?'-bracketX')|(?<=(?'bracketX'\().*?))(?'-openX'))|)(?>(?'-openNOption')(?(openN)|(?(bracketN)(?(?<=(?!\k'bracketN')(?>(?!\().*?))(?'-bracketN')|(?<=(?'bracketN'\().*?))|(?<=(?'bracketN'\().*?))(?'openN'))|(?'-closeNOption')(?(openN)(?(?<=(?!\k'bracketN')(?>(?!\().*?))(?'-bracketN')|(?<=(?'bracketN'\().*?))(?'-openN'))|))|(?=[^?])(?(openN)(?<=(?'groupNumber'.(?#used to mark current bracket's grouping number, capture empty content sign to have hnumber)))|(?>(?'-conditionExpress')(?<=(?'groupNumber'.))|(?'groupCount'(?#used to count how many numberic captured groups))(?>(?'-InvalidateNearestNumberedGroup')(?<=(?'groupNumber'.))|(?'groupNumber'))))))(?'q1')(?'q2')(?'InLineComment')|(?'prefix'\)(?'-open')(?(bracketX)(?(?<!\k'bracketX')(?'-bracketX')(?'-bracketX')?((?'-openX')|(?'openX'))|(?'-bracketX')))(?(bracketN)(?(?<!\k'bracketN')(?'-bracketN')(?'-bracketN')?((?'-openN')|(?'openN'))|(?'-bracketN')))|(?'open1'\[)(?>\^?)(?>(?'charrange'[]-](-(?>[^][\\]|\\([0-7]{1,3}|[^DdWwSsPpuxc]|u[0-9A-Fa-f]{4}|x[0-9A-Fa-f]{2}|c[A-Za-z[\\\]^_])))?)?)(?>(?>(?'open1'-\[)(?>(?'-charrange')?\^?)(?>(?'charrange'[]-](-(?>[^][\\]|\\([0-7]{1,3}|[^DdWwSsPpuxc]|u[0-9A-Fa-f]{4}|x[0-9A-Fa-f]{2}|c[A-Za-z[\\\]^_])))?)?)|((?>[^]\\]|\\([0-7]{1,3}|[^DdWwSsPpuxc]|u[0-9A-Fa-f]{4}|x[0-9A-Fa-f]{2}|c[A-Za-z[\\\]^_]))(-(?>[^][\\]|\\([0-7]{1,3}|[^DdWwSsPpuxc]|u[0-9A-Fa-f]{4}|x[0-9A-Fa-f]{2}|c[A-Za-z[\\\]^_])))?)|\\(?>[DdWwSs]|[Pp]\{[A-Za-z\d-]+\})|(?'-charrange'))+)\-?(?>(?'-open1'\])+)(?(open1)(?!))|\\(k(<|(?<quote>'))([0-9]+|(?![0-9]+)\w+)(?((?'-quote'))'|>)|[dDwWsSAzZGbB]|[0-9]+((?>[^[+*?()\\|\t\r\n\f]|\\[^Pp\dkcdDwWsSAzZGbBux])(?![*+?]|\{\d+(,\d*)?\}))*|[Pp]\{[A-Za-z\d-]+\}|u([A-Fa-f0-9]{4})|x([A-Fa-f0-9]{2})|c([A-Za-z[\\\]^_])))(?(openX)(?'InLineComment'\(\?#(?>[^)]*)\)|[ \t\r\n\f]+|#[^\r\n]*)*(?'q1'([*+?]|\{\d+(,\d*)?\})?)(?'InLineComment'\(\?#(?>[^)]*)\)|[ \t\r\n\f]+|#[^\r\n]*)*(?'q2'\??)|(?'InLineComment'\(\?#(?>[^)]*)\))*(?'q1'([*+?]|\{\d+(,\d*)?\})?)(?'InLineComment'\(\?#(?>[^)]*)\))*(?'q2'\??))(?'InLineComment')|(?(openX)(?>(?>(?'prefix'[^+*?()\\|^$# \t\r\n\f]|\\.))(?>(?>(?>(?'InLineComment'\(\?#(?>[^)]*)\)|[ \t\r\n\f]+|#[^\r\n]*)+)(?'q1'([*+?]|\{\d+(,\d*)?\})?)|(?'q1'[*+?]|\{\d+(,\d*)?\}))(?'InLineComment'\(\?#(?>[^)]*)\)|[ \t\r\n\f]+|#[^\r\n]*)*(?'q2'\??)(?'InLineComment'))|(?'prefix'((?>[^[+*?()\\|^$#]|\\[^Pp\dkcuxdDwWsSAzZGbB])(?!(?>(?>\(\?#(?>[^)]*)\)|[ \t\r\n\f]+|#[^\r\n]*)*)(?>[*+?]|\{\d+(,\d*)?\}))[ \t\r\n\f]*)+)(?'q1')(?'q2')(?'InLineComment'))|(?>(?>(?'prefix'[^+*?()\\^$|]|\\.))(?>(?>(?'InLineComment'\(\?#(?>[^)]*)\))*)(?'q1'[*+?]|\{\d+(,\d*)?\})(?'InLineComment'\(\?#(?>[^)]*)\))*(?'q2'\??)(?'InLineComment'))|(?'prefix'((?>[^[+*?()\\|^$]|\\[^Pp\dkcuxdDwWsSAzZGbB])(?!(?>(?>\(\?#(?>[^)]*)\))*)(?>[*+?]|\{\d+(,\d*)?\})))+)(?'q1')(?'q2')(?'InLineComment')))|(?'prefix'[$^|])(?'q1')(?'q2')(?'InLineComment')|((?'InLineComment'\(\?#(?>[^)]*)\))(?'prefix')(?'InLineComment')(?'q1')(?'q2'))+|(?(openX)|(?!))(?'InLineComment'#[^\r\n]*)(?'InLineComment'[ \t\r\n\f]+|\(\?#(?>[^)]*)\)|#[^\r\n]*)*(?'q1')(?'q2')(?'prefix')(?'InLineComment'))+)(?!(?'-open'))$")]
		internal static partial Regex ParsePatternRegex();
	}

	internal struct RegexPrefix
	{
		/// <summary>
		/// use to store Concatenation/Sequence
		/// </summary>
		public List<IRegexNode> RegexNodeList { get; set; }

		/// <summary>
		/// use to store Concatenation/Sequence splitted by alternation
		/// </summary>
		public List<List<IRegexNode>> ALternationList { get; set; }

		public RegexPrefix(ReadOnlySpan<char> pattern)
		{
			//Pattern = pattern;
			char firstChar = pattern[0];

			if (firstChar == '(')
			{
				RegexNodeList = new();
				ALternationList = new();

				if (pattern.Length > 1 && pattern[1] == '?')
				{
					if (pattern.Length == 2)
					{
						Type = RegexPrefixType.Condition;
						//Pattern = "(?";
					}
					else
					{
						Type = RegexPrefixType.GroupingConstruct;
						(Type, Pattern) = (RegexPrefixType.GroupingConstruct, pattern[1..].ToString());
					}
				}
				else
				{
					Type = RegexPrefixType.LeftBracket;
				}
			}
			/*else if (firstChar == '|')
			{
				Type = RegexPrefixType.ALternation;
			}*/
		}

		public string Pattern { get; set; } = "";

		public RegexPrefixType Type { get; set; }

		public int GroupingNumber { get; set; } = -1;//used to record the grouping number of current prefix, only valid when Type is LeftBracket
	}

	public enum RegexPrefixType
	{
		GroupingConstruct,
		LeftBracket,
		Unit,
		//ALternation,
		Condition
	}

	internal static class ParseExtend
	{
		/// <summary>
		/// Simplify Quantifier
		/// </summary>
		/// <param name="quantifier"></param>
		/// <returns></returns>
		public static string QuantifierSimplify(this string quantifier)
		{
			if (quantifier[0] == '{')
			{
				var isLazyMatch = quantifier[quantifier.Length - 1] == '?';
				var offsetLen = isLazyMatch ? 1 : 0;
				var mtime = quantifier;
				ReadOnlySpan<char> mtimeSpan = mtime;
				int splitIndex = mtime.IndexOf(',', 2);
				if (splitIndex < 0)
				{
					var minstr = mtimeSpan.Slice(1, mtime.Length - 2 - offsetLen);
					if (minstr.Length == 1 && minstr[0] == '1') quantifier = "";
					else if (isLazyMatch) quantifier = $"{{{minstr}}}";
				}
				else
				{
					var minstr = mtimeSpan.Slice(1, splitIndex - 1);
					var maxstr = new ReadOnlySpan<char>();
					int maxlen = mtime.Length - splitIndex - 2 - offsetLen;
					if (maxlen > 0)
					{
						maxstr = mtimeSpan.Slice(splitIndex + 1, maxlen);
					}
					if (maxstr.Length == 0)
					{
						if (minstr.Length == 1 && minstr[0] == '0') quantifier = "*" + (isLazyMatch ? "?" : "");
						else if (minstr.Length == 1 && minstr[0] == '1') quantifier = "+" + (isLazyMatch ? "?" : "");
					}
					else
					{
						if (minstr.Length == 1 && minstr[0] == '0' && maxstr.SequenceEqual("2147483647"))
							quantifier = "*" + (isLazyMatch ? "?" : "");
						else if (minstr.Length == 1 && minstr[0] == '1' && maxstr.SequenceEqual("2147483647"))
							quantifier = "+" + (isLazyMatch ? "?" : "");
						else if (minstr.Length == 1 && minstr[0] == '0' && maxstr.Length == 1 && maxstr[0] == '1')
							quantifier = "?" + (isLazyMatch ? "?" : "");
						else if (minstr.Length == 1 && minstr[0] == '1' && maxstr.Length == 1 && maxstr[0] == '1')
							quantifier = "";//equals 1
						else if (minstr.SequenceEqual(maxstr)) quantifier = $"{{{minstr}}}";
					}
				}
			}
			return quantifier;
		}

		/// <summary>
		/// Set Capturing Group and Balancing Group another name
		/// </summary>
		/// <param name="panel"></param>
		/// <param name="NamedGroupNumberMap"></param>
		/// <param name="groupconstrucValue"></param>
		public static void SetGroupAnotherName(this RePanel panel, Dictionary<string, string> NamedGroupNumberMap, string groupconstrucValue)
		{
			if (groupconstrucValue.Length > 3)//check if is capturing group, only when the length > 3, it maybe a capturing group
			{
				var lastChar = groupconstrucValue[groupconstrucValue.Length - 1];
				if (lastChar == '\'' || lastChar == '>')
				{
					var value = groupconstrucValue;
					string CaptureName = "", BalanceName = "";
					var subCharIndex = value.IndexOf('-', 2);
					if (subCharIndex > 0)
					{
						CaptureName = value.Substring(2, subCharIndex - 2);
						BalanceName = value.Substring(subCharIndex + 1, value.Length - subCharIndex - 2);
					}
					else CaptureName = value.Substring(2, value.Length - 3);

					if (NamedGroupNumberMap.ContainsKey(CaptureName))
					{
						panel.CaptureGroup2rdName = NamedGroupNumberMap[CaptureName].ToString();
					}
					if (NamedGroupNumberMap.ContainsKey(BalanceName))
					{
						panel.BalancingGroup2rdName = NamedGroupNumberMap[BalanceName].ToString();
					}
				}
			}
		}

	}

}

using RegexDebug.RegexDev;
using System.Net;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;

namespace RegexDebug
{
	/// <summary>
	/// this printer provide by deepseek.
	/// </summary>
	internal class RegexParserUtils
	{
		// 打印AST树形结构
		public static void PrintASTTree(IRegexNode node)
		{
			var sb = new StringBuilder();
			BuildTreeString(node, "", true, sb, false, "");
			Console.WriteLine(sb.ToString());
		}

		private static void BuildTreeString(IRegexNode node, string indent, bool isLast, StringBuilder sb, bool isConditionBranch, string branchType)
		{
			// 添加连接线
			sb.Append(indent);
			if (!string.IsNullOrEmpty(indent))
			{
				sb.Append(isLast ? "└── " : "├── ");
			}

			// 根据分支类型添加前缀
			if (isConditionBranch && !string.IsNullOrEmpty(branchType))
			{
				if (branchType == "yes")
					sb.Append("✓ ");
				else if (branchType == "no")
					sb.Append("✗ ");
			}

			// 根据节点类型添加内容
			string nodeLabel = GetNodeLabel(node);
			sb.AppendLine(nodeLabel);

			// 构建新的缩进
			string newIndent = indent + (isLast ? "    " : "│   ");

			// 对于条件节点，需要特殊处理分支颜色
			if (node is ReCondition condition)
			{
				var childrenWithBranchTypes = new List<(IRegexNode node, string branchType)>();

				// 添加条件分支
				if (!string.IsNullOrEmpty(condition.conditionGroup))
				{
					var aka = condition.conditionGroup2RdName.Length > 0 ? $" AKA: {condition.conditionGroup2RdName}" : "";
					childrenWithBranchTypes.Add((new ReSingle($"Group: {condition.conditionGroup}{aka}", SingleType.Backreference), ""));
				}
				else if (condition.condition1 != null)
				{
					childrenWithBranchTypes.Add((condition.condition1, ""));
				}

				// 添加yes分支
				childrenWithBranchTypes.Add((condition.condition2, "yes"));

				// 添加no分支（如果有）
				if (condition.HaveNoBanch && condition.condition3 != null)
				{
					childrenWithBranchTypes.Add((condition.condition3, "no"));
				}

				// 递归处理带分支类型的子节点
				for (int i = 0; i < childrenWithBranchTypes.Count; i++)
				{
					bool childIsLast = i == childrenWithBranchTypes.Count - 1;
					var child = childrenWithBranchTypes[i];
					BuildTreeString(child.node, newIndent, childIsLast, sb,
								  !string.IsNullOrEmpty(child.branchType), child.branchType);
				}
			}
			else
			{
				// 正常节点的递归处理
				var children = GetNodeChildren(node);
				for (int i = 0; i < children.Count; i++)
				{
					bool childIsLast = i == children.Count - 1;
					BuildTreeString(children[i], newIndent, childIsLast, sb, false, "");
				}
			}
		}

		// 获取节点标签
		private static string GetNodeLabel(IRegexNode node)
		{
			if (node is ReSingle single)
			{
				string typeStr = GetSingleTypeString(single.singleType);
				string content = EscapeSpecialChars(single.pattern);

				string referenceInfo = single.IsReference.HasValue
					? (single.IsReference.Value ? " [Backreference]" : " [Octal]")
					: "";

				return $"{typeStr}: {content}{referenceInfo}";
			}
			else if (node is RePanel panel)
			{
				if (panel.addBracket)
				{
					if (panel.iscondition)
						return "Condition Group [Non-capturing]";
					else
						return (panel.GroupingNumber > 0 ? "" : "Non-") + "Capturing Group" + (panel.GroupingNumber > 0 ? $"#{panel.GroupingNumber}" : "") + (string.IsNullOrEmpty(panel.Quantifier) ? "" : $" [{panel.Quantifier}]");
				}
				else if (panel.GroupingConstruct.Count > 0)
				{
					string construct = string.Join(", ", panel.GroupingConstruct);

					var aka = "";
					if ((panel.CaptureGroup2rdName.Length + panel.BalancingGroup2rdName.Length) > 0)
					{
						var f = panel.GroupingConstruct[0][1] == '<' ? '<' : '\'';
						var l = panel.GroupingConstruct[0][1] == '<' ? '>' : '\'';
						aka = $" AKA: (?{f}{panel.CaptureGroup2rdName}{(panel.BalancingGroup2rdName.Length > 0 ? $"-{panel.BalancingGroup2rdName}" : "")}{l})";

						var list = new List<string>();
						if (panel.CaptureGroup2rdName.Length > 0) list.Add("#" + panel.CaptureGroup2rdName);
						if (panel.BalancingGroup2rdName.Length > 0) list.Add("#" + panel.BalancingGroup2rdName);
						aka = $" AKA: {string.Join(", ", list)}";
					}

					return $"Grouping Construct: ({construct})" +
						   (aka) +
						   (string.IsNullOrEmpty(panel.Quantifier) ? "" : $" [{panel.Quantifier}]");
				}
				else if (!string.IsNullOrEmpty(panel.Quantifier))
				{
					return $"Quantifier: {panel.Quantifier}";
				}
				else
				{
					return "Sequence";
				}
			}
			else if (node is Reline)
			{
				return "Alternation (|)";
			}
			else if (node is ReCondition condition)
			{
				var aka = condition.conditionGroup2RdName.Length > 0 ? $" AKA: {condition.conditionGroup2RdName}" : "";
				string conditionType = string.IsNullOrEmpty(condition.conditionGroup)
					? "Expression Condition"
					: $"Group Condition: {condition.conditionGroup}{aka}";

				if (condition.InvalidateNearestNumberedGroup)
					conditionType += " [Invalidates Nearest numbered group]";

				return conditionType;
			}

			return "Unknown Node";
		}

		// 获取单节点类型字符串
		private static string GetSingleTypeString(SingleType type)
		{
			return type switch
			{
				SingleType.Default => "Character/Token",
				SingleType.CharacterGroup => "Character Group",
				SingleType.Backreference => "Backreference",
				SingleType.PatternMiddleOption => "Inline Option",
				SingleType.InlineComment => "Inline Comment",
				SingleType.Anchor => "Anchor",
				SingleType.InlineOptions => "Inline Options",
				SingleType.CharacterClasses => "Character Class",
				SingleType.EndofLineComment => "End-of-Line Comment",
				_ => "Unknown"
			};
		}

		// 转义特殊字符以便显示
		private static string EscapeSpecialChars(string input)
		{
			if (string.IsNullOrEmpty(input)) return "[empty]";

			var result = new StringBuilder();
			foreach (char c in input)
			{
				switch (c)
				{
					case '\n': result.Append("\\n"); break;
					case '\r': result.Append("\\r"); break;
					case '\t': result.Append("\\t"); break;
					case '\0': result.Append("\\0"); break;
					case '\\': result.Append("\\\\"); break;
					default:
						if (c < 32)
							result.Append($"\\u{(int)c:X4}");
						else
							result.Append(c);
						break;
				}
			}
			return result.ToString();
		}

		// 获取节点的子节点
		private static List<IRegexNode> GetNodeChildren(IRegexNode node)
		{
			var children = new List<IRegexNode>();

			if (node is RePanel panel)
			{
				children.AddRange(panel.SequenceNodes);
			}
			else if (node is Reline line)
			{
				children.AddRange(line.AlternationNodes);
			}
			else if (node is ReCondition condition)
			{
				if (string.IsNullOrEmpty(condition.conditionGroup))
				{
					children.Add(condition.condition1);
				}
				else
				{
					var aka = condition.conditionGroup2RdName.Length > 0 ? $" AKA: {condition.conditionGroup2RdName}" : "";
					children.Add(new ReSingle($"Group: {condition.conditionGroup}{aka}", SingleType.Backreference));
				}

				children.Add(condition.condition2);

				if (condition.HaveNoBanch)
				{
					children.Add(condition.condition3);
				}
			}

			return children;
		}

		// 打印简化的AST（原始方法，保持兼容性）
		public static void PrintAST(IRegexNode node, int level = 0)
		{
			StringBuilder indent = new StringBuilder(new string(' ', level * 2));

			if (node is ReSingle single)
			{
				string typeStr = GetSingleTypeString(single.singleType);
				string content = EscapeSpecialChars(single.pattern);
				Console.WriteLine($"{indent}{typeStr}: {content}");
			}
			else if (node is RePanel panel)
			{
				if (panel.addBracket)
					Console.WriteLine($"{indent}Capturing Group:");
				else if (panel.GroupingConstruct.Count > 0)
					Console.WriteLine($"{indent}Grouping Construct ({string.Join(",", panel.GroupingConstruct)}):");
				else if (!string.IsNullOrEmpty(panel.Quantifier))
					Console.WriteLine($"{indent}Quantifier [{panel.Quantifier}]:");

				foreach (var child in panel.SequenceNodes)
				{
					PrintAST(child, level + 1);
				}
			}
			else if (node is Reline line)
			{
				Console.WriteLine($"{indent}Alternation:");
				for (int i = 0; i < line.AlternationNodes.Count; i++)
				{
					if (i > 0) Console.WriteLine($"{indent}  |");
					PrintAST(line.AlternationNodes[i], level + 2);
				}
			}
			else if (node is ReCondition condition)
			{
				Console.WriteLine($"{indent}Conditional Expression:");
				if (string.IsNullOrEmpty(condition.conditionGroup))
				{
					Console.WriteLine($"{indent}  Condition:");
					PrintAST(condition.condition1, level + 2);
				}
				else
				{
					Console.WriteLine($"{indent}  Group: {condition.conditionGroup}");
				}

				Console.WriteLine($"{indent}  Yes Branch:");
				PrintAST(condition.condition2, level + 2);

				if (condition.HaveNoBanch)
				{
					Console.WriteLine($"{indent}  No Branch:");
					PrintAST(condition.condition3, level + 2);
				}
			}
		}

		// 新增：彩色树形打印 - 支持条件分支颜色
		public static void PrintColorASTTree(IRegexNode node)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("AST Tree Structure (with Condition Branches):");
			Console.ForegroundColor = ConsoleColor.White;

			var sb = new StringBuilder();
			BuildColorTreeString(node, "", true, sb, false, "");
			Console.WriteLine(sb.ToString());
		}

		private static void BuildColorTreeString(IRegexNode node, string indent, bool isLast, StringBuilder sb, bool isConditionBranch, string branchType)
		{
			// 添加连接线
			sb.Append(indent);
			if (!string.IsNullOrEmpty(indent))
			{
				sb.Append(isLast ? "└── " : "├── ");
			}

			// 根据分支类型设置颜色和前缀
			if (isConditionBranch && !string.IsNullOrEmpty(branchType))
			{
				if (branchType == "yes")
				{
					sb.Append("\u001b[32m(yesBranch) \u001b[0m"); // 绿色 ✓
				}
				else if (branchType == "no")
				{
					sb.Append("\u001b[31m(noBranch) \u001b[0m"); // 红色 ✗
				}
			}

			// 根据节点类型设置颜色
			string nodeLabel = GetNodeLabel(node);
			string coloredLabel = GetColoredNodeLabel(node, nodeLabel);

			sb.Append(coloredLabel);
			sb.AppendLine();

			// 构建新的缩进
			string newIndent = indent + (isLast ? "    " : "│   ");

			// 对于条件节点，需要特殊处理分支颜色
			if (node is ReCondition condition)
			{
				var childrenWithBranchTypes = new List<(IRegexNode node, string branchType)>();

				// 添加条件分支
				if (!string.IsNullOrEmpty(condition.conditionGroup))
				{
					var aka = condition.conditionGroup2RdName.Length > 0 ? $" AKA: {condition.conditionGroup2RdName}" : "";
					childrenWithBranchTypes.Add((new ReSingle($"Group: {condition.conditionGroup}{aka}", SingleType.Backreference), ""));
				}
				else if (condition.condition1 != null)
				{
					childrenWithBranchTypes.Add((condition.condition1, ""));
				}

				// 添加yes分支（绿色）
				childrenWithBranchTypes.Add((condition.condition2, "yes"));

				// 添加no分支（红色，如果有）
				if (condition.HaveNoBanch && condition.condition3 != null)
				{
					childrenWithBranchTypes.Add((condition.condition3, "no"));
				}

				// 递归处理带分支类型的子节点
				for (int i = 0; i < childrenWithBranchTypes.Count; i++)
				{
					bool childIsLast = i == childrenWithBranchTypes.Count - 1;
					var child = childrenWithBranchTypes[i];
					BuildColorTreeString(child.node, newIndent, childIsLast, sb,
									   !string.IsNullOrEmpty(child.branchType), child.branchType);
				}
			}
			else
			{
				// 正常节点的递归处理
				var children = GetNodeChildren(node);
				for (int i = 0; i < children.Count; i++)
				{
					bool childIsLast = i == children.Count - 1;
					BuildColorTreeString(children[i], newIndent, childIsLast, sb, false, "");
				}
			}
		}

		private static string GetColoredNodeLabel(IRegexNode node, string label)
		{
			const string RESET = "\u001b[0m";
			const string GREEN = "\u001b[32m";   // 绿色 - Yes分支
			const string RED = "\u001b[31m";     // 红色 - No分支
			const string BLUE = "\u001b[34m";    // 蓝色 - 串联结构
			const string YELLOW = "\u001b[33m";  // 黄色 - 量词/特殊
			const string MAGENTA = "\u001b[35m"; // 紫色 - 条件/后向引用
			const string CYAN = "\u001b[36m";    // 青色 - 分组构造
			const string GRAY = "\u001b[90m";    // 灰色 - 注释

			// 检查是否是条件分支节点（通过标签判断）
			if (label.Contains("Yes:"))
			{
				return $"{GREEN}{label}{RESET}";
			}
			else if (label.Contains("No:"))
			{
				return $"{RED}{label}{RESET}";
			}

			// 原有颜色逻辑保持不变
			if (node is ReSingle single)
			{
				switch (single.singleType)
				{
					case SingleType.Anchor:
					case SingleType.CharacterClasses:
						return $"{YELLOW}{label}{RESET}";
					case SingleType.Backreference:
						return $"{MAGENTA}{label}{RESET}";
					case SingleType.InlineOptions:
					case SingleType.PatternMiddleOption:
						return $"{CYAN}{label}{RESET}";
					case SingleType.InlineComment:
					case SingleType.EndofLineComment:
						return $"{GRAY}{label}{RESET}";
					default:
						return $"{GREEN}{label}{RESET}";
				}
			}
			else if (node is RePanel panel)
			{
				if (panel.addBracket)
					return $"{BLUE}{label}{RESET}";
				else if (panel.GroupingConstruct.Count > 0)
					return $"{CYAN}{label}{RESET}";
				else
					return $"{YELLOW}{label}{RESET}";
			}
			else if (node is Reline)
			{
				return $"{RED}{label}{RESET}";
			}
			else if (node is ReCondition)
			{
				return $"{MAGENTA}{label}{RESET}";
			}

			return label;
		}

		// 新增：将AST转换为Markdown树形结构（便于文档化）
		public static string ASTToMarkdown(IRegexNode node)
		{
			var sb = new StringBuilder();
			sb.AppendLine("```mermaid");
			sb.AppendLine("graph TD");
			BuildMermaidDiagram(node, "root", sb, new Dictionary<string, int>());
			sb.AppendLine("```");
			return sb.ToString();
		}

		private static void BuildMermaidDiagram(IRegexNode node, string parentId, StringBuilder sb, Dictionary<string, int> nodeCounter)
		{
			string nodeType = node.GetType().Name.Replace("Re", "");
			string nodeId = $"{nodeType}_{(nodeCounter.ContainsKey(nodeType) ? nodeCounter[nodeType] : 0)}";

			if (!nodeCounter.ContainsKey(nodeType))
				nodeCounter[nodeType] = 0;
			nodeCounter[nodeType]++;

			// 添加节点定义
			string label = GetNodeLabel(node).Replace("\"", "'");
			sb.AppendLine($"    {nodeId}[\"{label}\"]");

			// 连接父节点
			if (parentId != "root")
				sb.AppendLine($"    {parentId} --> {nodeId}");

			// 递归处理子节点
			var children = GetNodeChildren(node);
			foreach (var child in children)
			{
				BuildMermaidDiagram(child, nodeId, sb, nodeCounter);
			}
		}

		// 新增：将AST转换为带有CSS样式的Mermaid图
		public static string ASTToStyledMermaid(IRegexNode node)
		{
			var sb = new StringBuilder();
			var styleBuilder = new StringBuilder();
			var nodeDefinitions = new StringBuilder();
			var relationships = new StringBuilder();

			BuildMermaidDiagramWithStyles(node, "root", sb, new Dictionary<string, int>(),
				styleBuilder, nodeDefinitions, relationships);

			// 构建完整的Mermaid图表
			sb.Clear();
			sb.AppendLine("```mermaid");
			sb.AppendLine("graph TD");

			// 添加样式定义
			sb.AppendLine("    %% 节点样式定义");
			sb.AppendLine("    classDef sequence fill:#e6f2ff,stroke:#0066cc,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef alternation fill:#ffe6e6,stroke:#cc0000,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef single fill:#e6ffe6,stroke:#00cc00,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef special fill:#ffffe6,stroke:#cccc00,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef capturing fill:#e6e6ff,stroke:#6600cc,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef grouping fill:#e6ffff,stroke:#006666,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef quantifier fill:#fff0e6,stroke:#cc6600,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef condition fill:#f0e6ff,stroke:#9900cc,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef reference fill:#ffe6f2,stroke:#cc0066,stroke-width:2px,color:#000");
			sb.AppendLine();

			// 添加节点定义和关系
			sb.Append(nodeDefinitions);
			sb.AppendLine();
			sb.Append(relationships);
			sb.AppendLine();

			// 添加样式类应用
			sb.AppendLine("    %% 应用样式类");
			sb.Append(styleBuilder);

			sb.AppendLine("```");
			return sb.ToString();
		}

		private static void BuildMermaidDiagramWithStyles(
			IRegexNode node,
			string parentId,
			StringBuilder sb,
			Dictionary<string, int> nodeCounter,
			StringBuilder styleBuilder,
			StringBuilder nodeDefinitions,
			StringBuilder relationships)
		{
			string nodeType = node.GetType().Name.Replace("Re", "");
			string nodeId = $"{nodeType}_{(nodeCounter.ContainsKey(nodeType) ? nodeCounter[nodeType] : 0)}";

			if (!nodeCounter.ContainsKey(nodeType))
				nodeCounter[nodeType] = 0;
			nodeCounter[nodeType]++;

			// 获取节点标签
			string label = GetNodeLabel(node).Replace("\"", "'");

			// 添加节点定义
			nodeDefinitions.AppendLine($"    {nodeId}[\"{label}\"]");

			// 连接父节点
			if (parentId != "root")
				relationships.AppendLine($"    {parentId} --> {nodeId}");

			// 获取样式类并记录
			string styleClass = GetMermaidNodeStyle(node);
			if (!string.IsNullOrEmpty(styleClass))
				styleBuilder.AppendLine($"    class {nodeId} {styleClass};");

			// 递归处理子节点
			var children = GetNodeChildren(node);
			foreach (var child in children)
			{
				BuildMermaidDiagramWithStyles(child, nodeId, sb, nodeCounter, styleBuilder, nodeDefinitions, relationships);
			}
		}

		private static string GetMermaidNodeStyle(IRegexNode node)
		{
			if (node is RePanel panel)
			{
				if (string.IsNullOrEmpty(panel.Quantifier) && panel.GroupingConstruct.Count == 0 && !panel.addBracket)
					return "sequence";
				else if (panel.addBracket)
					return "capturing";
				else if (panel.GroupingConstruct.Count > 0)
					return "grouping";
				else if (!string.IsNullOrEmpty(panel.Quantifier))
					return "quantifier";
			}
			else if (node is Reline)
			{
				return "alternation";
			}
			else if (node is ReCondition)
			{
				return "condition";
			}
			else if (node is ReSingle single)
			{
				switch (single.singleType)
				{
					case SingleType.Anchor:
					case SingleType.CharacterClasses:
						return "special";
					case SingleType.Backreference:
						return "reference";
					default:
						return "single";
				}
			}

			return "";
		}

		// 新增：生成交互式HTML报告
		public static string ASTToHtml(IRegexNode node, string originalPattern)
		{
			var sb = new StringBuilder();

			sb.AppendLine("<!DOCTYPE html>");
			sb.AppendLine("<html lang=\"en\">");
			sb.AppendLine("<head>");
			sb.AppendLine("    <meta charset=\"UTF-8\">");
			sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
			sb.AppendLine("    <title>Regex AST Visualization</title>");
			sb.AppendLine("    <script src=\"https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js\"></script>");
			sb.AppendLine("    <style>");
			sb.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; }");
			sb.AppendLine("        .container { display: flex; flex-direction: column; gap: 20px; }");
			sb.AppendLine("        .pattern-box { background: #f5f5f5; padding: 15px; border-radius: 5px; }");
			sb.AppendLine("        .mermaid-container {");
			sb.AppendLine("            background: white;");
			sb.AppendLine("            border: 1px solid #ddd;");
			sb.AppendLine("            border-radius: 5px;");
			sb.AppendLine("            padding: 15px;");
			sb.AppendLine("            position: relative;");
			sb.AppendLine("            overflow: hidden;");
			sb.AppendLine("            min-height: 700px;");
			sb.AppendLine("            max-height: 900px;");
			sb.AppendLine("        }");
			sb.AppendLine("        .mermaid-wrapper {");
			sb.AppendLine("            width: 100%;");
			sb.AppendLine("            height: 100%;");
			sb.AppendLine("            overflow: auto;");
			sb.AppendLine("            cursor: move;");
			sb.AppendLine("        }");
			sb.AppendLine("        .mermaid-svg {");
			sb.AppendLine("            transform-origin: 0 0;");
			sb.AppendLine("            transition: transform 0.1s ease;");
			sb.AppendLine("        }");
			sb.AppendLine("        .zoom-controls {");
			sb.AppendLine("            position: absolute;");
			sb.AppendLine("            top: 20px;");
			sb.AppendLine("            right: 20px;");
			sb.AppendLine("            background: white;");
			sb.AppendLine("            border: 1px solid #ddd;");
			sb.AppendLine("            border-radius: 5px;");
			sb.AppendLine("            padding: 10px;");
			sb.AppendLine("            display: flex;");
			sb.AppendLine("            gap: 5px;");
			sb.AppendLine("            box-shadow: 0 2px 5px rgba(0,0,0,0.1);");
			sb.AppendLine("            z-index: 100;");
			sb.AppendLine("        }");
			sb.AppendLine("        .zoom-controls button {");
			sb.AppendLine("            padding: 8px 12px;");
			sb.AppendLine("            background: #f0f0f0;");
			sb.AppendLine("            border: 1px solid #ccc;");
			sb.AppendLine("            border-radius: 3px;");
			sb.AppendLine("            cursor: pointer;");
			sb.AppendLine("            font-size: 14px;");
			sb.AppendLine("        }");
			sb.AppendLine("        .zoom-controls button:hover {");
			sb.AppendLine("            background: #e0e0e0;");
			sb.AppendLine("        }");
			sb.AppendLine("        .zoom-level {");
			sb.AppendLine("            padding: 8px 12px;");
			sb.AppendLine("            min-width: 70px;");
			sb.AppendLine("            text-align: center;");
			sb.AppendLine("            font-size: 14px;");
			sb.AppendLine("            background: #fff;");
			sb.AppendLine("            border: 1px solid #ccc;");
			sb.AppendLine("            border-radius: 3px;");
			sb.AppendLine("        }");
			sb.AppendLine("        .legend { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 10px; }");
			sb.AppendLine("        .legend-item { display: flex; align-items: center; gap: 5px; }");
			sb.AppendLine("        .legend-color { width: 15px; height: 15px; border-radius: 3px; }");
			sb.AppendLine("        .sequence { background: #e6f2ff; border: 2px solid #0066cc; }");
			sb.AppendLine("        .alternation { background: #ffe6e6; border: 2px solid #cc0000; }");
			sb.AppendLine("        .single { background: #e6ffe6; border: 2px solid #00cc00; }");
			sb.AppendLine("        .special { background: #ffffe6; border: 2px solid #cccc00; }");
			sb.AppendLine("        .capturing { background: #e6e6ff; border: 2px solid #6600cc; }");
			sb.AppendLine("        .grouping { background: #e6ffff; border: 2px solid #006666; }");
			sb.AppendLine("        .quantifier { background: #fff0e6; border: 2px solid #cc6600; }");
			sb.AppendLine("        .condition { background: #f0e6ff; border: 2px solid #9900cc; }");
			sb.AppendLine("        .reference { background: #ffe6f2; border: 2px solid #cc0066; }");
			sb.AppendLine("        .yesbranch { background: #a8e6cf; border: 2px solid #00a86b; }");
			sb.AppendLine("        .nobranch { background: #ffb3ba; border: 2px solid #ff0000; }");
			sb.AppendLine("        .instructions {");
			sb.AppendLine("            background: #f8f9fa;");
			sb.AppendLine("            padding: 10px;");
			sb.AppendLine("            border-radius: 5px;");
			sb.AppendLine("            margin: 10px 0;");
			sb.AppendLine("            border-left: 4px solid #007bff;");
			sb.AppendLine("        }");
			sb.AppendLine("        .zoom-controls{opacity:0.01;}");
			sb.AppendLine("        .zoom-controls:hover{opacity:1;}");
			sb.AppendLine("        .mermaid-wrapper{min-width:1920px;min-height:600px;}");
			sb.AppendLine("    </style>");
			sb.AppendLine("</head>");
			sb.AppendLine("<body>");
			sb.AppendLine("    <div class=\"container\">");
			sb.AppendLine($"        <div class=\"pattern-box\"><strong>Original Pattern:</strong> {EscapeHtml(originalPattern)}</div>");

			sb.AppendLine("        <div class=\"instructions\">");
			sb.AppendLine("            <strong>Instructions:</strong>");
			sb.AppendLine("            <ul>");
			sb.AppendLine("                <li>Use mouse wheel to zoom in/out</li>");
			sb.AppendLine("                <li>Drag the diagram with mouse to pan</li>");
			sb.AppendLine("                <li>Use buttons to control zoom level</li>");
			sb.AppendLine("                <li>Condition branches: <span style='color:#00a86b'>Green = Yes</span>, <span style='color:#ff0000'>Red = No</span></li>");
			sb.AppendLine("            </ul>");
			sb.AppendLine("        </div>");

			sb.AppendLine("        <div class=\"legend\"><div class=\"legend-item\"><a href=\"https://github.com/longxya/regexdev\" target=\"_blank\">github</a></div><div class=\"legend-item\"><a href=\"https://regexdev.com\" target=\"_blank\">regexdev</a></div></div>");

			sb.AppendLine("        <div class=\"legend\">");
			sb.AppendLine("            <div class=\"legend-item\"><div class=\"legend-color sequence\"></div><span>Sequence (Concatenation)</span></div>");
			sb.AppendLine("            <div class=\"legend-item\"><div class=\"legend-color alternation\"></div><span>Alternation (|)</span></div>");
			sb.AppendLine("            <div class=\"legend-item\"><div class=\"legend-color single\"></div><span>Character/Token</span></div>");
			sb.AppendLine("            <div class=\"legend-item\"><div class=\"legend-color special\"></div><span>Anchor/Character Class</span></div>");
			sb.AppendLine("            <div class=\"legend-item\"><div class=\"legend-color capturing\"></div><span>Capturing Group</span></div>");
			sb.AppendLine("            <div class=\"legend-item\"><div class=\"legend-color grouping\"></div><span>Grouping Construct</span></div>");
			sb.AppendLine("            <div class=\"legend-item\"><div class=\"legend-color quantifier\"></div><span>Quantifier</span></div>");
			sb.AppendLine("            <div class=\"legend-item\"><div class=\"legend-color condition\"></div><span>Conditional Expression</span></div>");
			sb.AppendLine("            <div class=\"legend-item\"><div class=\"legend-color reference\"></div><span>Backreference</span></div>");
			sb.AppendLine("            <div class=\"legend-item\"><div class=\"legend-color yesbranch\"></div><span>Yes Branch (Condition satisfied)</span></div>");
			sb.AppendLine("            <div class=\"legend-item\"><div class=\"legend-color nobranch\"></div><span>No Branch (Condition not satisfied)</span></div>");
			sb.AppendLine("        </div>");

			sb.AppendLine("        <div class=\"mermaid-container\">");
			sb.AppendLine("            <div class=\"zoom-controls\">");
			sb.AppendLine("                <button onclick=\"zoomIn()\">+</button>");
			sb.AppendLine("                <div class=\"zoom-level\" id=\"zoomLevel\">100%</div>");
			sb.AppendLine("                <button onclick=\"zoomOut()\">-</button>");
			sb.AppendLine("                <button onclick=\"resetZoom()\">Reset</button>");
			sb.AppendLine("                <button onclick=\"downloadSVG()\">Save SVG</button>");
			sb.AppendLine("            </div>");
			sb.AppendLine("            <div class=\"mermaid-wrapper\" id=\"mermaidWrapper\">");
			sb.AppendLine("                <div class=\"mermaid\">");

			// 生成Mermaid图表 - 使用新的条件分支图表生成方法
			var mermaidContent = GenerateMermaidChartWithConditionBranches(node);
			sb.Append(mermaidContent);

			// 添加样式定义
			sb.AppendLine("    classDef sequence fill:#e6f2ff,stroke:#0066cc,stroke-width:2px");
			sb.AppendLine("    classDef alternation fill:#ffe6e6,stroke:#cc0000,stroke-width:2px");
			sb.AppendLine("    classDef single fill:#e6ffe6,stroke:#00cc00,stroke-width:2px");
			sb.AppendLine("    classDef special fill:#ffffe6,stroke:#cccc00,stroke-width:2px");
			sb.AppendLine("    classDef capturing fill:#e6e6ff,stroke:#6600cc,stroke-width:2px");
			sb.AppendLine("    classDef grouping fill:#e6ffff,stroke:#006666,stroke-width:2px");
			sb.AppendLine("    classDef quantifier fill:#fff0e6,stroke:#cc6600,stroke-width:2px");
			sb.AppendLine("    classDef condition fill:#f0e6ff,stroke:#9900cc,stroke-width:2px");
			sb.AppendLine("    classDef reference fill:#ffe6f2,stroke:#cc0066,stroke-width:2px");
			sb.AppendLine("    classDef yesbranch fill:#a8e6cf,stroke:#00a86b,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef nobranch fill:#ffb3ba,stroke:#ff0000,stroke-width:2px,color:#000");

			sb.AppendLine("                </div>");
			sb.AppendLine("            </div>");
			sb.AppendLine("        </div>");
			sb.AppendLine("    </div>");

			sb.AppendLine("    <script>");
			sb.AppendLine("        // 初始化Mermaid");
			sb.AppendLine("        mermaid.initialize({");
			sb.AppendLine("            startOnLoad: true,");
			sb.AppendLine("            theme: 'default',");
			sb.AppendLine("            maxTextSize: 100000000,");
			sb.AppendLine("            flowchart: {");
			sb.AppendLine("                useMaxWidth: false, // 设置为false以便缩放");
			sb.AppendLine("                htmlLabels: true");
			sb.AppendLine("            }");
			sb.AppendLine("        });");
			sb.AppendLine("");
			sb.AppendLine("        // 等待Mermaid渲染完成");
			sb.AppendLine("        setTimeout(initZoomAndDrag, 100);");
			sb.AppendLine("");
			sb.AppendLine("        function initZoomAndDrag() {");
			sb.AppendLine("            // 获取SVG元素");
			sb.AppendLine("            const svg = document.querySelector('.mermaid-wrapper svg');");
			sb.AppendLine("            if (!svg) {");
			sb.AppendLine("                console.warn('SVG not found, retrying...');");
			sb.AppendLine("                setTimeout(initZoomAndDrag, 200);");
			sb.AppendLine("                return;");
			sb.AppendLine("            }");
			sb.AppendLine("");
			sb.AppendLine("            // 添加mermaid-svg类以便样式控制");
			sb.AppendLine("            svg.classList.add('mermaid-svg');");
			sb.AppendLine("            ");
			sb.AppendLine("            // 设置初始SVG尺寸以适应容器");
			sb.AppendLine("            const container = document.querySelector('.mermaid-container');");
			sb.AppendLine("            const wrapper = document.getElementById('mermaidWrapper');");
			sb.AppendLine("            const originalWidth = svg.getBoundingClientRect().width;");
			sb.AppendLine("            const originalHeight = svg.getBoundingClientRect().height;");
			sb.AppendLine("            ");
			sb.AppendLine("            // 设置wrapper的初始尺寸");
			sb.AppendLine("            wrapper.style.width = originalWidth + 'px';");
			sb.AppendLine("            wrapper.style.height = originalHeight + 'px';");
			sb.AppendLine("            ");
			sb.AppendLine("            // 缩放控制变量");
			sb.AppendLine("            let scale = 1;");
			sb.AppendLine("            let translateX = 0;");
			sb.AppendLine("            let translateY = 0;");
			sb.AppendLine("            let isDragging = false;");
			sb.AppendLine("            let startX, startY;");
			sb.AppendLine("            ");
			sb.AppendLine("            // 更新SVG变换");
			sb.AppendLine("            function updateTransform() {");
			sb.AppendLine("                svg.style.transform = `translate(${translateX}px, ${translateY}px) scale(${scale})`;");
			sb.AppendLine("                document.getElementById('zoomLevel').textContent = Math.round(scale * 100) + '%';");
			sb.AppendLine("            }");
			sb.AppendLine("            ");
			sb.AppendLine("            // 缩放函数");
			sb.AppendLine("            window.zoomIn = function() {");
			sb.AppendLine("                if(scale <= 0.091) scale = scale + 0.01");
			sb.AppendLine("                else scale = Math.min(3, scale + 0.05); // 最大放大3倍");
			sb.AppendLine("                updateTransform();");
			sb.AppendLine("            };");
			sb.AppendLine("            ");
			sb.AppendLine("            window.zoomOut = function() {");
			sb.AppendLine("                if(scale < 0.11) scale = Math.max(0.01, scale - 0.01)");
			sb.AppendLine("                else scale = Math.max(0.1, scale - 0.05); // 最小缩小到10%");
			sb.AppendLine("                updateTransform();");
			sb.AppendLine("            };");
			sb.AppendLine("            ");
			sb.AppendLine("            window.resetZoom = function() {");
			sb.AppendLine("                scale = 1;");
			sb.AppendLine("                translateX = 0;");
			sb.AppendLine("                translateY = 0;");
			sb.AppendLine("                updateTransform();");
			sb.AppendLine("            };");
			sb.AppendLine("            ");
			sb.AppendLine("            // 下载SVG");
			sb.AppendLine("            window.downloadSVG = function() {");
			sb.AppendLine("                if (!svg) return;");
			sb.AppendLine("                ");
			sb.AppendLine("                // 克隆SVG以避免修改原图");
			sb.AppendLine("                const clonedSvg = svg.cloneNode(true);");
			sb.AppendLine("                ");
			sb.AppendLine("                // 移除变换样式，保存原始大小");
			sb.AppendLine("                clonedSvg.removeAttribute('style');");
			sb.AppendLine("                ");
			sb.AppendLine("                // 序列化SVG");
			sb.AppendLine("                const serializer = new XMLSerializer();");
			sb.AppendLine("                let source = serializer.serializeToString(clonedSvg);");
			sb.AppendLine("                ");
			sb.AppendLine("                // 添加XML声明");
			sb.AppendLine("                if (!source.match(/^<\\?xml/)) {");
			sb.AppendLine("                    source = '<?xml version=\"1.0\" encoding=\"UTF-8\"?>\\n' + source;");
			sb.AppendLine("                }");
			sb.AppendLine("                ");
			sb.AppendLine("                // 创建下载链接");
			sb.AppendLine("                const blob = new Blob([source], { type: 'image/svg+xml' });");
			sb.AppendLine("                const url = URL.createObjectURL(blob);");
			sb.AppendLine("                const a = document.createElement('a');");
			sb.AppendLine("                a.href = url;");
			sb.AppendLine("                a.download = 'regex-ast-diagram.svg';");
			sb.AppendLine("                document.body.appendChild(a);");
			sb.AppendLine("                a.click();");
			sb.AppendLine("                document.body.removeChild(a);");
			sb.AppendLine("                URL.revokeObjectURL(url);");
			sb.AppendLine("            };");
			sb.AppendLine("            ");
			sb.AppendLine("            // 鼠标滚轮缩放");
			sb.AppendLine("            wrapper.addEventListener('wheel', function(e) {");
			sb.AppendLine("                e.preventDefault();");
			sb.AppendLine("                ");
			sb.AppendLine("                // 计算鼠标相对于SVG的位置");
			sb.AppendLine("                const rect = svg.getBoundingClientRect();");
			sb.AppendLine("                const mouseX = e.clientX - rect.left;");
			sb.AppendLine("                const mouseY = e.clientY - rect.top;");
			sb.AppendLine("                ");
			sb.AppendLine("                // 计算缩放前的鼠标位置在SVG坐标系中的位置");
			sb.AppendLine("                const svgX = (mouseX - translateX) / scale;");
			sb.AppendLine("                const svgY = (mouseY - translateY) / scale;");
			sb.AppendLine("                ");
			sb.AppendLine("                // 更新缩放比例");
			sb.AppendLine("                let delta = e.deltaY > 0 ? -0.05 : 0.05;");
			sb.AppendLine("                if(delta < 0&&scale < 0.11) delta = -0.01");
			sb.AppendLine("                if(delta > 0&&scale <= 0.091) delta = 0.01");
			sb.AppendLine("                const newScale = Math.max(0.01, Math.min(3, scale + delta));");
			sb.AppendLine("                ");
			sb.AppendLine("                // 计算缩放后的平移，使鼠标位置保持不变");
			sb.AppendLine("                translateX = mouseX - svgX * newScale;");
			sb.AppendLine("                translateY = mouseY - svgY * newScale;");
			sb.AppendLine("                scale = newScale;");
			sb.AppendLine("                ");
			sb.AppendLine("                updateTransform();");
			sb.AppendLine("            });");
			sb.AppendLine("            ");
			sb.AppendLine("            // 鼠标拖拽");
			sb.AppendLine("            wrapper.addEventListener('mousedown', function(e) {");
			sb.AppendLine("                if (e.button === 0) { // 左键");
			sb.AppendLine("                    isDragging = true;");
			sb.AppendLine("                    startX = e.clientX - translateX;");
			sb.AppendLine("                    startY = e.clientY - translateY;");
			sb.AppendLine("                    wrapper.style.cursor = 'grabbing';");
			sb.AppendLine("                    e.preventDefault();");
			sb.AppendLine("                }");
			sb.AppendLine("            });");
			sb.AppendLine("            ");
			sb.AppendLine("            wrapper.addEventListener('mousemove', function(e) {");
			sb.AppendLine("                if (isDragging) {");
			sb.AppendLine("                    translateX = e.clientX - startX;");
			sb.AppendLine("                    translateY = e.clientY - startY;");
			sb.AppendLine("                    updateTransform();");
			sb.AppendLine("                }");
			sb.AppendLine("            });");
			sb.AppendLine("            ");
			sb.AppendLine("            wrapper.addEventListener('mouseup', function() {");
			sb.AppendLine("                isDragging = false;");
			sb.AppendLine("                wrapper.style.cursor = 'move';");
			sb.AppendLine("            });");
			sb.AppendLine("            ");
			sb.AppendLine("            wrapper.addEventListener('mouseleave', function() {");
			sb.AppendLine("                isDragging = false;");
			sb.AppendLine("                wrapper.style.cursor = 'move';");
			sb.AppendLine("            });");
			sb.AppendLine("            ");
			sb.AppendLine("            // 初始化显示");
			sb.AppendLine("            updateTransform();");
			sb.AppendLine("        }");
			sb.AppendLine("    </script>");
			sb.AppendLine("</body>");
			sb.AppendLine("</html>");

			return sb.ToString();
		}

		private static string EscapeHtml(string input)
		{
			if (string.IsNullOrEmpty(input)) return "";
			return WebUtility.HtmlEncode(input);
			return input.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
		}

		// 新增：带有条件分支的Mermaid图表生成
		public static string GenerateMermaidChartWithConditionBranches(IRegexNode node)
		{
			var sb = new StringBuilder();
			var styleBuilder = new StringBuilder();
			var nodeDefinitions = new StringBuilder();
			var relationships = new StringBuilder();
			var nodeCounter = new Dictionary<string, int>();

			// 样式定义
			sb.AppendLine("graph TD");
			sb.AppendLine("    %% 节点样式定义");
			sb.AppendLine("    classDef sequence fill:#e6f2ff,stroke:#0066cc,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef alternation fill:#ffe6e6,stroke:#cc0000,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef single fill:#e6ffe6,stroke:#00cc00,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef special fill:#ffffe6,stroke:#cccc00,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef capturing fill:#e6e6ff,stroke:#6600cc,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef grouping fill:#e6ffff,stroke:#006666,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef quantifier fill:#fff0e6,stroke:#cc6600,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef condition fill:#f0e6ff,stroke:#9900cc,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef reference fill:#ffe6f2,stroke:#cc0066,stroke-width:2px,color:#000");
			sb.AppendLine("    classDef yesbranch fill:#a8e6cf,stroke:#00a86b,stroke-width:2px,color:#000"); // 绿色 - Yes分支
			sb.AppendLine("    classDef nobranch fill:#ffb3ba,stroke:#ff0000,stroke-width:2px,color:#000"); // 红色 - No分支
			sb.AppendLine();

			// 生成图表内容
			BuildMermaidChartRecursive(node, "root", nodeCounter, styleBuilder, nodeDefinitions, relationships);

			// 添加节点定义
			sb.Append(nodeDefinitions);
			sb.AppendLine();

			// 添加关系
			sb.Append(relationships);
			sb.AppendLine();

			// 添加样式类应用
			sb.AppendLine("    %% 应用样式类");
			sb.Append(styleBuilder);

			return sb.ToString();
		}

		// 修改：生成Mermaid图表时正确处理条件表达式
		private static void BuildMermaidChartRecursive(
			IRegexNode node,
			string parentId,
			Dictionary<string, int> nodeCounter,
			StringBuilder styleBuilder,
			StringBuilder nodeDefinitions,
			StringBuilder relationships,string relationshipMiddleString = "")
		{
			string nodeType = node.GetType().Name.Replace("Re", "");
			string nodeId = $"{nodeType}_{(nodeCounter.ContainsKey(nodeType) ? nodeCounter[nodeType] : 0)}";

			if (!nodeCounter.ContainsKey(nodeType))
				nodeCounter[nodeType] = 0;
			nodeCounter[nodeType]++;

			// 获取节点标签
			string label = GetNodeLabel(node);//.Replace("\"", "&quot;");
			label = EscapeHtml(label);

			// 添加节点定义
			nodeDefinitions.AppendLine($"    {nodeId}[\"{label}\"]");

			// 连接父节点（如果不是根节点）
			if (parentId != "root")
				relationships.AppendLine($"    {parentId} {(relationshipMiddleString == ""?"": $"--{relationshipMiddleString}")}--> {nodeId}");

			// 获取并应用样式类
			string styleClass = GetMermaidNodeStyle(node);
			if (!string.IsNullOrEmpty(styleClass))
				styleBuilder.AppendLine($"    class {nodeId} {styleClass}");

			// 特殊处理条件表达式节点
			if (node is ReCondition condition)
			{
				// 生成条件判断节点（菱形）
				string decisionNodeId = $"Decision_{nodeId}";
				string decisionLabel = "";

				if (!string.IsNullOrEmpty(condition.conditionGroup))
				{
					var aka = condition.conditionGroup2RdName.Length > 0 ? $" (AKA: {condition.conditionGroup2RdName})" : "";
					decisionLabel = $"\"Group {condition.conditionGroup}{aka} captured?\"";
				}
				else
				{
					BuildMermaidChartRecursive(condition.condition1, decisionNodeId, nodeCounter, styleBuilder, nodeDefinitions, relationships,
						"Condition Express");

					decisionLabel = "Condition satisfied?";
				}

				// 使用菱形表示决策节点
				nodeDefinitions.AppendLine($"    {decisionNodeId}{{{decisionLabel}}}");
				styleBuilder.AppendLine($"    class {decisionNodeId} condition");
				relationships.AppendLine($"    {nodeId} --> {decisionNodeId}");

				// 生成Yes分支
				string yesNodeId = $"Yes_{nodeId}";
				nodeDefinitions.AppendLine($"    {yesNodeId}[\"Yes (Branch)\"]");
				styleBuilder.AppendLine($"    class {yesNodeId} yesbranch"); // 使用绿色表示Yes分支
				relationships.AppendLine($"    {decisionNodeId} -- \"Yes\" --> {yesNodeId}");

				// 递归构建condition2（Yes分支）
				if (condition.condition2 != null)
				{
					BuildConditionBranchRecursive(
						condition.condition2,
						yesNodeId,
						nodeCounter,
						styleBuilder,
						nodeDefinitions,
						relationships,
						"yes");
				}

				// 生成No分支（如果有）
				if (condition.HaveNoBanch && condition.condition3 != null)
				{
					string noNodeId = $"No_{nodeId}";
					nodeDefinitions.AppendLine($"    {noNodeId}[\"No (Branch)\"]");
					styleBuilder.AppendLine($"    class {noNodeId} nobranch"); // 使用红色表示No分支
					relationships.AppendLine($"    {decisionNodeId} -- \"No\" --> {noNodeId}");

					// 递归构建condition3（No分支）
					BuildConditionBranchRecursive(
						condition.condition3,
						noNodeId,
						nodeCounter,
						styleBuilder,
						nodeDefinitions,
						relationships,
						"no");
				}
			}
			else
			{
				// 其他节点的正常处理
				var children = GetNodeChildren(node);
				foreach (var child in children)
				{
					BuildMermaidChartRecursive(child, nodeId, nodeCounter, styleBuilder, nodeDefinitions, relationships);
				}
			}
		}

		// 辅助方法：构建条件分支
		private static void BuildConditionBranchRecursive(
			IRegexNode node,
			string parentId,
			Dictionary<string, int> nodeCounter,
			StringBuilder styleBuilder,
			StringBuilder nodeDefinitions,
			StringBuilder relationships,
			string branchType)
		{
			string nodeType = node.GetType().Name.Replace("Re", "");
			string nodeId = $"{nodeType}_{branchType}_{(nodeCounter.ContainsKey($"{nodeType}_{branchType}") ? nodeCounter[$"{nodeType}_{branchType}"] : 0)}";

			if (!nodeCounter.ContainsKey($"{nodeType}_{branchType}"))
				nodeCounter[$"{nodeType}_{branchType}"] = 0;
			nodeCounter[$"{nodeType}_{branchType}"]++;

			// 获取节点标签
			string label = GetNodeLabel(node);//.Replace("\"", "&quot;");
			label = EscapeHtml(label);

			// 根据分支类型添加前缀
			if (branchType == "yes")
				label = $"Yes: {label}";
			else if (branchType == "no")
				label = $"No: {label}";

			// 添加节点定义
			nodeDefinitions.AppendLine($"    {nodeId}[\"{label}\"]");

			// 连接父节点
			relationships.AppendLine($"    {parentId} --> {nodeId}");

			// 根据分支类型应用样式类
			string styleClass = branchType == "yes" ? "yesbranch" : "nobranch";
			styleBuilder.AppendLine($"    class {nodeId} {styleClass}");

			// 递归处理子节点
			var children = GetNodeChildren(node);
			foreach (var child in children)
			{
				BuildMermaidChartRecursive(child, nodeId, nodeCounter, styleBuilder, nodeDefinitions, relationships);
			}
		}

		// 原始Mermaid图表生成方法（保持兼容性）
		private static string GenerateMermaidChart(IRegexNode node)
		{
			return GenerateMermaidChartWithConditionBranches(node);
		}
	}
}
namespace RegexDebug.RegexDev
{
	/// <summary>
	/// as a container of Concatenation/Sequence regex nodes, 
	/// may have a number group`(Sequence Nodes)`, or a Grouping Construct`(?'name'Sequence Nodes)`, or a Quantifier`SequenceNodes{num1,num2}`, `\d+`,  or `\d\w`(it's two regex node)
	/// </summary>
	internal class RePanel : IRegexNode
	{
		public List<IRegexNode> SequenceNodes;

		public string Quantifier { get; set; } = "";

		/// <summary>
		/// if or not add parentheses to form a numbering group
		/// </summary>
		public bool addBracket { get; set; } = false;

		public int GroupingNumber { get; set; }

		public string CaptureGroup2rdName { get; set; } = "";//used to record the aka name of capture group, only valid when Type is GroupingConstruct and is a capture group
		public string BalancingGroup2rdName { get; set; } = "";//used to record the aka name of balancing group name, only valid when Type is GroupingConstruct and is a balancing group

		/// <summary>
		/// The grouping constructs used, such as "?>", "?+i-M+n-S+x+IIIMMM", "?:", "?=", "?!", "?<=", "?<!", "?<name1-name2>", etc.
		/// </summary>
		public List<string> GroupingConstruct { get; set; } = new List<string>();

		/// <summary>
		/// whether or not is conditional test panel. if is (c1) of `(?(c1)yes|no)`
		/// if yes, then addBracket must be true, and this bracket is not a number capturing group
		/// </summary>
		public bool iscondition { get; set; } = false;

		public RePanel(IRegexNode deduce, string num)
		{
			SequenceNodes = new List<IRegexNode>();
			SequenceNodes.Add(deduce);
			Quantifier = num;
		}

		public RePanel(List<IRegexNode> list, string num)
		{
			this.SequenceNodes = list;
			Quantifier = num;
		}
	}
}

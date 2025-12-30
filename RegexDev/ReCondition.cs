namespace RegexDebug.RegexDev
{
	/// <summary>
	/// as a conditional regex node, (?(c1)yes|no)
	/// </summary>
	internal class ReCondition : IRegexNode
	{
		public RePanel condition1 { get; set; }
		public RePanel condition2 { get; set; }
		public RePanel condition3 { get; set; }
		public string conditionGroup { get; set; } = "";

		public string conditionGroup2RdName { get; set; } = "";

		/// <summary>
		/// (?(?=1)y|n)(this number group will be Invalidated)
		/// </summary>
		public bool InvalidateNearestNumberedGroup { get; set; } = false;

		/// <summary>
		/// if or not have the 'no' branch
		/// </summary>
		public bool HaveNoBanch { get; set; } = true;

		public (bool c1Cover, bool c2Cover, bool c3Cover, string c3AddOptions) pattern2dotNET5 { get; set; } = (false, false, false, "");

		public ReCondition(RePanel c1, RePanel c2, RePanel c3, string matchtimes)
		{
			condition1 = c1; condition1.iscondition = true;
			condition2 = c2;
			condition3 = c3;
		}

		public ReCondition(string group, RePanel c2, RePanel c3, string matchtimes)
		{
			this.conditionGroup = group;
			condition2 = c2;
			condition3 = c3;
		}
	}
}

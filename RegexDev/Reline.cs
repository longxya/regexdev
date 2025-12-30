namespace RegexDebug.RegexDev
{
	/// <summary>
	/// as a container of Alternation regex nodes
	/// </summary>
	internal class Reline : IRegexNode
	{
		public List<IRegexNode> AlternationNodes { get; set; }

		public Reline(List<IRegexNode> regularDeduces) => this.AlternationNodes = regularDeduces.ToList();
	}
}

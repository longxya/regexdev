namespace RegexDebug.RegexDev
{
	/// <summary>
	/// as a single regex node, such as a character, a character class, a back reference, an anchor, etc.
	/// </summary>
	internal class ReSingle : IRegexNode
	{
		public SingleType singleType = SingleType.Default;

		public string pattern { get; set; } = "";

		/// <summary>
		/// Is it a back reference
		/// \number Used to mark whether it is a back reference in this situation
		/// </summary>
		public bool? IsReference { get; set; } = null;

		public ReSingle() { }

		public ReSingle(string content, SingleType type = SingleType.Default)
		{
			this.pattern = content;

			this.singleType = type;
		}
	}

	public enum SingleType
	{
		Default,
		CharacterGroup,
		Backreference,
		PatternMiddleOption,
		InlineComment,
		Anchor,
		InlineOptions,
		CharacterClasses,
		EndofLineComment
	}
}

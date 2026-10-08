using System.IO;

namespace ExCSS
{
    /// <summary>
    /// The matches-any pseudo-class <c>:is(S)</c> / <c>:matches(S)</c> / <c>:where(S)</c> - matches an
    /// element that matches any selector in S. <c>:is()</c> is the current name, <c>:matches()</c> the
    /// legacy alias, and <c>:where()</c> the zero-specificity form; all share this class, and
    /// <see cref="Keyword"/> controls which form it round-trips to.
    /// </summary>
    public sealed class MatchesSelector : StylesheetNode, ISelector
    {
        public MatchesSelector(ISelector inner, string keyword)
        {
            Inner = inner;
            Keyword = keyword;
        }

        public ISelector Inner { get; }

        public string Keyword { get; }

        // The specificity of :is()/:matches() is the specificity of its most specific argument (CSS
        // Selectors 4 16.1), which ListSelector.Specificity supplies as the static max for a list argument.
        // :where() is always zero.
        public Priority Specificity =>
            Keyword.Equals(PseudoClassNames.Where, System.StringComparison.OrdinalIgnoreCase) ? Priority.Zero : Inner.Specificity;

        public string Text => this.ToCss();

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            writer.Write(':');
            writer.Write(Keyword);
            writer.Write('(');
            writer.Write(Inner.Text);
            writer.Write(')');
        }
    }
}

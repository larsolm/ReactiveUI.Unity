using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExCSS
{
    public sealed class StyleRule : Rule, IStyleRule
    {
        // CSS Nesting: rules written inside this rule's block, in source order. A style rule is
        // resolved to an absolute selector against this parent; a grouping at-rule (@media) keeps its
        // own condition and holds an implicit rule carrying this parent's selector. Kept in a
        // dedicated list (not Children) so Selector/Style lookups and ToCss are unaffected.
        // Populated by StylesheetComposer.FillDeclarations.
        private readonly List<IRule> _nestedRules = new List<IRule>();

        public StyleRule(StylesheetParser parser) : base(RuleType.Style, parser)
        {
            AppendChild(AllSelector.Create());
            AppendChild(new StyleDeclaration(this));
        }

        public IReadOnlyList<IRule> NestedRules => _nestedRules;

        public void AddNestedRule(IRule rule) => _nestedRules.Add(rule);

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            writer.Write(formatter.Style(SelectorText, Style));
        }

        public ISelector Selector
        {
            get => Children.OfType<ISelector>().FirstOrDefault();
            set => ReplaceSingle(Selector, value);
        }

        public string SelectorText
        {
            get => Selector.Text;
            set => Selector = Parser.ParseSelector(value);
        }

        public StyleDeclaration Style => Children.OfType<StyleDeclaration>().FirstOrDefault();
    }
}
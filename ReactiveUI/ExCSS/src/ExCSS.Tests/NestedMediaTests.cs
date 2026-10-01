using System.Linq;
using Xunit;

namespace ExCSS.Tests
{
    /// <summary>
    /// CSS Nesting (<see href="https://www.w3.org/TR/css-nesting-1/">CSS Nesting 1</see>) applied to
    /// grouping at-rules: an <c>@media</c> written inside a style rule's block. Per the spec the block's
    /// own declarations belong to an implicit <c>&amp;</c> rule, so the media rule holds a synthetic
    /// style rule carrying the parent's selector, and anything written inside resolves against it.
    /// </summary>
    public class NestedMediaTests : CssConstructionFunctions
    {
        private static StyleRule ParseRule(string source) =>
            (StyleRule)ParseStyleSheet(source).Rules.First();

        [Fact]
        public void BareDeclarationsBecomeAnImplicitParentRule()
        {
            // `.card { @media (…) { padding: 1rem; } }` == `@media (…) { .card { padding: 1rem; } }`.
            var rule = ParseRule(".card { padding: 2rem; @media (max-width: 40rem) { padding: 1rem; } }");

            var media = Assert.IsAssignableFrom<IMediaRule>(Assert.Single(rule.NestedRules));
            Assert.Equal("(max-width: 40rem)", media.Media.MediaText);

            var implicitRule = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(media.Rules));
            Assert.Equal(".card", implicitRule.SelectorText);
            Assert.NotEqual(string.Empty, implicitRule.Style["padding"]);

            // The parent keeps its own declaration; only the conditional one moved.
            Assert.NotEqual(string.Empty, rule.Style["padding"]);
        }

        [Fact]
        public void StyleRuleInsideNestedMediaResolvesAgainstParent()
        {
            // The implicit rule carries the parent's selector, so ordinary nesting resolution applies.
            var rule = ParseRule(".card { @media (max-width: 40rem) { .title { color: #0000ff; } } }");

            var media = Assert.IsAssignableFrom<IMediaRule>(Assert.Single(rule.NestedRules));
            var implicitRule = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(media.Rules));
            var inner = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(implicitRule.NestedRules));

            Assert.Equal(":is(.card) .title", inner.SelectorText);
        }

        [Fact]
        public void NestedMediaInsideNestedMedia()
        {
            // Two conditions are a logical AND; each keeps its own prelude, and the inner one hangs off
            // the outer one's implicit rule.
            var rule = ParseRule(
                ".card { @media (max-width: 40rem) { @media (orientation: portrait) { padding: 1rem; } } }");

            var outer = Assert.IsAssignableFrom<IMediaRule>(Assert.Single(rule.NestedRules));
            var outerImplicit = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(outer.Rules));
            var inner = Assert.IsAssignableFrom<IMediaRule>(Assert.Single(outerImplicit.NestedRules));

            Assert.Equal("(max-width: 40rem)", outer.Media.MediaText);
            Assert.Equal("(orientation: portrait)", inner.Media.MediaText);

            var innerImplicit = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(inner.Rules));
            Assert.Equal(".card", innerImplicit.SelectorText);
            Assert.NotEqual(string.Empty, innerImplicit.Style["padding"]);
        }

        [Fact]
        public void DeclarationAfterNestedMediaStillApplies()
        {
            // The regression the @page margin-box comment in FillDeclarations warns about: a stale token
            // after the block drops everything that follows it. Here the later blue must win.
            var rule = ParseRule(
                ".card { color: #ff0000; @media (max-width: 40rem) { color: #00ff00; } color: #0000ff; }");
            var blue = ParseRule("x { color: #0000ff; }").Style["color"];

            Assert.Equal(blue, rule.Style["color"]);
            Assert.Single(rule.NestedRules);
        }

        [Fact]
        public void NestedRulesAreInSourceOrder()
        {
            // Document order across kinds is the cascade's tie-break, which is why nested style rules and
            // nested at-rules share one list rather than living in two.
            var rule = ParseRule(
                ".card { .a { color: #ff0000; } @media (max-width: 40rem) { color: #00ff00; } .b { color: #0000ff; } }");

            Assert.Equal(3, rule.NestedRules.Count);
            Assert.IsAssignableFrom<IStyleRule>(rule.NestedRules[0]);
            Assert.IsAssignableFrom<IMediaRule>(rule.NestedRules[1]);
            Assert.IsAssignableFrom<IStyleRule>(rule.NestedRules[2]);

            Assert.Equal(":is(.card) .a", ((IStyleRule)rule.NestedRules[0]).SelectorText);
            Assert.Equal(":is(.card) .b", ((IStyleRule)rule.NestedRules[2]).SelectorText);
        }

        [Fact]
        public void TopLevelMediaIsUnaffected()
        {
            var sheet = ParseStyleSheet("@media (min-width: 40rem) { .card { padding: 1rem; } }");
            var media = Assert.IsAssignableFrom<IMediaRule>(Assert.Single(sheet.Rules));
            var inner = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(media.Rules));

            Assert.Equal(".card", inner.SelectorText);
            Assert.Empty(inner.NestedRules);
        }

        [Theory]
        [InlineData("(width: 40rem)", "width", MediaFeatureComparison.Equal, false, false)]
        [InlineData("(min-width: 40rem)", "min-width", MediaFeatureComparison.Equal, true, false)]
        [InlineData("(max-width: 40rem)", "max-width", MediaFeatureComparison.Equal, false, true)]
        [InlineData("(width > 40rem)", "width", MediaFeatureComparison.GreaterThan, false, false)]
        [InlineData("(width >= 40rem)", "width", MediaFeatureComparison.GreaterThanOrEqual, false, false)]
        [InlineData("(width < 40rem)", "width", MediaFeatureComparison.LessThan, false, false)]
        [InlineData("(width <= 40rem)", "width", MediaFeatureComparison.LessThanOrEqual, false, false)]
        public void ComparisonIsExposed(
            string query, string name, MediaFeatureComparison comparison, bool isMinimum, bool isMaximum)
        {
            // The colon form's direction lives in the name prefix; the range form's lives in the
            // delimiter, which is otherwise not reachable from outside the parser.
            var sheet = ParseStyleSheet("@media " + query + " { .card { padding: 1rem; } }");
            var media = Assert.IsAssignableFrom<IMediaRule>(Assert.Single(sheet.Rules));
            var feature = Assert.Single(Assert.Single(media.Media.Media).Features);

            Assert.Equal(name, feature.Name);
            Assert.Equal("40rem", feature.Value);
            Assert.Equal(comparison, feature.Comparison);
            Assert.Equal(isMinimum, feature.IsMinimum);
            Assert.Equal(isMaximum, feature.IsMaximum);
        }

        [Fact]
        public void ValuelessFeatureDoesNotCollapseTheQueryToNotAll()
        {
            // FillMediaList replaces an unreadable prelude with `not all`, which INVERTS rather than
            // drops: a query that silently matched everything would be the worst possible outcome. A
            // boolean-context feature must not trigger it.
            var sheet = ParseStyleSheet(
                "@media (hover) { .card { padding: 1rem; } }", tolerateInvalidConstraints: true);
            var media = Assert.IsAssignableFrom<IMediaRule>(Assert.Single(sheet.Rules));
            var medium = Assert.Single(media.Media.Media);

            Assert.False(medium.IsInverse);
            Assert.Equal("hover", Assert.Single(medium.Features).Name);
        }
    }
}

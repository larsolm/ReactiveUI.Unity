using System.Linq;
using Xunit;

namespace ExCSS.Tests
{
    /// <summary>
    /// Custom pseudo-classes: names a host registers through the parser's <c>customPseudoClasses</c>
    /// argument, which then parse as ordinary pseudo-classes rather than invalidating the selector.
    /// A UI framework with states of its own (<c>:enter</c>/<c>:exit</c> transitions, say) needs them
    /// to score as a class and to round-trip through <c>SelectorText</c> — the latter being what makes
    /// them usable inside a nested rule, where the resolved selector is a synthesized string with no
    /// source span for an <see cref="UnknownSelector"/> to echo back.
    /// </summary>
    public class CustomPseudoClassTests : CssConstructionFunctions
    {
        private static readonly string[] Custom = { "enter", "exit" };

        private static StyleRule ParseWithCustom(string source) =>
            (StyleRule)ParseStyleSheet(source, tolerateInvalidSelectors: true, customPseudoClasses: Custom)
                .Rules.First();

        [Fact]
        public void RegisteredPseudoClassParsesFlat()
        {
            var rule = ParseWithCustom(".modal:enter { opacity: 0; }");

            Assert.Equal(".modal:enter", rule.SelectorText);
        }

        [Fact]
        public void RegisteredPseudoClassScoresAsAClass()
        {
            // Same weight as `.modal:hover` — one class for `.modal`, one for the pseudo-class.
            var rule = ParseWithCustom(".modal:enter { opacity: 0; }");

            Assert.Equal(new Priority(0, 0, 2, 0), rule.Selector.Specificity);
        }

        [Fact]
        public void RegisteredPseudoClassSurvivesNesting()
        {
            // The regression this feature exists for: unregistered, the nested rule resolved to an
            // UnknownSelector whose text was empty, so the rule silently styled nothing.
            var rule = ParseWithCustom(".modal { color: #ff0000; &:enter { opacity: 0; } }");
            var nested = (IStyleRule)Assert.Single(rule.NestedRules);

            Assert.Equal(":is(.modal):enter", nested.SelectorText);
            Assert.Equal(new Priority(0, 0, 2, 0), nested.Selector.Specificity);
        }

        [Fact]
        public void RegisteredPseudoClassIsRecognisedCaseInsensitively()
        {
            var rule = ParseWithCustom(".modal:ENTER { opacity: 0; }");

            Assert.Equal(".modal:enter", rule.SelectorText);
        }

        [Fact]
        public void RegisteredPseudoClassWorksInsideIs()
        {
            var rule = ParseWithCustom(":is(.a:enter) .b { opacity: 0; }");

            Assert.Equal(":is(.a:enter) .b", rule.SelectorText);
        }

        [Fact]
        public void RegisteredPseudoClassWorksInASelectorList()
        {
            var rule = ParseWithCustom(".a:enter, .b:exit { opacity: 0; }");

            Assert.Equal(".a:enter,.b:exit", rule.SelectorText);
        }

        [Fact]
        public void UnregisteredPseudoClassIsStillInvalid()
        {
            // The point of registering names rather than accepting any: a typo stays an error, and a
            // nested rule carrying one still resolves to an empty UnknownSelector.
            var rule = ParseWithCustom(".modal { color: #ff0000; &:bogus { opacity: 0; } }");
            var nested = (IStyleRule)Assert.Single(rule.NestedRules);

            Assert.Equal(string.Empty, nested.SelectorText);
        }

        [Fact]
        public void WithoutRegistrationTheNameIsInvalid()
        {
            var sheet = ParseStyleSheet(
                ".modal { color: #ff0000; &:enter { opacity: 0; } }", tolerateInvalidSelectors: true);
            var rule = (StyleRule)sheet.Rules.First();
            var nested = (IStyleRule)Assert.Single(rule.NestedRules);

            Assert.Equal(string.Empty, nested.SelectorText);
        }

        [Fact]
        public void StandardPseudoClassesAreUnaffected()
        {
            var rule = ParseWithCustom(".modal { &:hover { opacity: 0; } }");
            var nested = (IStyleRule)Assert.Single(rule.NestedRules);

            Assert.Equal(":is(.modal):hover", nested.SelectorText);
        }

        [Fact]
        public void LegacySingleColonPseudoElementsStillResolve()
        {
            // Guards the move of the `:before`/`:after`/`:first-line`/`:first-letter` registrations out
            // of the lazy singleton, which the parser no longer touches now that it builds its own
            // factory per parse.
            var rule = ParseWithCustom(".legacy:before { opacity: 0; }");

            Assert.Equal(".legacy::before", rule.SelectorText);
        }
    }
}

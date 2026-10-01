using System.Collections.Generic;

namespace ExCSS
{
    internal struct ParserOptions
    {
        /// <summary>
        /// Selectors for the pseudo-class names a host registered beyond the standard set, keyed by
        /// name without the leading colon and looked up case-insensitively.
        /// </summary>
        /// <remarks>
        /// A host with pseudo-classes of its own — a UI framework carrying <c>:enter</c>/<c>:exit</c>
        /// transition states, say — registers them so they parse as ordinary pseudo-classes: scoring as
        /// a class, and round-tripping through <c>SelectorText</c> whether written flat or nested.
        /// Deliberately a list of names rather than a blanket "accept anything", so a misspelt
        /// <c>:hoverr</c> is still an error. Each selector is built once with the registered spelling,
        /// so <c>:ENTER</c> writes itself back out as the <c>:enter</c> that was registered.
        /// </remarks>
        public IDictionary<string, ISelector> CustomPseudoClasses { get; set; }

        public bool IncludeUnknownRules { get; set; }
        public bool IncludeUnknownDeclarations { get; set; }
        public bool AllowInvalidSelectors { get; set; }
        public bool AllowInvalidValues { get; set; }
        public bool AllowInvalidConstraints { get; set; }
        public bool PreserveComments { get; set; }
        public bool PreserveDuplicateProperties { get; set; }
    }
}
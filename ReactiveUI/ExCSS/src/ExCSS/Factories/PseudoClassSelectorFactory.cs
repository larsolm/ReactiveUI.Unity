using System;
using System.Collections.Generic;
using System.Linq;

namespace ExCSS
{
    public sealed class PseudoClassSelectorFactory
    {
        private static readonly Lazy<PseudoClassSelectorFactory> Lazy =
            new(() => new PseudoClassSelectorFactory());

        private readonly StylesheetParser _parser;

        /// <summary>
        /// Adds the single-colon spellings of the four original pseudo-elements.
        /// </summary>
        /// <remarks>
        /// These live in a static constructor rather than in the lazy singleton's factory, because the
        /// parser now builds an instance of its own per parse and would otherwise never trigger the
        /// singleton — leaving <c>:before</c> and friends unrecognised.
        /// </remarks>
        static PseudoClassSelectorFactory()
        {
            Selectors.Add(PseudoElementNames.Before,
                PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.Before));
            Selectors.Add(PseudoElementNames.After,
                PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.After));
            Selectors.Add(PseudoElementNames.FirstLine,
                PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.FirstLine));
            Selectors.Add(PseudoElementNames.FirstLetter,
                PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.FirstLetter));
        }

        internal PseudoClassSelectorFactory(StylesheetParser parser = null)
        {
            _parser = parser;
        }

        #region Selectors

        private static readonly Dictionary<string, ISelector> Selectors =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PseudoClassNames.Root,
                    PseudoClassNames.Scope,
                    PseudoClassNames.OnlyType,
                    PseudoClassNames.FirstOfType,
                    PseudoClassNames.LastOfType,
                    PseudoClassNames.OnlyChild,
                    PseudoClassNames.FirstChild,
                    PseudoClassNames.LastChild,
                    PseudoClassNames.Empty,
                    PseudoClassNames.AnyLink,
                    PseudoClassNames.Link,
                    PseudoClassNames.Visited,
                    PseudoClassNames.Active,
                    PseudoClassNames.Hover,
                    PseudoClassNames.Focus,
                    PseudoClassNames.FocusVisible,
                    PseudoClassNames.FocusWithin,
                    PseudoClassNames.Target,
                    PseudoClassNames.Enabled,
                    PseudoClassNames.Disabled,
                    PseudoClassNames.Default,
                    PseudoClassNames.Checked,
                    PseudoClassNames.Indeterminate,
                    PseudoClassNames.PlaceholderShown,
                    PseudoClassNames.Unchecked,
                    PseudoClassNames.Valid,
                    PseudoClassNames.Invalid,
                    PseudoClassNames.Required,
                    PseudoClassNames.ReadOnly,
                    PseudoClassNames.ReadWrite,
                    PseudoClassNames.InRange,
                    PseudoClassNames.OutOfRange,
                    PseudoClassNames.Optional,
                    PseudoClassNames.Shadow,
                }
                .ToDictionary(x => x, PseudoClassSelector.Create);

        #endregion

        internal static PseudoClassSelectorFactory Instance => Lazy.Value;

        public ISelector Create(string name)
        {
            if (Selectors.TryGetValue(name, out var selector)) return selector;

            // A name the host registered is a real pseudo-class: it scores as a class and writes itself
            // back out, so it survives nesting, where an unrecognised one degrades to an UnknownSelector
            // whose text is empty once the selector is a string the stylesheet has no source span for.
            var custom = _parser?.Options.CustomPseudoClasses;

            return custom != null && custom.TryGetValue(name, out var registered) ? registered : null;
        }
    }
}
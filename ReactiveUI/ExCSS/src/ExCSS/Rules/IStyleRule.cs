using System.Collections.Generic;

namespace ExCSS
{
    public interface IStyleRule : IRule
    {
        string SelectorText { get; set; }
        StyleDeclaration Style { get; }
        ISelector Selector { get; set; }

        /// <summary>
        /// CSS Nesting: the rules written inside this rule's block, in source order. A nested style
        /// rule is already resolved to an absolute selector against this (parent) rule; a nested
        /// grouping at-rule (<c>@media</c>) carries an implicit <c>&amp;</c> rule holding the block's
        /// own declarations. Empty for a non-nesting rule.
        /// </summary>
        /// <remarks>
        /// One list rather than one per rule kind, because the relative order of a nested style rule
        /// and a nested at-rule is document order, and document order is the cascade's tie-break.
        /// </remarks>
        IReadOnlyList<IRule> NestedRules { get; }
    }
}

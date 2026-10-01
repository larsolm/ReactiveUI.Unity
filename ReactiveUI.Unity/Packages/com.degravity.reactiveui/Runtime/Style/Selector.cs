using System;

namespace ReactiveUI
{
	internal enum SelectorKind : byte
	{
		Universal,
		Type,
		Class,
		PseudoClass,

		/// <summary>
		/// <c>:scope</c> or a scope's <c>&amp;</c>: the scoping root the rule is being matched against.
		/// <see cref="SimpleSelector.Value"/> is unused.
		/// </summary>
		ScopeRoot,
	}

	internal enum Combinator : byte
	{
		None,
		Descendant,
		Child,
		NextSibling,
		SubsequentSibling,
	}

	internal readonly struct SimpleSelector
	{
		public readonly SelectorKind Kind;

		public readonly int Value;

		public SimpleSelector(SelectorKind kind, int value)
		{
			Kind = kind;
			Value = value;
		}
	}

	internal readonly struct CompoundSelector
	{
		public readonly int Start;
		public readonly int Count;
		public readonly Combinator ToNext;
		public readonly ulong StateMask;

		public CompoundSelector(int start, int count, Combinator toNext, ulong stateMask)
		{
			Start = start;
			Count = count;
			ToNext = toNext;
			StateMask = stateMask;
		}
	}

	internal readonly struct SelectorRecord
	{
		public readonly int CompoundStart;
		public readonly int CompoundCount;
		public readonly int Specificity;
		public readonly ulong SelfStateMask;
		public readonly ulong AncestorStateMask;

		public SelectorRecord(int compoundStart, int compoundCount, int specificity, ulong selfStateMask, ulong ancestorStateMask)
		{
			CompoundStart = compoundStart;
			CompoundCount = compoundCount;
			Specificity = specificity;
			SelfStateMask = selfStateMask;
			AncestorStateMask = ancestorStateMask;
		}

		public SelectorRecord WithSpecificity(int specificity) =>
			new(CompoundStart, CompoundCount, specificity, SelfStateMask, AncestorStateMask);

		/// <summary>
		/// CSS specificity, minus the id column — there are no id selectors, so a rule is only ever
		/// as specific as its classes and pseudo-classes, then its type names.
		/// </summary>
		public static int PackSpecificity(int classes, int types)
		{
			return (Math.Min(classes, 255) << 8) | Math.Min(types, 255);
		}
	}

	/// <summary>
	/// One rule. Its position in the sheet's rule array is its document order, which is why there
	/// is no order field: a per-sheet counter would say nothing about how two rules in different
	/// sheets compare, and that comparison is the one the cascade needs.
	/// </summary>
	internal readonly struct RuleRecord
	{
		public readonly int SelectorIndex;
		public readonly int DeclarationStart;
		public readonly int DeclarationCount;

		/// <summary>The <c>@media</c> condition this rule sits under, or 0 for none.</summary>
		/// <remarks>
		/// An index rather than the condition itself. This struct is read for every rule of every sheet
		/// on every match, and the overwhelming majority of rules are unconditional — so the test that
		/// has to be cheap is the compare against zero, not the evaluation behind it.
		/// </remarks>
		public readonly int MediaQueryIndex;

		/// <summary>The <c>@scope</c> this rule sits under, or 0 for none.</summary>
		public readonly int ScopeIndex;

		/// <summary>The sheet-local <c>@layer</c> this rule sits in, or 0 for unlayered.</summary>
		public readonly int LayerIndex;

		public RuleRecord(int selectorIndex, int declarationStart, int declarationCount, int mediaQueryIndex, int scopeIndex = 0, int layerIndex = 0)
		{
			SelectorIndex = selectorIndex;
			DeclarationStart = declarationStart;
			DeclarationCount = declarationCount;
			MediaQueryIndex = mediaQueryIndex;
			ScopeIndex = scopeIndex;
			LayerIndex = layerIndex;
		}
	}

	/// <summary>
	/// One <c>@scope (&lt;root&gt;) to (&lt;limit&gt;)</c> block.
	/// </summary>
	/// <remarks>
	/// The root and limit lists are ordinary selectors stored in the sheet's selector table. Index 0 of
	/// a sheet's scope table is the empty scope that unscoped rules point at, so the matcher's test for
	/// "is this rule scoped" is the same compare against zero the media index uses.
	/// </remarks>
	internal readonly struct ScopeRecord
	{
		public readonly int RootStart;
		public readonly int RootCount;
		public readonly int LimitStart;
		public readonly int LimitCount;

		/// <summary>The scope this one is nested inside, whose roots bound where this one's may be.</summary>
		public readonly int ParentIndex;

		public ScopeRecord(int rootStart, int rootCount, int limitStart, int limitCount, int parentIndex)
		{
			RootStart = rootStart;
			RootCount = rootCount;
			LimitStart = limitStart;
			LimitCount = limitCount;
			ParentIndex = parentIndex;
		}
	}
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	internal interface IMatchTarget
	{
		IMatchTarget? MatchParent { get; }
		IMatchTarget? MatchPreviousSibling { get; }
		ClassSet MatchClasses { get; }
		ulong MatchState { get; }

		/// <summary>The node's 0-based position among its parent's host children.</summary>
		int MatchChildIndex { get; }

		/// <summary>How many host children the node's parent has, the node included.</summary>
		int MatchSiblingCount { get; }

		/// <summary>Whether the node has no host children and no content of its own.</summary>
		bool MatchIsEmpty { get; }

		bool MatchesType(int typeId);
	}

	/// <summary>
	/// Owns the loaded stylesheets and everything derived from them: which rules match a node,
	/// what those rules compute to, and the interning tables that make both cheap to repeat.
	/// </summary>
	/// <remarks>
	/// Deliberately an instance rather than a static. The old framework kept its style table in
	/// process-wide statics that were append-only and never invalidated, with ids cached forever in
	/// consumer <c>static readonly</c> fields — which is precisely what made hot reload impossible.
	/// Everything mutable lives here and dies with the runtime.
	/// </remarks>
	internal sealed class StyleEngine
	{
		internal int Generation { get; private set; }

		/// <summary>
		/// Bumped whenever any node's state bits move, so a cached condition mask can tell whether it
		/// is still answering the same question.
		/// </summary>
		/// <remarks>
		/// One counter for the whole tree rather than a version per node, because a node's mask depends
		/// on its ancestors' states as much as its own — <c>.card:hover .title</c> is the ordinary case
		/// — and tracking that dependency precisely would cost more than re-deriving the mask. A
		/// pointer crossing invalidates every cached mask, which sounds coarse until you notice that
		/// state changes are the rare event and props-driven re-renders are the common one: it is the
		/// re-renders that this keeps off the matcher entirely.
		/// </remarks>
		internal int StateGeneration { get; private set; }

		internal void NoteStateChanged()
		{
			StateGeneration++;
		}

		internal StyleContext Context
		{
			get => _ctx;
			set
			{
				_ctx = value;
				Invalidate();
			}
		}

		internal bool UsesSiblingCombinators { get; private set; }

		/// <summary>Whether any active sheet uses <c>:nth-child</c> or <c>:first-child</c>, which read a node's index.</summary>
		internal bool UsesPositionFromStart { get; private set; }

		/// <summary>Whether any active sheet uses <c>:nth-last-child</c> or <c>:last-child</c>, which read a node's index from the end.</summary>
		internal bool UsesPositionFromEnd { get; private set; }

		/// <summary>Whether any active sheet uses <c>:empty</c>.</summary>
		internal bool UsesEmpty { get; private set; }

		/// <summary>
		/// Whether any active sheet tests position, emptiness or a sibling on a compound other than the
		/// one a rule styles, as in <c>li:first-child .label</c>. A node whose position moves then
		/// changes what its descendants match.
		/// </summary>
		internal bool UsesStructureInAncestors { get; private set; }

		/// <summary>The source's sheets by slot; null for a slot not yet active in this engine.</summary>
		private readonly List<StyleSheet?> _sheets = new();

		private IStyleSheetSource? _source;

		/// <summary>Classes this engine has seen on a node, so each triggers activation once.</summary>
		private readonly HashSet<int> _seenClasses = new();

		/// <summary>Packed rules bucketed by a class their key compound requires.</summary>
		private readonly Dictionary<int, List<int>> _rulesByClass = new();

		/// <summary>Packed rules whose key compound names a type but no class, by type.</summary>
		private readonly Dictionary<int, List<int>> _rulesByType = new();

		/// <summary>Packed rules whose key compound names neither a class nor a type.</summary>
		private readonly List<int> _rulesUniversal = new();

		/// <summary>Packed (sheet, compound) interactive compounds, bucketed like the rules.</summary>
		private readonly Dictionary<int, List<int>> _pointerByClass = new();

		private readonly Dictionary<int, List<int>> _pointerByType = new();
		private readonly List<int> _pointerUniversal = new();

		private readonly List<int> _candidates = new();

		/// <summary>
		/// How many levels up its scoping root sat, for each candidate of the current match that is
		/// scoped. Unscoped candidates are absent, which reads as infinitely far — what CSS says an
		/// unscoped rule's proximity is.
		/// </summary>
		private readonly Dictionary<int, int> _proximity = new();
		private readonly List<PropEntry> _entryScratch = new();
		private readonly Dictionary<RuleSetKey, int> _ruleSetIds = new();
		private readonly List<int[]> _ruleSets = new();
		private readonly Dictionary<ComputedKey, ComputedStyle> _computed = new();
		private readonly Dictionary<VarSetKey, int> _varSetIds = new();
		private readonly List<Dictionary<int, StyleValue>> _varSets = new();

		/// <summary>
		/// Every <c>@keyframes</c> block across every sheet, by interned name.
		/// </summary>
		/// <remarks>
		/// Instance state rather than a static table, for the reason this whole class is: a
		/// process-wide registry is what made hot reload impossible in the framework this replaced.
		/// Rebuilt with the sheets rather than in <c>Invalidate</c>, since a clip cannot change when
		/// only the rem size did.
		/// </remarks>
		private readonly Dictionary<int, KeyframesClip> _clips = new();

		/// <summary>
		/// Which of each sheet's <c>@media</c> queries currently hold, parallel to <see cref="_sheets"/>.
		/// </summary>
		/// <remarks>
		/// Per engine rather than per sheet: sheets are shared process-wide through
		/// <see cref="StyleSheets.Source"/>, and two runtimes rendering into differently sized containers
		/// ask the same query different questions. The same reason the rest of this class is an instance.
		/// </remarks>
		private readonly List<bool[]> _mediaActive = new();

		private StyleContext _ctx;
		private MediaEnvironment _media;

		/// <summary>
		/// The cascade comparison, built once. As a lambda it captured <c>this</c>, so every match
		/// allocated a closure and a delegate before it could sort anything.
		/// </summary>
		private readonly Comparison<int> _cascadeOrder;

		internal StyleEngine(StyleContext ctx)
		{
			_ctx = ctx;
			_cascadeOrder = CompareCascadeOrder;
			_ruleSets.Add(Array.Empty<int>());
			_varSets.Add(new Dictionary<int, StyleValue>());
		}

		/// <summary>
		/// Takes the source's sheets, activating its eager ones now and the rest when a node first
		/// carries one of their classes.
		/// </summary>
		internal void SetSheets(IStyleSheetSource? source)
		{
			_source = source;

			_sheets.Clear();
			_mediaActive.Clear();
			_seenClasses.Clear();
			_clips.Clear();
			_rulesByClass.Clear();
			_rulesByType.Clear();
			_rulesUniversal.Clear();
			_pointerByClass.Clear();
			_pointerByType.Clear();
			_pointerUniversal.Clear();
			UsesSiblingCombinators = false;
			UsesPositionFromStart = false;
			UsesPositionFromEnd = false;
			UsesEmpty = false;
			UsesStructureInAncestors = false;

			var count = source?.Count ?? 0;

			for (var i = 0; i < count; i++)
			{
				_sheets.Add(null);
				_mediaActive.Add(Array.Empty<bool>());
			}

			// In slot order, so a keyframes name declared twice resolves to the later sheet.
			for (var i = 0; i < count; i++)
			{
				if (source!.IsEager(i))
					Activate(i);
			}

			Invalidate();
		}

		/// <summary>Activates every lazy sheet naming a class on the target that this engine has not seen.</summary>
		private void ActivateFor(in ClassSet classes)
		{
			if (_source is null)
				return;

			for (var i = 0; i < classes.Count; i++)
			{
				var id = classes[i];

				if (!_seenClasses.Add(id) || _source.LazySlotsFor(id) is not { } slots)
					continue;

				for (var s = 0; s < slots.Count; s++)
				{
					if (Activate(slots[s]))
						StylePreloads.Note(PreloadKind.Sheet, _source.PathOf(slots[s]));
				}
			}
		}

		/// <summary>Activates a deferred slot now, rather than when a node first carries one of its classes.</summary>
		internal void Preload(int slot)
		{
			if (_source is not null && slot >= 0 && slot < _sheets.Count)
				Activate(slot);
		}

		/// <summary>Loads one slot's sheet into this engine and indexes it.</summary>
		/// <remarks>
		/// Slots never move, so rule sets interned before an activation stay valid after it.
		/// </remarks>
		/// <returns>Whether the slot was activated by this call.</returns>
		private bool Activate(int slot)
		{
			if (_sheets[slot] is not null || _source!.Get(slot) is not { } sheet)
				return false;

			using var marker = UiMarkers.ActivateSheet.Auto();

			_sheets[slot] = sheet;

			for (var i = 0; i < sheet.Keyframes.Length; i++)
				_clips[sheet.Keyframes[i].NameId] = sheet.Keyframes[i];

			NoteStructure(sheet);

			var flags = new bool[sheet.MediaQueries.Length];

			// Index 0 is the unconditional query.
			if (flags.Length > 0)
				flags[0] = true;

			_mediaActive[slot] = flags;
			RefreshMediaFlags(slot);

			for (var rule = 0; rule < sheet.Rules.Length; rule++)
			{
				var selector = sheet.Selectors[sheet.Rules[rule].SelectorIndex];
				var key = sheet.Compounds[selector.CompoundStart + selector.CompoundCount - 1];

				AddToIndex(sheet, key, (slot << 20) | rule, _rulesByClass, _rulesByType, _rulesUniversal);
			}

			foreach (var compound in sheet.InteractiveCompounds)
				AddToIndex(sheet, sheet.Compounds[compound], (slot << 20) | compound, _pointerByClass, _pointerByType, _pointerUniversal);

			return true;
		}

		/// <summary>Records which structural tests a newly activated sheet makes.</summary>
		private void NoteStructure(StyleSheet sheet)
		{
			for (var s = 0; s < sheet.Selectors.Length; s++)
			{
				var selector = sheet.Selectors[s];
				var last = selector.CompoundStart + selector.CompoundCount - 1;

				for (var c = selector.CompoundStart; c <= last; c++)
				{
					var compound = sheet.Compounds[c];
					var structural = false;

					if (compound.ToNext is Combinator.NextSibling or Combinator.SubsequentSibling)
					{
						UsesSiblingCombinators = true;

						// The compound to the right of a sibling combinator is the one whose neighbour is
						// read, so the test sits on an ancestor whenever that compound is not the key.
						if (c + 1 < last)
							UsesStructureInAncestors = true;
					}

					for (var i = compound.Start; i < compound.Start + compound.Count; i++)
					{
						switch (sheet.Simples[i].Kind)
						{
							case SelectorKind.NthChild:
								UsesPositionFromStart = true;
								structural = true;
								break;

							case SelectorKind.NthLastChild:
								UsesPositionFromEnd = true;
								structural = true;
								break;

							case SelectorKind.Empty:
								UsesEmpty = true;
								structural = true;
								break;
						}
					}

					if (structural && c != last)
						UsesStructureInAncestors = true;
				}
			}
		}

		/// <summary>
		/// Files an entry under one class its compound requires, else its type, else as universal.
		/// </summary>
		private static void AddToIndex(
			StyleSheet sheet,
			in CompoundSelector compound,
			int packed,
			Dictionary<int, List<int>> byClass,
			Dictionary<int, List<int>> byType,
			List<int> universal)
		{
			int? type = null;

			for (var i = 0; i < compound.Count; i++)
			{
				var simple = sheet.Simples[compound.Start + i];

				if (simple.Kind == SelectorKind.Class)
				{
					Bucket(byClass, simple.Value).Add(packed);

					return;
				}

				if (simple.Kind == SelectorKind.Type)
					type ??= simple.Value;
			}

			if (type is { } typeId)
				Bucket(byType, typeId).Add(packed);
			else
				universal.Add(packed);
		}

		private static List<int> Bucket(Dictionary<int, List<int>> index, int key)
		{
			if (!index.TryGetValue(key, out var bucket))
			{
				bucket = new List<int>();
				index[key] = bucket;
			}

			return bucket;
		}

		private void Invalidate()
		{
			_ruleSetIds.Clear();
			_ruleSets.Clear();
			_ruleSets.Add(Array.Empty<int>());
			_computed.Clear();
			_varSetIds.Clear();
			_varSets.Clear();
			_varSets.Add(new Dictionary<int, StyleValue>());

			Generation++;

			// Rule set ids are reissued from zero, so a mask cached against one of them now describes
			// rules that no longer exist.
			StateGeneration++;
		}

		/// <summary>
		/// Re-evaluates every <c>@media</c> query against a new environment, and reports whether any
		/// answer moved.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A method rather than a property setter like <see cref="Context"/>, because the caller needs the
		/// answer: a resize that does not cross a breakpoint must not re-match the tree, and only this can
		/// say whether it did.
		/// </para>
		/// <para>
		/// This split is the whole design. Dragging a window moves the environment every frame, while a
		/// query's answer flips a handful of times across the entire drag. Re-evaluating is a walk over a
		/// few dozen compiled comparisons; invalidating is every interned rule set, every computed style
		/// and a full re-match of the tree. Keeping them apart is the difference between a resize costing
		/// nothing and a resize costing a full restyle per pixel.
		/// </para>
		/// </remarks>
		internal bool SetMediaEnvironment(in MediaEnvironment environment)
		{
			if (_media.Equals(environment))
				return false;

			_media = environment;

			if (!RefreshMediaFlags())
				return false;

			Invalidate();

			return true;
		}

		private bool RefreshMediaFlags()
		{
			var changed = false;

			for (var i = 0; i < _sheets.Count; i++)
				changed |= RefreshMediaFlags(i);

			return changed;
		}

		private bool RefreshMediaFlags(int slot)
		{
			if (_sheets[slot] is not { } sheet)
				return false;

			var changed = false;
			var flags = _mediaActive[slot];

			// Index 0 is the unconditional query and is always on.
			for (var q = 1; q < flags.Length; q++)
			{
				var active = MediaQueryEvaluator.Evaluate(sheet, q, _media);

				if (flags[q] == active)
					continue;

				flags[q] = active;
				changed = true;
			}

			return changed;
		}

		/// <summary>The clip an <c>animation-name</c> resolves to, or null if nothing declares it.</summary>
		internal KeyframesClip? Clip(int nameId) => _clips.TryGetValue(nameId, out var clip) ? clip : null;

		#region Matching

		public int Match(IMatchTarget target)
		{
			using var marker = UiMarkers.Match.Auto();

			var classes = target.MatchClasses;

			ActivateFor(classes);

			_candidates.Clear();
			_proximity.Clear();

			// Each rule sits in exactly one bucket, so no rule is tried twice.
			MatchBucket(_rulesUniversal, target);

			for (var i = 0; i < classes.Count; i++)
			{
				if (_rulesByClass.TryGetValue(classes[i], out var bucket))
					MatchBucket(bucket, target);
			}

			foreach (var pair in _rulesByType)
			{
				if (target.MatchesType(pair.Key))
					MatchBucket(pair.Value, target);
			}

			if (_candidates.Count == 0)
				return 0;

			SortByCascadeOrder(_candidates);

			return InternRuleSet(_candidates);
		}

		private void MatchBucket(List<int> bucket, IMatchTarget target)
		{
			for (var i = 0; i < bucket.Count; i++)
			{
				// Rules are packed as (sheet, rule) so the ordered set survives interning.
				var packed = bucket[i];
				var sheet = _sheets[packed >> 20]!;
				var rule = sheet.Rules[packed & 0xFFFFF];

				// A rule under an `@media` that does not currently hold is not a candidate at all.
				// Filtered here rather than at resolution so the condition never reaches the per-frame
				// path: the rule set a node interns to already has the answer baked in, and the
				// environment moving is what re-matches the tree.
				if (rule.MediaQueryIndex != 0 && !_mediaActive[packed >> 20][rule.MediaQueryIndex])
					continue;

				var selector = sheet.Selectors[rule.SelectorIndex];

				// Pseudo-classes are skipped here: matching answers "could this rule ever apply",
				// which must not change when a pointer moves. Whether it applies *right now* is
				// decided per frame in EvaluateConditions.
				if (!MatchesRule(sheet, rule, selector, target, checkState: false, out var hops))
					continue;

				_candidates.Add(packed);

				if (rule.ScopeIndex != 0)
					_proximity[packed] = hops;
			}
		}

		private void SortByCascadeOrder(List<int> candidates)
		{
			candidates.Sort(_cascadeOrder);
		}

		/// <summary>
		/// Orders two matched rules so the one that wins sorts later.
		/// </summary>
		/// <remarks>
		/// Cascade layers first, then specificity, then scope proximity, then document order — the
		/// whole of the cascade that matters once <c>!important</c> and inline styles are handled
		/// elsewhere, in the order CSS Cascade 6 gives it.
		/// </remarks>
		private int CompareCascadeOrder(int a, int b)
		{
			var leftSheet = _sheets[a >> 20]!;
			var rightSheet = _sheets[b >> 20]!;
			var leftRule = leftSheet.Rules[a & 0xFFFFF];
			var rightRule = rightSheet.Rules[b & 0xFFFFF];

			// A later layer beats an earlier one whatever the specificity, and unlayered rules beat
			// every layer — which is the point of layers.
			var byLayer = leftSheet.LayerRanks[leftRule.LayerIndex].CompareTo(rightSheet.LayerRanks[rightRule.LayerIndex]);

			if (byLayer != 0)
				return byLayer;

			var bySpecificity = leftSheet.Selectors[leftRule.SelectorIndex].Specificity
				.CompareTo(rightSheet.Selectors[rightRule.SelectorIndex].Specificity);

			if (bySpecificity != 0)
				return bySpecificity;

			// Of two equally specific rules, the one whose scoping root is nearer the node wins, and an
			// unscoped rule is as far away as a rule can be. More hops therefore sorts earlier.
			var leftHops = _proximity.TryGetValue(a, out var hopsA) ? hopsA : int.MaxValue;
			var rightHops = _proximity.TryGetValue(b, out var hopsB) ? hopsB : int.MaxValue;

			if (leftHops != rightHops)
				return rightHops.CompareTo(leftHops);

			// Document order runs across the whole manifest, not within one file, so the packed
			// value is the tie-break: sheet index in the high bits, position in that sheet in the
			// low ones. Comparing a per-sheet counter instead made the first rule of every file
			// compare equal to the first rule of every other, and List.Sort is not stable — so
			// `.start` versus `.screen` resolved to whichever the sort happened to leave last.
			return a.CompareTo(b);
		}

		private SelectorRecord SelectorOf(int packed)
		{
			var sheet = _sheets[packed >> 20]!;
			return sheet.Selectors[sheet.Rules[packed & 0xFFFFF].SelectorIndex];
		}

		/// <summary>
		/// Whether a rule applies to a node, honouring the <c>@scope</c> it sits in.
		/// </summary>
		/// <param name="hops">
		/// For a scoped rule, how many levels above the node its scoping root sat — the scope proximity
		/// the cascade compares.
		/// </param>
		private static bool MatchesRule(
			StyleSheet sheet, in RuleRecord rule, in SelectorRecord selector, IMatchTarget target, bool checkState, out int hops)
		{
			hops = 0;

			if (rule.ScopeIndex == 0)
				return Matches(sheet, selector, target, checkState, scopeRoot: null);

			// Nearest root first: that is the one proximity is measured to, and a node inside two
			// nested `.card`s is styled by the inner one.
			for (var root = target; root is not null; root = root.MatchParent, hops++)
			{
				if (!IsScopeRoot(sheet, rule.ScopeIndex, root, checkState))
					continue;

				if (IsBeyondLimit(sheet, sheet.Scopes[rule.ScopeIndex], root, target, checkState))
					continue;

				if (Matches(sheet, selector, target, checkState, root))
					return true;
			}

			return false;
		}

		/// <summary>
		/// Whether a node can root the given scope: it matches one of the scope's root selectors and,
		/// for a scope nested in another, sits inside the outer scope.
		/// </summary>
		private static bool IsScopeRoot(StyleSheet sheet, int scopeIndex, IMatchTarget node, bool checkState)
		{
			var scope = sheet.Scopes[scopeIndex];

			if (scope.ParentIndex == 0)
				return AnySelector(sheet, scope.RootStart, scope.RootCount, node, checkState, scopeRoot: null);

			var outer = sheet.Scopes[scope.ParentIndex];

			for (var outerRoot = node; outerRoot is not null; outerRoot = outerRoot.MatchParent)
			{
				if (IsScopeRoot(sheet, scope.ParentIndex, outerRoot, checkState)
					&& !IsBeyondLimit(sheet, outer, outerRoot, node, checkState)
					&& AnySelector(sheet, scope.RootStart, scope.RootCount, node, checkState, outerRoot))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Whether a node is cut off from a scoping root by the scope's limit: it, or something between
		/// it and the root, matches a <c>to (…)</c> selector. The limit is exclusive, so a node matching
		/// it is out of scope too.
		/// </summary>
		/// <remarks>
		/// While matching (rather than checking state), a limit that depends on a pseudo-class is left
		/// out. Matching answers whether a rule could ever apply, and a limit that only holds while
		/// something is hovered does not stop that; the per-frame check, which does read state, does.
		/// </remarks>
		private static bool IsBeyondLimit(StyleSheet sheet, in ScopeRecord scope, IMatchTarget root, IMatchTarget node, bool checkState)
		{
			if (scope.LimitCount == 0)
				return false;

			for (var current = node; current is not null && !ReferenceEquals(current, root); current = current.MatchParent)
			{
				for (var i = scope.LimitStart; i < scope.LimitStart + scope.LimitCount; i++)
				{
					var limit = sheet.Selectors[i];

					if (!checkState && (limit.SelfStateMask | limit.AncestorStateMask) != 0UL)
						continue;

					if (Matches(sheet, limit, current, checkState, root))
						return true;
				}
			}

			return false;
		}

		private static bool AnySelector(
			StyleSheet sheet, int start, int count, IMatchTarget node, bool checkState, IMatchTarget? scopeRoot)
		{
			for (var i = start; i < start + count; i++)
			{
				if (Matches(sheet, sheet.Selectors[i], node, checkState, scopeRoot))
					return true;
			}

			return false;
		}

		/// <param name="scopeRoot">
		/// The node <c>:scope</c> and a scope's <c>&amp;</c> stand for, or null outside a scope.
		/// </param>
		private static bool Matches(StyleSheet sheet, SelectorRecord selector, IMatchTarget target, bool checkState, IMatchTarget? scopeRoot)
		{
			var compoundIndex = selector.CompoundStart + selector.CompoundCount - 1;

			if (!MatchesCompound(sheet, sheet.Compounds[compoundIndex], target, checkState, scopeRoot))
				return false;

			var current = target;

			for (var i = compoundIndex - 1; i >= selector.CompoundStart; i--)
			{
				var combinator = sheet.Compounds[i].ToNext;
				var compound = sheet.Compounds[i];

				switch (combinator)
				{
					case Combinator.Child:
						current = current.MatchParent;
						if (current is null || !MatchesCompound(sheet, compound, current, checkState, scopeRoot))
							return false;

						break;

					case Combinator.Descendant:
					{
						var ancestor = current.MatchParent;

						while (ancestor is not null && !MatchesCompound(sheet, compound, ancestor, checkState, scopeRoot))
						{
							ancestor = ancestor.MatchParent;
						}

						if (ancestor is null)
							return false;

						current = ancestor;

						break;
					}

					case Combinator.NextSibling:
						current = current.MatchPreviousSibling;
						if (current is null || !MatchesCompound(sheet, compound, current, checkState, scopeRoot))
							return false;

						break;

					case Combinator.SubsequentSibling:
					{
						var sibling = current.MatchPreviousSibling;

						while (sibling is not null && !MatchesCompound(sheet, compound, sibling, checkState, scopeRoot))
						{
							sibling = sibling.MatchPreviousSibling;
						}

						if (sibling is null)
							return false;

						current = sibling;

						break;
					}

					default:
						return false;
				}
			}

			return true;
		}

		/// <param name="scopeRoot">
		/// The node a <see cref="SelectorKind.ScopeRoot"/> must be. Null accepts any node, which only the
		/// pointer pre-check relies on: it asks whether a compound could ever match, and over-answering
		/// that is harmless.
		/// </param>
		private static bool MatchesCompound(
			StyleSheet sheet, CompoundSelector compound, IMatchTarget target, bool checkState, IMatchTarget? scopeRoot)
		{
			for (var i = 0; i < compound.Count; i++)
			{
				var simple = sheet.Simples[compound.Start + i];

				switch (simple.Kind)
				{
					case SelectorKind.Universal:
						break;

					case SelectorKind.Class:
						if (!target.MatchClasses.Contains(new ClassName(simple.Value)))
							return false;

						break;

					case SelectorKind.Type:
						if (!target.MatchesType(simple.Value))
							return false;

						break;

					case SelectorKind.PseudoClass:
						if (checkState && (target.MatchState & (1UL << simple.Value)) == 0UL)
							return false;

						break;

					case SelectorKind.ScopeRoot:
						if (scopeRoot is not null && !ReferenceEquals(target, scopeRoot))
							return false;

						break;

					case SelectorKind.NthChild:
						if (!SimpleSelector.NthMatches(SimpleSelector.NthA(simple.Value), SimpleSelector.NthB(simple.Value), target.MatchChildIndex + 1))
							return false;

						break;

					case SelectorKind.NthLastChild:
						if (!SimpleSelector.NthMatches(SimpleSelector.NthA(simple.Value), SimpleSelector.NthB(simple.Value), target.MatchSiblingCount - target.MatchChildIndex))
							return false;

						break;

					case SelectorKind.Empty:
						if (!target.MatchIsEmpty)
							return false;

						break;
				}
			}

			return true;
		}

		#endregion

		#region Resolution

		public ulong EvaluateConditions(int ruleSetId, IMatchTarget target)
		{
			var rules = Rules(ruleSetId);
			var mask = 0UL;

			// Beyond 64 state-dependent rules on one node the mask cannot address them; that is far
			// past anything reasonable, and the excess simply stays inactive.
			var limit = Math.Min(rules.Length, 64);

			for (var i = 0; i < limit; i++)
			{
				var packed = rules[i];
				var sheet = _sheets[packed >> 20]!;
				var rule = sheet.Rules[packed & 0xFFFFF];
				var selector = sheet.Selectors[rule.SelectorIndex];

				if ((selector.SelfStateMask | selector.AncestorStateMask) == 0UL)
				{
					// Unconditional rules are always on.
					mask |= 1UL << i;
					continue;
				}

				if (MatchesRule(sheet, rule, selector, target, checkState: true, out _))
					mask |= 1UL << i;
			}

			return mask;
		}

		public bool UsesState(int ruleSetId, StateBit bit)
		{
			if (ruleSetId == 0 || !bit.IsValid)
				return false;

			var rules = Rules(ruleSetId);

			for (var i = 0; i < rules.Length; i++)
			{
				var selector = SelectorOf(rules[i]);

				if (((selector.SelfStateMask | selector.AncestorStateMask) & bit.Mask) != 0UL)
					return true;
			}

			return false;
		}

		public bool DependsOnState(int ruleSetId)
		{
			var rules = Rules(ruleSetId);

			for (var i = 0; i < rules.Length; i++)
			{
				var selector = SelectorOf(rules[i]);
				if ((selector.SelfStateMask | selector.AncestorStateMask) != 0UL)
					return true;
			}

			return false;
		}

		/// <remarks>
		/// Deliberately blind to <c>@media</c>: the index it scans is per compound, with no link back to
		/// the rule that owns it, so a <c>:hover</c> rule living only inside a media block enables the
		/// pointer whatever the viewport. That is a superset — a hit-testable node that currently has no
		/// hover style — and cheaper than the link it would take to be exact.
		/// </remarks>
		public bool NeedsPointer(IMatchTarget target)
		{
			var classes = target.MatchClasses;

			ActivateFor(classes);

			if (AnyCompound(_pointerUniversal, target))
				return true;

			for (var i = 0; i < classes.Count; i++)
			{
				if (_pointerByClass.TryGetValue(classes[i], out var bucket) && AnyCompound(bucket, target))
					return true;
			}

			foreach (var pair in _pointerByType)
			{
				if (target.MatchesType(pair.Key) && AnyCompound(pair.Value, target))
					return true;
			}

			return false;
		}

		private bool AnyCompound(List<int> bucket, IMatchTarget target)
		{
			for (var i = 0; i < bucket.Count; i++)
			{
				var sheet = _sheets[bucket[i] >> 20]!;
				var compound = sheet.Compounds[bucket[i] & 0xFFFFF];

				if (MatchesCompound(sheet, compound, target, checkState: false, scopeRoot: null))
					return true;
			}

			return false;
		}

		public ComputedStyle Resolve(int ruleSetId, ulong conditionMask, int inheritedId, int varSetId)
		{
			if (ruleSetId == 0 && inheritedId == 0)
				return ComputedStyle.Empty;

			var key = new ComputedKey(ruleSetId, conditionMask, inheritedId, varSetId);

			if (_computed.TryGetValue(key, out var cached))
				return cached;

			ComputedStyle computed;

			using (UiMarkers.ComputeStyle.Auto())
				computed = Build(ruleSetId, conditionMask, inheritedId, varSetId);

			_computed[key] = computed;

			return computed;
		}

		private ComputedStyle Build(int ruleSetId, ulong conditionMask, int inheritedId, int varSetId)
		{
			_entryScratch.Clear();

			// Inherited values come first so that anything the node declares itself overrides them.
			if (inheritedId != 0)
			{
				var inherited = Scope(inheritedId);
				foreach (var pair in inherited) _entryScratch.Add(new PropEntry((PropId)pair.Key, pair.Value));
			}

			var scope = Scope(varSetId);
			var rules = Rules(ruleSetId);

			for (var i = 0; i < rules.Length; i++)
			{
				// A rule contributes only when its condition bit is set, which is where every
				// pseudo-class — the node's own and its ancestors' — has already been accounted for.
				if (i < 64 && (conditionMask & (1UL << i)) == 0UL)
					continue;

				var packed = rules[i];
				var sheet = _sheets[packed >> 20]!;
				var rule = sheet.Rules[packed & 0xFFFFF];

				for (var d = 0; d < rule.DeclarationCount; d++)
				{
					var declaration = sheet.Declarations[rule.DeclarationStart + d];
					if (declaration.IsCustom)
						continue;

					if (!TryResolveValue(declaration.Id, declaration.Value, scope, out var value))
						continue;

					_entryScratch.Add(new PropEntry(declaration.Id, value));
				}
			}

			return ComputedStyle.FromEntries(_entryScratch);
		}

		private static bool TryResolveValue(
			PropId id, StyleValue value, Dictionary<int, StyleValue> scope, out StyleValue resolved)
		{
			if (value.Kind != StyleValueKind.VarReference)
			{
				// A calc that survived the sheet build is one that reads a custom property; a literal
				// one folded to a plain value there. It resolves to a length or a number rather than
				// to a reference, which is why it cannot ride the composite path below.
				if (value.Reference is CalcExpr calc)
					return calc.TryResolve(scope, out resolved);

				// A transition or transform that reads a custom property can only be split once it
				// has been substituted, so each of its longhands arrives here holding the whole text.
				if (value.Reference is PendingShorthand pending)
					return pending.TryResolve(id, scope, out resolved);

				// A shadow list or a gradient is a composite whose colours may be references while the
				// rest of the value is already final, so it rebuilds itself rather than being replaced
				// wholesale. The result is cached with the computed style, so this runs once per
				// distinct (rule set, scope) pair rather than per node.
				if (value.Reference is IVarDependent { HasVars: true } composite)
					return TrySubstitute(composite, scope, out resolved);

				resolved = value;

				return true;
			}

			// An unresolvable reference drops the declaration, which is what CSS does rather than
			// falling back to something arbitrary.
			if (!scope.TryGetValue(value.VarNameId, out resolved))
				return false;

			// A custom property written as `rgba(var(--accent), .1)` is kept unresolved in its scope and
			// resolved here, against the scope of the node using it — so a theme that redefines
			// `--accent` further down retints every token derived from it, not only the base colour.
			if (resolved.Reference is IVarDependent { HasVars: true } derived)
				return TrySubstitute(derived, scope, out resolved);

			// A numeric keyword — `--weight: 500` read by `font-weight` — arrives as a number and has to
			// be looked up in the property's keyword set.
			if (resolved.Kind == StyleValueKind.Number
				&& PropertyRegistry.TryGet(id, out var numeric)
				&& numeric._syntax == ValueSyntax.Keyword)
			{
				var keyword = Keywords.Resolve(
					numeric._keywords, resolved.AsNumber().ToString(System.Globalization.CultureInfo.InvariantCulture));

				resolved = keyword < 0 ? default : StyleValue.OfKeyword(keyword);

				return keyword >= 0;
			}

			if (resolved.Reference is not string text)
				return true;

			// Re-read text can itself hold references — a shadow token written with
			// `rgba(var(--accent), .5)` — which resolve against the same scope. Never a bare var() again,
			// since a custom property holding one was already resolved when its scope was built.
			return TryReparse(id, text, out var reparsed)
				&& reparsed.Kind != StyleValueKind.VarReference
				&& TryResolveValue(id, reparsed, scope, out resolved);
		}

		/// <summary>
		/// Rebuilds a composite value against a scope. A single colour comes back as a colour, so a
		/// colour property reading <c>rgba(var(--x), a)</c> resolves to the same kind of value as one
		/// written as a literal.
		/// </summary>
		private static bool TrySubstitute(IVarDependent composite, IReadOnlyDictionary<int, StyleValue> scope, out StyleValue resolved)
		{
			switch (composite.Substitute(scope))
			{
				case null:
					resolved = default;
					return false;

				case Color color:
					resolved = StyleValue.OfColor(color);
					return true;

				case var substituted:
					resolved = StyleValue.OfReference(substituted);
					return true;
			}
		}

		/// <summary>
		/// Reads a custom property's raw text in the syntax of the property it was substituted into.
		/// </summary>
		/// <remarks>
		/// A custom property has no declared type, so anything that is not a colour, a length, a number
		/// or a calc is kept as the text it was written as — <c>.12s</c>, <c>cubic-bezier(…)</c>,
		/// <c>"Chakra Petch"</c>. Only the property that receives it knows what it should be, which is
		/// what makes a token file of durations, easings and font names usable through <c>var()</c>.
		/// </remarks>
		private static bool TryReparse(PropId id, string text, out StyleValue resolved)
		{
			if (!PropertyRegistry.TryGet(id, out var info))
			{
				resolved = default;

				return false;
			}

			if (info._syntax == ValueSyntax.FontReference && id == PropId.FontFamily)
			{
				resolved = StyleValue.OfReference(ValueParser.Unquote(text));

				return true;
			}

			return ValueParser.TryParse(text, info, out resolved);
		}

		/// <param name="conditionMask">
		/// Which of the node's rules currently hold, as <see cref="EvaluateConditions"/> reports it. A rule
		/// sits in every node's rule set when its only qualifier is a pseudo-class — <c>:root</c> above all
		/// — so without this each descendant would re-declare the root's variables over any override an
		/// ancestor made.
		/// </param>
		public int ResolveVarScope(int ruleSetId, int parentVarSetId, int inlineSlot, ulong conditionMask)
		{
			var rules = Rules(ruleSetId);
			var hasInline = inlineSlot >= 0 && InlineArena.At(inlineSlot).VarCount > 0;

			if (rules.Length == 0 && !hasInline)
				return parentVarSetId;

			Dictionary<int, StyleValue>? scope = null;

			for (var i = 0; i < rules.Length; i++)
			{
				if (i < 64 && (conditionMask & (1UL << i)) == 0UL)
					continue;

				var packed = rules[i];
				var sheet = _sheets[packed >> 20]!;
				var rule = sheet.Rules[packed & 0xFFFFF];

				for (var d = 0; d < rule.DeclarationCount; d++)
				{
					var declaration = sheet.Declarations[rule.DeclarationStart + d];
					if (!declaration.IsCustom)
						continue;

					scope ??= new Dictionary<int, StyleValue>(Scope(parentVarSetId));

					var value = declaration.Value;

					// `--tile-edge-color: var(--banana-600)` — one custom property aliasing another,
					// or `--gap-wide: calc(var(--gap) * 2)` doing arithmetic on one. Rules arrive in
					// cascade order, so either sees the parent scope plus everything a less specific
					// rule on this node already contributed, which is what CSS does.
					if (value.Kind == StyleValueKind.VarReference)
					{
						if (!scope.TryGetValue(value.VarNameId, out value))
							continue;
					}
					else if (value.Reference is CalcExpr calc && !calc.TryResolve(scope, out value))
					{
						continue;
					}

					scope[declaration.CustomNameId] = value;
				}
			}

			if (hasInline)
			{
				scope ??= new Dictionary<int, StyleValue>(Scope(parentVarSetId));

				for (var i = 0; i < InlineArena.At(inlineSlot).VarCount; i++)
				{
					var entry = InlineArena.At(inlineSlot).VarAt(i);
					scope[entry.Id] = entry.Value;
				}
			}

			return scope is null ? parentVarSetId : InternVarSet(scope);
		}

		public int ResolveInherited(ComputedStyle style, int parentInheritedId)
		{
			Dictionary<int, StyleValue>? inherited = null;

			for (var i = 0; i < style.Count; i++)
			{
				var entry = style[i];
				if (!PropertyRegistry.IsInherited(entry.Id))
					continue;

				inherited ??= new Dictionary<int, StyleValue>(Scope(parentInheritedId));
				inherited[(int)entry.Id] = entry.Value;
			}

			return inherited is null ? parentInheritedId : InternVarSet(inherited);
		}

		#endregion

		#region Interning

		private int[] Rules(int ruleSetId)
		{
			return ruleSetId >= 0 && ruleSetId < _ruleSets.Count ? _ruleSets[ruleSetId] : Array.Empty<int>();
		}

		private Dictionary<int, StyleValue> Scope(int varSetId)
		{
			return varSetId >= 0 && varSetId < _varSets.Count ? _varSets[varSetId] : _varSets[0];
		}

		private int InternRuleSet(List<int> rules)
		{
			// Probed against the live scratch list rather than a copy of it. Interning is overwhelmingly
			// a hit — a node re-matching to the rule set it already had — and building an array to ask
			// the question meant every Match that found any rule allocated one purely to throw it away.
			if (_ruleSetIds.TryGetValue(RuleSetKey.Probe(rules), out var id))
				return id;

			var stored = rules.ToArray();

			id = _ruleSets.Count;
			_ruleSets.Add(stored);
			_ruleSetIds[RuleSetKey.Stored(stored)] = id;

			return id;
		}

		private int InternVarSet(Dictionary<int, StyleValue> scope)
		{
			var key = new VarSetKey(scope);

			if (_varSetIds.TryGetValue(key, out var id))
				return id;

			id = _varSets.Count;
			_varSets.Add(scope);
			_varSetIds[key] = id;

			if (_varSets.Count == 4096)
			{
				Debug.LogError(
					"[ReactiveUI] Over 4096 distinct variable scopes. Something is feeding a "
					+ "continuously changing value into a custom property, which the cache cannot collapse.");
			}

			return id;
		}

		/// <summary>
		/// A rule set as a dictionary key, over either the interned array or the scratch list a lookup
		/// is asking about.
		/// </summary>
		/// <remarks>
		/// The two shapes are the point. A key that could only hold an array forced every lookup to
		/// copy the candidate list before it could ask whether that copy was needed — which, on the
		/// hit that interning exists to produce, it never was. Only the array form is ever stored;
		/// the list form is a probe over a buffer that is about to be reused.
		/// </remarks>
		private readonly struct RuleSetKey : IEquatable<RuleSetKey>
		{
			private readonly int[]? _stored;
			private readonly List<int>? _probe;
			private readonly int _hash;

			private RuleSetKey(int[]? stored, List<int>? probe, int hash)
			{
				_stored = stored;
				_probe = probe;
				_hash = hash;
			}

			internal static RuleSetKey Stored(int[] rules)
			{
				var hash = 17;
				for (var i = 0; i < rules.Length; i++) hash = hash * 31 + rules[i];

				return new RuleSetKey(rules, null, hash);
			}

			internal static RuleSetKey Probe(List<int> rules)
			{
				var hash = 17;
				for (var i = 0; i < rules.Count; i++) hash = hash * 31 + rules[i];

				return new RuleSetKey(null, rules, hash);
			}

			private int Count => _stored?.Length ?? _probe?.Count ?? 0;

			private int At(int index) => _stored is not null ? _stored[index] : _probe![index];

			public bool Equals(RuleSetKey other)
			{
				if (_hash != other._hash) return false;

				var count = Count;
				if (count != other.Count) return false;

				for (var i = 0; i < count; i++)
				{
					if (At(i) != other.At(i)) return false;
				}

				return true;
			}

			public override bool Equals(object? obj)
			{
				return obj is RuleSetKey other && Equals(other);
			}

			public override int GetHashCode()
			{
				return _hash;
			}
		}

		private readonly struct VarSetKey : IEquatable<VarSetKey>
		{
			private readonly Dictionary<int, StyleValue> _scope;
			private readonly int _hash;

			internal VarSetKey(Dictionary<int, StyleValue> scope)
			{
				_scope = scope;

				// Order-independent, because dictionary enumeration order is not guaranteed.
				var hash = scope.Count;

				foreach (var pair in scope)
					hash ^= pair.Key * 397 ^ pair.Value.GetHashCode();

				_hash = hash;
			}

			public bool Equals(VarSetKey other)
			{
				if (_scope.Count != other._scope.Count)
					return false;

				foreach (var pair in _scope)
				{
					if (!other._scope.TryGetValue(pair.Key, out var value) || !value.Equals(pair.Value))
						return false;
				}

				return true;
			}

			public override bool Equals(object? obj)
			{
				return obj is VarSetKey other && Equals(other);
			}

			public override int GetHashCode()
			{
				return _hash;
			}
		}

		private readonly struct ComputedKey : IEquatable<ComputedKey>
		{
			private readonly int _ruleSetId;
			private readonly ulong _state;
			private readonly int _inheritedId;
			private readonly int _varSetId;

			internal ComputedKey(int ruleSetId, ulong state, int inheritedId, int varSetId)
			{
				_ruleSetId = ruleSetId;
				_state = state;
				_inheritedId = inheritedId;
				_varSetId = varSetId;
			}

			public bool Equals(ComputedKey other)
			{
				return _ruleSetId == other._ruleSetId
					&& _state == other._state
					&& _inheritedId == other._inheritedId
					&& _varSetId == other._varSetId;
			}

			public override bool Equals(object? obj)
			{
				return obj is ComputedKey other && Equals(other);
			}

			public override int GetHashCode()
			{
				return HashCode.Combine(_ruleSetId, _state, _inheritedId, _varSetId);
			}
		}

		#endregion
	}
}

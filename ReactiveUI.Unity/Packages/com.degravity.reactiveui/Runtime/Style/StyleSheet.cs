using System.Collections.Generic;

namespace ReactiveUI
{
	internal readonly struct Declaration
	{
		public bool IsCustom => CustomNameId != 0;

		public readonly PropId Id;
		public readonly int CustomNameId;
		public readonly StyleValue Value;

		public Declaration(PropId id, StyleValue value)
		{
			Id = id;
			CustomNameId = 0;
			Value = value;
		}

		public Declaration(int customNameId, StyleValue value)
		{
			Id = PropId.None;
			CustomNameId = customNameId;
			Value = value;
		}
	}

	/// <summary>An <c>@font-face</c>, registered when its sheet is loaded.</summary>
	internal readonly struct FontFace
	{
		public readonly string Family;

		/// <summary>The font asset's path under a <c>Resources</c> folder.</summary>
		public readonly string ResourcePath;

		public readonly int Weight;

		public FontFace(string family, string resourcePath, int weight)
		{
			Family = family;
			ResourcePath = resourcePath;
			Weight = weight;
		}
	}

	internal sealed class StyleSheet
	{
		/// <summary>The asset the sheet was compiled from, for diagnostics.</summary>
		public readonly string Name;

		public readonly SimpleSelector[] Simples;
		public readonly CompoundSelector[] Compounds;
		public readonly SelectorRecord[] Selectors;
		public readonly RuleRecord[] Rules;
		public readonly Declaration[] Declarations;

		/// <summary>The <c>@keyframes</c> blocks this sheet declared, in document order.</summary>
		public readonly KeyframesClip[] Keyframes;

		/// <summary>Compiled <c>@media</c> conditions. Index 0 is the unconditional query.</summary>
		/// <remarks>
		/// Only the conditions live here, never their answers: sheets are shared process-wide through
		/// <see cref="StyleSheets.Source"/>, and two runtimes rendering into differently sized
		/// containers ask the same query different questions. The answers belong to the engine.
		/// </remarks>
		public readonly MediaQuery[] MediaQueries;

		public readonly MediaClause[] MediaClauses;
		public readonly MediaFeatureTest[] MediaFeatures;

		/// <summary><c>@scope</c> blocks. Index 0 is the empty scope unscoped rules point at.</summary>
		public readonly ScopeRecord[] Scopes;

		/// <summary>
		/// The <c>@layer</c> names this sheet mentions, in the order it first mentions them. A rule's
		/// <see cref="RuleRecord.LayerIndex"/> is one past its index here; 0 is unlayered.
		/// </summary>
		public readonly string[] LayerNames;

		public readonly FontFace[] FontFaces;

		/// <summary>
		/// Each local layer's rank in the cascade across every loaded sheet, filled in by whoever loads
		/// the set, since a layer's position depends on every sheet that names it. Index 0 is unlayered,
		/// which outranks every layer.
		/// </summary>
		public int[] LayerRanks;

		public readonly Dictionary<string, UnityEngine.Object?> Resources = new();
		public readonly HashSet<int> AncestorRelevantClasses = new();
		public readonly List<int> InteractiveCompounds = new();

		/// <summary>Whether anything in this sheet is conditional, so the matcher can skip the check.</summary>
		public bool HasMediaQueries => MediaQueries.Length > 1;

		public StyleSheet(
			string name,
			SimpleSelector[] simples,
			CompoundSelector[] compounds,
			SelectorRecord[] selectors,
			RuleRecord[] rules,
			Declaration[] declarations,
			KeyframesClip[] keyframes,
			MediaQuery[] mediaQueries,
			MediaClause[] mediaClauses,
			MediaFeatureTest[] mediaFeatures,
			ScopeRecord[] scopes,
			string[] layerNames,
			FontFace[] fontFaces)
		{
			Name = name;
			Simples = simples;
			Compounds = compounds;
			Selectors = selectors;
			Rules = rules;
			Declarations = declarations;
			Keyframes = keyframes;
			MediaQueries = mediaQueries;
			MediaClauses = mediaClauses;
			MediaFeatures = mediaFeatures;
			Scopes = scopes;
			LayerNames = layerNames;
			FontFaces = fontFaces;
			LayerRanks = new int[layerNames.Length + 1];
			LayerRanks[0] = int.MaxValue;

			var interactive = UiStates.s_hover.Mask | UiStates.s_active.Mask;

			for (var i = 0; i < selectors.Length; i++)
			{
				var selector = selectors[i];

				// Every compound except the last is an ancestor/sibling position.
				for (var c = 0; c < selector.CompoundCount - 1; c++)
				{
					var compound = compounds[selector.CompoundStart + c];

					for (var s = 0; s < compound.Count; s++)
					{
						var simple = simples[compound.Start + s];

						if (simple.Kind == SelectorKind.Class)
							AncestorRelevantClasses.Add(simple.Value);
					}
				}

				for (var c = 0; c < selector.CompoundCount; c++)
				{
					var index = selector.CompoundStart + c;

					if ((compounds[index].StateMask & interactive) != 0UL)
						InteractiveCompounds.Add(index);
				}
			}

			// A scope's root and limit are always ancestors of the node a scoped rule styles, so every
			// class they name decides something below the node that carries it — the last compound too.
			for (var i = 1; i < scopes.Length; i++)
			{
				AddAllClasses(scopes[i].RootStart, scopes[i].RootCount);
				AddAllClasses(scopes[i].LimitStart, scopes[i].LimitCount);
			}
		}

		private void AddAllClasses(int selectorStart, int selectorCount)
		{
			for (var i = selectorStart; i < selectorStart + selectorCount; i++)
			{
				var selector = Selectors[i];

				for (var c = 0; c < selector.CompoundCount; c++)
				{
					var compound = Compounds[selector.CompoundStart + c];

					for (var s = 0; s < compound.Count; s++)
					{
						var simple = Simples[compound.Start + s];

						if (simple.Kind == SelectorKind.Class)
							AncestorRelevantClasses.Add(simple.Value);
					}
				}
			}
		}
	}
}

using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace ReactiveUI
{
	internal enum MediaFeatureKind : byte
	{
		Width,
		Height,
		Orientation,
		AspectRatio,
		InputDevice,
		GamepadLayout,
	}

	internal enum MediaComparison : byte
	{
		Equal,
		LessThan,
		LessThanOrEqual,
		GreaterThan,
		GreaterThanOrEqual,
	}

	internal enum MediaOrientation : byte
	{
		Portrait,
		Landscape,
	}

	/// <summary>
	/// One <c>(feature: value)</c> test.
	/// </summary>
	/// <remarks>
	/// A length keeps its unit rather than folding to pixels when the sheet is built, because a
	/// <c>rem</c> breakpoint has to track the rem size the way every other length in the sheet does —
	/// a project that sizes in rem would otherwise find its breakpoints frozen at whatever the rem size
	/// happened to be at import.
	/// </remarks>
	internal readonly struct MediaFeatureTest
	{
		public readonly MediaFeatureKind Kind;
		public readonly MediaComparison Comparison;

		/// <summary>The breakpoint, for <see cref="MediaFeatureKind.Width"/> and <see cref="MediaFeatureKind.Height"/>.</summary>
		public readonly StyleLength Length;

		/// <summary>The ratio, for <see cref="MediaFeatureKind.AspectRatio"/>.</summary>
		public readonly float Number;

		/// <summary>
		/// The keyword, for <see cref="MediaFeatureKind.Orientation"/>, <see cref="MediaFeatureKind.InputDevice"/>
		/// and <see cref="MediaFeatureKind.GamepadLayout"/>.
		/// </summary>
		public readonly int Keyword;

		private MediaFeatureTest(
			MediaFeatureKind kind, MediaComparison comparison, StyleLength length, float number, int keyword)
		{
			Kind = kind;
			Comparison = comparison;
			Length = length;
			Number = number;
			Keyword = keyword;
		}

		public static MediaFeatureTest OfLength(MediaFeatureKind kind, MediaComparison comparison, StyleLength length) =>
			new(kind, comparison, length, 0f, 0);

		public static MediaFeatureTest OfNumber(MediaFeatureKind kind, MediaComparison comparison, float number) =>
			new(kind, comparison, default, number, 0);

		public static MediaFeatureTest OfKeyword(MediaFeatureKind kind, int keyword) =>
			new(kind, MediaComparison.Equal, default, 0f, keyword);
	}

	/// <summary>
	/// One comma-separated arm of a query: a media type, an optional <c>not</c>, and features that must
	/// all hold.
	/// </summary>
	internal readonly struct MediaClause
	{
		public readonly int FeatureStart;
		public readonly int FeatureCount;
		public readonly bool IsInverse;

		/// <summary>Whether the media type named is one this runtime answers to.</summary>
		/// <remarks>
		/// Folded when the sheet is built rather than kept as a name: the type cannot change at runtime,
		/// and folding keeps <c>not print</c> — which must match — a single boolean flip.
		/// </remarks>
		public readonly bool TypeMatches;

		public MediaClause(int featureStart, int featureCount, bool isInverse, bool typeMatches)
		{
			FeatureStart = featureStart;
			FeatureCount = featureCount;
			IsInverse = isInverse;
			TypeMatches = typeMatches;
		}
	}

	/// <summary>
	/// A compiled <c>@media</c> condition.
	/// </summary>
	/// <remarks>
	/// Index 0 of a sheet's query table is the unconditional query, which is why a rule carrying 0 needs
	/// no evaluation at all. Nesting is kept as a parent index rather than flattened into one clause
	/// list, because nesting is a logical AND across two independent queries and an AND of two clause
	/// lists is a cross product the walk can skip.
	/// </remarks>
	internal readonly struct MediaQuery
	{
		public readonly int ClauseStart;
		public readonly int ClauseCount;
		public readonly int ParentIndex;

		/// <summary>Set when the query named something this runtime cannot evaluate.</summary>
		/// <remarks>
		/// Such a query never matches, rather than never matching only until it is negated: CSS drops a
		/// query it cannot parse, and inverting one instead would turn a typo into a rule that applies
		/// everywhere.
		/// </remarks>
		public readonly bool NeverMatches;

		public MediaQuery(int clauseStart, int clauseCount, int parentIndex, bool neverMatches)
		{
			ClauseStart = clauseStart;
			ClauseCount = clauseCount;
			ParentIndex = parentIndex;
			NeverMatches = neverMatches;
		}
	}

	/// <summary>
	/// A query compiled on its own rather than as part of a sheet — what <c>UseMedia</c> reads.
	/// </summary>
	internal sealed class MediaCondition
	{
		[NoAutoStaticsCleanup]
		internal static readonly MediaCondition s_never = new(Array.Empty<MediaClause>(), Array.Empty<MediaFeatureTest>(), neverMatches: true);

		private readonly MediaClause[] _clauses;
		private readonly MediaFeatureTest[] _features;
		private readonly bool _neverMatches;

		internal MediaCondition(MediaClause[] clauses, MediaFeatureTest[] features, bool neverMatches)
		{
			_clauses = clauses;
			_features = features;
			_neverMatches = neverMatches;
		}

		internal bool Evaluate(in MediaEnvironment environment)
		{
			return !_neverMatches
				&& MediaQueryEvaluator.AnyClause(_clauses, _features, 0, _clauses.Length, environment);
		}
	}

	internal static class MediaQueryEvaluator
	{
		/// <summary>Whether a sheet's compiled query holds in the given environment.</summary>
		/// <remarks>
		/// The parent chain is walked iteratively rather than recursively; nesting is an AND, so the
		/// first arm that fails settles the whole chain.
		/// </remarks>
		internal static bool Evaluate(StyleSheet sheet, int queryIndex, in MediaEnvironment environment)
		{
			while (queryIndex != 0)
			{
				var query = sheet.MediaQueries[queryIndex];

				if (query.NeverMatches)
					return false;

				if (query.ClauseCount != 0
					&& !AnyClause(sheet.MediaClauses, sheet.MediaFeatures, query.ClauseStart, query.ClauseCount, environment))
				{
					return false;
				}

				queryIndex = query.ParentIndex;
			}

			return true;
		}

		/// <summary>Whether any arm of a comma-separated query list holds.</summary>
		internal static bool AnyClause(
			MediaClause[] clauses,
			MediaFeatureTest[] features,
			int start,
			int count,
			in MediaEnvironment environment)
		{
			for (var i = 0; i < count; i++)
			{
				if (EvaluateClause(clauses[start + i], features, environment))
					return true;
			}

			return false;
		}

		private static bool EvaluateClause(
			in MediaClause clause, MediaFeatureTest[] features, in MediaEnvironment environment)
		{
			var holds = clause.TypeMatches;

			for (var i = 0; holds && i < clause.FeatureCount; i++)
				holds = EvaluateFeature(features[clause.FeatureStart + i], environment);

			return holds != clause.IsInverse;
		}

		private static bool EvaluateFeature(in MediaFeatureTest test, in MediaEnvironment environment)
		{
			switch (test.Kind)
			{
				case MediaFeatureKind.Width:
					return Compare(environment.Viewport.Width, Resolve(test.Length, environment), test.Comparison);

				case MediaFeatureKind.Height:
					return Compare(environment.Viewport.Height, Resolve(test.Length, environment), test.Comparison);

				case MediaFeatureKind.AspectRatio:
					return Compare(environment.Viewport.AspectRatio, test.Number, test.Comparison);

				case MediaFeatureKind.Orientation:
					var orientation = environment.Viewport.IsPortrait
						? MediaOrientation.Portrait
						: MediaOrientation.Landscape;

					return (int)orientation == test.Keyword;

				case MediaFeatureKind.InputDevice:
					return (int)environment.Device == test.Keyword;

				case MediaFeatureKind.GamepadLayout:
					return (int)environment.Layout == test.Keyword;

				default:
					return false;
			}
		}

		private static float Resolve(StyleLength length, in MediaEnvironment environment)
		{
			return length.Unit == LengthUnit.Rem ? length.Value * environment.RemSize : length.Value;
		}

		/// <remarks>
		/// The equality case is approximate rather than exact. CSS defines <c>(width: 40rem)</c> as an
		/// exact match, but the value it is compared against is a rect measured through a canvas scaler
		/// and is essentially never exactly anything — an exact test would make the plain form a rule
		/// that simply never applies.
		/// </remarks>
		private static bool Compare(float actual, float target, MediaComparison comparison)
		{
			return comparison switch
			{
				MediaComparison.LessThan => actual < target,
				MediaComparison.LessThanOrEqual => actual <= target,
				MediaComparison.GreaterThan => actual > target,
				MediaComparison.GreaterThanOrEqual => actual >= target,
				_ => Mathf.Approximately(actual, target),
			};
		}
	}
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>
	/// Turns <c>@media</c> preludes into the flat tables a sheet carries.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One per sheet build, seeded with the unconditional query at index 0 so that "no media" and "a
	/// media that always holds" are the same cheap answer in the matcher.
	/// </para>
	/// <para>
	/// The prelude is read here rather than by the CSS library, because <c>UseMedia</c> hands a query
	/// over at runtime and a player carries no CSS library. One reader serves both, so a query means
	/// the same thing in a sheet as it does in C#. The grammar is the part of Media Queries 4 this
	/// runtime can evaluate: a list of <c>[not|only] type and (feature)…</c> or <c>(feature) and …</c>
	/// clauses, where a feature is <c>name: value</c>, a bare name, or a range such as
	/// <c>(width &gt;= 40rem)</c> or <c>(20rem &lt; width &lt;= 60rem)</c>. <c>or</c> and nested
	/// conditions are named in a diagnostic and never match.
	/// </para>
	/// </remarks>
	internal sealed class MediaQueryCompiler
	{
		/// <summary>
		/// The media types CSS defines. One outside this set is a typo worth naming; one inside it that
		/// this runtime is not — <c>print</c> — simply does not match, which is what CSS says.
		/// </summary>
		[NoAutoStaticsCleanup]
		private static readonly HashSet<string> s_knownTypes = new(StringComparer.OrdinalIgnoreCase)
		{
			"all", "screen", "print", "speech",
			"tty", "tv", "projection", "handheld", "braille", "embossed", "aural",
		};

		private readonly string _sourceName;
		private readonly List<string> _diagnostics;
		private readonly List<MediaQuery> _queries = new();
		private readonly List<MediaClause> _clauses = new();
		private readonly List<MediaFeatureTest> _features = new();

		internal MediaQueryCompiler(string sourceName, List<string> diagnostics)
		{
			_sourceName = sourceName;
			_diagnostics = diagnostics;

			// Index 0 is the unconditional query: no clauses, no parent, always true.
			_queries.Add(new MediaQuery(0, 0, 0, neverMatches: false));
		}

		internal MediaQuery[] BuildQueries() => _queries.ToArray();

		internal MediaClause[] BuildClauses() => _clauses.ToArray();

		internal MediaFeatureTest[] BuildFeatures() => _features.ToArray();

		/// <summary>
		/// Compiles one prelude, ANDed with the query it is nested inside.
		/// </summary>
		internal int Compile(string prelude, int parentIndex)
		{
			var clauseStart = _clauses.Count;
			var neverMatches = !TryCompileList(prelude, _clauses);

			var index = _queries.Count;
			_queries.Add(new MediaQuery(clauseStart, _clauses.Count - clauseStart, parentIndex, neverMatches));

			return index;
		}

		/// <summary>
		/// Compiles a query written outside a stylesheet — what <c>UseMedia</c> is handed.
		/// </summary>
		internal static MediaCondition CompileCondition(string query, List<string> diagnostics)
		{
			var compiler = new MediaQueryCompiler($"UseMedia(\"{query}\")", diagnostics);
			var clauses = new List<MediaClause>();
			var neverMatches = !compiler.TryCompileList(query, clauses) || clauses.Count == 0;

			return new MediaCondition(clauses.ToArray(), compiler.BuildFeatures(), neverMatches);
		}

		/// <summary>
		/// Compiles every comma-separated arm into <paramref name="into"/>, and says whether all of
		/// them could be read.
		/// </summary>
		/// <remarks>
		/// An arm that cannot be read is dropped and poisons the whole query rather than just itself.
		/// CSS would keep the readable arms, but a list where one arm is a typo is far more often a
		/// mistake than a deliberate fallback, and a query that quietly matches less than it says is the
		/// harder bug to find.
		/// </remarks>
		private bool TryCompileList(string prelude, List<MediaClause> into)
		{
			var ok = true;
			var arms = ValueParser.SplitTop(prelude, ',');

			if (arms.Count == 0)
			{
				Report($"@media has an empty condition; the query never matches.");

				return false;
			}

			foreach (var arm in arms)
			{
				if (TryCompileClause(arm, out var clause))
					into.Add(clause);
				else
					ok = false;
			}

			return ok;
		}

		private bool TryCompileClause(string text, out MediaClause clause)
		{
			clause = default;

			var featureStart = _features.Count;
			var inverse = false;
			var typeMatches = true;
			var sawType = false;
			var expectFeature = true;
			var pendingAnd = false;
			var index = 0;
			var first = true;

			while (true)
			{
				SkipWhitespace(text, ref index);

				if (index >= text.Length)
					break;

				if (text[index] == '(')
				{
					if (!expectFeature)
						return Unreadable(text, featureStart);

					var close = MatchingParen(text, index);
					if (close < 0)
						return Unreadable(text, featureStart);

					if (!TryCompileFeature(text.Substring(index + 1, close - index - 1).Trim()))
					{
						Rewind(featureStart);

						return false;
					}

					index = close + 1;
					expectFeature = false;
					pendingAnd = false;
					first = false;

					continue;
				}

				var word = ReadWord(text, ref index);

				if (word.Length == 0)
					return Unreadable(text, featureStart);

				if (first && word.Equals("not", StringComparison.OrdinalIgnoreCase))
				{
					inverse = true;

					continue;
				}

				// `only` exists to hide a query from parsers that predate media queries, which is not a
				// situation this runtime can be in.
				if (first && word.Equals("only", StringComparison.OrdinalIgnoreCase))
					continue;

				if (word.Equals("and", StringComparison.OrdinalIgnoreCase))
				{
					if (expectFeature)
						return Unreadable(text, featureStart);

					expectFeature = true;
					pendingAnd = true;

					continue;
				}

				if (word.Equals("or", StringComparison.OrdinalIgnoreCase))
				{
					Report($"@media '{text.Trim()}' uses 'or', which is not supported; use a comma-separated list instead. The query never matches.");
					Rewind(featureStart);

					return false;
				}

				if (sawType || !first)
					return Unreadable(text, featureStart);

				sawType = true;
				first = false;
				expectFeature = false;
				typeMatches = TypeMatches(word);
			}

			if (first || pendingAnd)
				return Unreadable(text, featureStart);

			clause = new MediaClause(featureStart, _features.Count - featureStart, inverse, typeMatches);

			return true;
		}

		private bool Unreadable(string text, int featureStart)
		{
			Rewind(featureStart);
			Report($"@media could not read '{Describe(text)}'; the query never matches.");

			return false;
		}

		private void Rewind(int featureStart)
		{
			if (_features.Count > featureStart)
				_features.RemoveRange(featureStart, _features.Count - featureStart);
		}

		private bool TypeMatches(string type)
		{
			if (type.Equals("all", StringComparison.OrdinalIgnoreCase)
				|| type.Equals("screen", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (!s_knownTypes.Contains(type))
				Report($"@media names an unknown media type '{type}'; the query never matches.");

			return false;
		}

		/// <summary>
		/// Compiles the inside of one pair of parentheses: <c>name: value</c>, a bare <c>name</c>, or a
		/// range with the name on either side of its comparison, or between two.
		/// </summary>
		private bool TryCompileFeature(string text)
		{
			if (text.Length == 0)
				return Unreadable("()", _features.Count);

			if (text[0] == '(' || StartsWithWord(text, "not"))
			{
				Report($"@media '({text})' nests a condition, which is not supported; the query never matches.");

				return false;
			}

			var colon = text.IndexOf(':');

			if (colon >= 0)
			{
				var name = text.Substring(0, colon).Trim();
				var value = text.Substring(colon + 1).Trim();
				var comparison = PrefixComparison(name, out var bare);

				return TryAddFeature(name, bare, comparison, value);
			}

			if (!TrySplitRange(text, out var parts, out var operators))
				return Unreadable($"({text})", _features.Count);

			if (parts.Count == 1)
			{
				Report($"@media '({text})' tests a feature without a value, which is not supported; the query never matches.");

				return false;
			}

			if (parts.Count == 2)
			{
				// `name op value`, or `value op name`, which reads the same with the operator mirrored.
				if (IsName(parts[0]))
					return TryAddFeature(parts[0], Bare(parts[0]), operators[0], parts[1]);

				if (IsName(parts[1]))
					return TryAddFeature(parts[1], Bare(parts[1]), Mirror(operators[0]), parts[0]);

				return Unreadable($"({text})", _features.Count);
			}

			// `low op name op high` is two tests on the one feature.
			if (parts.Count == 3 && IsName(parts[1]) && !IsEquality(operators[0]) && !IsEquality(operators[1]))
			{
				var start = _features.Count;

				if (TryAddFeature(parts[1], Bare(parts[1]), Mirror(operators[0]), parts[0])
					&& TryAddFeature(parts[1], Bare(parts[1]), operators[1], parts[2]))
				{
					return true;
				}

				Rewind(start);

				return false;
			}

			return Unreadable($"({text})", _features.Count);
		}

		private bool TryAddFeature(string name, string bare, MediaComparison comparison, string value)
		{
			switch (bare)
			{
				case "width":
				case "height":
				{
					var kind = bare == "width" ? MediaFeatureKind.Width : MediaFeatureKind.Height;

					if (!TryLength(value, out var length))
						return CannotParse(name, value);

					_features.Add(MediaFeatureTest.OfLength(kind, comparison, length));

					return true;
				}

				case "orientation":
				{
					if (value.Equals("portrait", StringComparison.OrdinalIgnoreCase))
						_features.Add(MediaFeatureTest.OfKeyword(MediaFeatureKind.Orientation, (int)MediaOrientation.Portrait));
					else if (value.Equals("landscape", StringComparison.OrdinalIgnoreCase))
						_features.Add(MediaFeatureTest.OfKeyword(MediaFeatureKind.Orientation, (int)MediaOrientation.Landscape));
					else
						return CannotParse(name, value);

					return true;
				}

				case "input-device":
					return TryKeyword<InputDeviceKind>(name, value, MediaFeatureKind.InputDevice);

				case "gamepad-layout":
					return TryKeyword<GamepadLayout>(name, value, MediaFeatureKind.GamepadLayout);

				case "aspect-ratio":
				{
					if (!TryRatio(value, out var ratio))
						return CannotParse(name, value);

					_features.Add(MediaFeatureTest.OfNumber(MediaFeatureKind.AspectRatio, comparison, ratio));

					return true;
				}

				default:
					Report($"@media has an unknown feature '{name}'; the query never matches.");

					return false;
			}
		}

		private bool CannotParse(string name, string value)
		{
			Report($"@media cannot parse '{name}: {value}'.");

			return false;
		}

		/// <summary>
		/// Reads a keyword feature whose values are an enum's members, written in kebab case
		/// (<c>playstation</c>, <c>gamepad</c>).
		/// </summary>
		private bool TryKeyword<TEnum>(string name, string value, MediaFeatureKind kind)
			where TEnum : struct, Enum
		{
			var bare = value.Trim().Replace("-", string.Empty);

			if (!Enum.TryParse<TEnum>(bare, ignoreCase: true, out var parsed) || !Enum.IsDefined(typeof(TEnum), parsed))
				return CannotParse(name, value);

			_features.Add(MediaFeatureTest.OfKeyword(kind, Convert.ToInt32(parsed)));

			return true;
		}

		/// <summary>
		/// The comparison a <c>min-</c> or <c>max-</c> prefix spells, with the prefix stripped from the
		/// name that comes back.
		/// </summary>
		private static MediaComparison PrefixComparison(string name, out string bare)
		{
			if (name.StartsWith("min-", StringComparison.OrdinalIgnoreCase))
			{
				bare = Bare(name.Substring(4));

				return MediaComparison.GreaterThanOrEqual;
			}

			if (name.StartsWith("max-", StringComparison.OrdinalIgnoreCase))
			{
				bare = Bare(name.Substring(4));

				return MediaComparison.LessThanOrEqual;
			}

			bare = Bare(name);

			return MediaComparison.Equal;
		}

		/// <summary>
		/// Splits a range at its comparison operators, which is the only thing that separates its parts.
		/// </summary>
		private static bool TrySplitRange(string text, out List<string> parts, out List<MediaComparison> operators)
		{
			parts = new List<string>(3);
			operators = new List<MediaComparison>(2);

			var start = 0;
			var i = 0;

			while (i < text.Length)
			{
				var c = text[i];

				if (c is not ('<' or '>' or '='))
				{
					i++;

					continue;
				}

				var orEqual = c != '=' && i + 1 < text.Length && text[i + 1] == '=';
				var comparison = c switch
				{
					'<' => orEqual ? MediaComparison.LessThanOrEqual : MediaComparison.LessThan,
					'>' => orEqual ? MediaComparison.GreaterThanOrEqual : MediaComparison.GreaterThan,
					_ => MediaComparison.Equal,
				};

				var part = text.Substring(start, i - start).Trim();
				if (part.Length == 0)
					return false;

				parts.Add(part);
				operators.Add(comparison);

				i += orEqual ? 2 : 1;
				start = i;
			}

			var last = text.Substring(start).Trim();
			if (last.Length == 0)
				return false;

			parts.Add(last);

			return parts.Count <= 3;
		}

		private static MediaComparison Mirror(MediaComparison comparison) => comparison switch
		{
			MediaComparison.LessThan => MediaComparison.GreaterThan,
			MediaComparison.LessThanOrEqual => MediaComparison.GreaterThanOrEqual,
			MediaComparison.GreaterThan => MediaComparison.LessThan,
			MediaComparison.GreaterThanOrEqual => MediaComparison.LessThanOrEqual,
			_ => comparison,
		};

		private static bool IsEquality(MediaComparison comparison) => comparison == MediaComparison.Equal;

		/// <summary>
		/// A feature name starts with a letter; every value this runtime compares against starts with a
		/// digit, a sign or a dot.
		/// </summary>
		private static bool IsName(string text) => text.Length > 0 && char.IsLetter(text[0]);

		private static string Bare(string name) => name.Trim().ToLowerInvariant();

		/// <remarks>
		/// A percentage or <c>auto</c> has nothing to resolve against — the viewport is what a percentage
		/// would be a percentage of — and a <c>calc()</c> reading a custom property has no scope here, so
		/// only the two absolute units are accepted.
		/// </remarks>
		private static bool TryLength(string text, out StyleLength length)
		{
			length = default;

			if (string.IsNullOrWhiteSpace(text) || !ValueParser.TryParseLength(text.Trim(), out var value))
				return false;

			if (value.Kind != StyleValueKind.Length)
				return false;

			length = value.AsLength();

			return length.Unit is LengthUnit.Points or LengthUnit.Rem;
		}

		/// <summary>Reads a <c>w/h</c> ratio, or a bare number for the degenerate form.</summary>
		private static bool TryRatio(string text, out float ratio)
		{
			ratio = 0f;

			if (string.IsNullOrWhiteSpace(text))
				return false;

			var parts = text.Split('/');

			if (parts.Length == 1)
				return TryNumber(parts[0], out ratio) && ratio > 0f;

			if (parts.Length != 2)
				return false;

			if (!TryNumber(parts[0], out var width) || !TryNumber(parts[1], out var height) || height <= 0f)
				return false;

			ratio = width / height;

			return ratio > 0f;
		}

		private static bool TryNumber(string text, out float number)
		{
			return float.TryParse(
				text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
		}

		private static void SkipWhitespace(string text, ref int index)
		{
			while (index < text.Length && char.IsWhiteSpace(text[index]))
				index++;
		}

		private static string ReadWord(string text, ref int index)
		{
			var start = index;

			while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != '(')
				index++;

			return text.Substring(start, index - start);
		}

		private static bool StartsWithWord(string text, string word)
		{
			return text.StartsWith(word, StringComparison.OrdinalIgnoreCase)
				&& (text.Length == word.Length || char.IsWhiteSpace(text[word.Length]) || text[word.Length] == '(');
		}

		private static int MatchingParen(string text, int open)
		{
			var depth = 0;

			for (var i = open; i < text.Length; i++)
			{
				if (text[i] == '(')
					depth++;
				else if (text[i] == ')' && --depth == 0)
					return i;
			}

			return -1;
		}

		private void Report(string message) => _diagnostics.Add($"{_sourceName}: {message}");

		private static string Describe(string text)
		{
			return string.IsNullOrWhiteSpace(text) ? "(empty)" : text.Trim();
		}
	}
}

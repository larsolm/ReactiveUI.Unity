using System.Collections.Generic;

namespace ReactiveUI
{
	/// <summary>
	/// Flattens the selector text of a nested rule into selectors <see cref="SelectorParser"/> accepts.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The CSS library resolves <c>&amp;</c> itself, but it always wraps the parent in <c>:is(…)</c> —
	/// even when the parent is a single class — and wraps again once per nesting level. Nothing in the
	/// engine understands a functional pseudo-class, so the wrapper is spliced away here before a
	/// selector ever reaches the parser.
	/// </para>
	/// <para>
	/// <c>&amp;</c> means exactly what it means in CSS: it stands for the enclosing selector and
	/// compounds with what surrounds it. It never joins onto the parent's text. A suffix that would
	/// continue an identifier — <c>&amp;--big</c>, which the library leaves as the literal
	/// <c>:is(.tile)--big</c> — is a Sass spelling and is reported rather than guessed at.
	/// </para>
	/// <para>
	/// One authored selector can still expand into several, because a <c>:is(…)</c> holding a list has
	/// no flat spelling. <see cref="CssBuilder"/> scores every member of such a group with the group's
	/// highest specificity, which is what CSS means by taking a <c>:is()</c> at its most specific
	/// argument.
	/// </para>
	/// </remarks>
	internal static class SelectorDesugar
	{
		/// <summary>Cap on the selectors one authored selector may expand into.</summary>
		private const int MaxExpansion = 64;

		/// <summary>Cap on nesting depth, so a pathological selector cannot recurse without end.</summary>
		private const int MaxDepth = 16;

		/// <summary>Flattens a rule's selector text into the selectors it stands for.</summary>
		internal static void Expand(string selectorText, string sourceName, List<string> results, List<string> diagnostics)
		{
			var parts = new List<string>();
			SplitList(selectorText, parts);

			foreach (var part in parts)
				ExpandOne(part, selectorText, sourceName, results, diagnostics, 0);
		}

		/// <summary>
		/// Flattens one complex selector of a list, so its expansion can be scored as a group.
		/// </summary>
		/// <remarks>
		/// <see cref="MaxExpansion"/> applies per call, and so counts per authored selector rather than
		/// per rule. That is the bound worth having: it is one selector's list nesting that blows up.
		/// </remarks>
		internal static void ExpandPart(
			string part, string origin, string sourceName, List<string> results, List<string> diagnostics)
		{
			ExpandOne(part, origin, sourceName, results, diagnostics, 0);
		}

		/// <summary>Splits a selector list on its top-level commas.</summary>
		/// <remarks>
		/// A resolved nested selector carries commas inside <c>:is(…)</c>, so the split has to count
		/// parentheses rather than take every comma it finds.
		/// </remarks>
		internal static void SplitList(string text, List<string> parts)
		{
			var depth = 0;
			var start = 0;

			for (var i = 0; i < text.Length; i++)
			{
				var c = text[i];

				if (c == '(') depth++;
				else if (c == ')') depth--;
				else if (c == ',' && depth == 0)
				{
					AddTrimmed(text, start, i, parts);
					start = i + 1;
				}
			}

			AddTrimmed(text, start, text.Length, parts);
		}

		private static void AddTrimmed(string text, int start, int end, List<string> parts)
		{
			var part = text.Substring(start, end - start).Trim();

			if (part.Length > 0) parts.Add(part);
		}

		private static void ExpandOne(
			string selector, string origin, string sourceName, List<string> results, List<string> diagnostics, int depth)
		{
			if (depth > MaxDepth)
			{
				diagnostics.Add($"{sourceName}: '{origin}' nests too deeply to flatten.");

				return;
			}

			if (!TryFindIs(selector, out var start, out var open, out var close))
			{
				var trimmed = selector.Trim();

				if (trimmed.Length > 0) results.Add(trimmed);

				return;
			}

			var prefix = selector.Substring(0, start);
			var inner = selector.Substring(open + 1, close - open - 1);
			var suffix = selector.Substring(close + 1);

			var parts = new List<string>();
			SplitList(inner, parts);

			var alternatives = new List<string>();

			foreach (var part in parts)
				ExpandOne(part, origin, sourceName, alternatives, diagnostics, depth + 1);

			foreach (var alternative in alternatives)
			{
				if (results.Count >= MaxExpansion)
				{
					diagnostics.Add(
						$"{sourceName}: '{origin}' expands past {MaxExpansion} selectors; narrow the selector lists it nests under.");

					return;
				}

				if (!TrySplice(prefix, alternative, suffix, out var spliced, out var reason))
				{
					diagnostics.Add($"{sourceName}: '{origin}' cannot be flattened — {reason}");

					continue;
				}

				ExpandOne(spliced, origin, sourceName, results, diagnostics, depth + 1);
			}
		}

		/// <summary>Splices what <c>&amp;</c> stood for into the selector around it.</summary>
		private static bool TrySplice(string prefix, string inner, string suffix, out string spliced, out string reason)
		{
			spliced = "";

			// A suffix opening on an identifier character is `&--mod` or `&__el`. CSS nesting has no
			// concatenation — `&` compounds, it never extends the parent's text — so this is reported
			// rather than joined, however usable the join would have been.
			if (suffix.Length > 0 && IsIdentifierPart(suffix[0]))
			{
				var stem = Stem(suffix);

				reason =
					$"'&{suffix}' would join '{suffix}' onto the text of '{inner}'. That is a Sass spelling; "
					+ $"CSS nesting has no concatenation. Write '& .{stem}' for an element inside '{inner}', "
					+ $"or '&.{stem}' for a class on '{inner}' itself.";

				return false;
			}

			// A parent spanning several compounds carries its own subject, so splicing it after anything
			// else — `.x :is(.a .b)` — moves which node the selector is about. CSS says the subject stays
			// `.b`; a flat splice would make it `.a`, and there is no flat spelling of the real meaning.
			if (prefix.Length > 0 && IsComplex(inner))
			{
				reason =
					$"'&' stands for '{inner}', which spans several compounds, and it is used here after "
					+ $"'{prefix}'. Write this selector out in full.";

				return false;
			}

			spliced = prefix + inner + suffix;
			reason = "";

			return true;
		}

		/// <summary>The suffix with its leading separators dropped, for use in a diagnostic.</summary>
		private static string Stem(string suffix)
		{
			var index = 0;

			while (index < suffix.Length && (suffix[index] == '-' || suffix[index] == '_'))
				index++;

			return index < suffix.Length ? suffix.Substring(index) : suffix;
		}

		private static bool TryFindIs(string selector, out int start, out int open, out int close)
		{
			start = 0;
			open = 0;
			close = 0;

			for (var i = 0; i + 3 < selector.Length; i++)
			{
				if (selector[i] != ':') continue;
				if (selector[i + 1] != 'i' && selector[i + 1] != 'I') continue;
				if (selector[i + 2] != 's' && selector[i + 2] != 'S') continue;
				if (selector[i + 3] != '(') continue;

				if (!TryMatchParen(selector, i + 3, out close)) continue;

				start = i;
				open = i + 3;

				return true;
			}

			return false;
		}

		private static bool TryMatchParen(string text, int open, out int close)
		{
			var depth = 0;

			for (var i = open; i < text.Length; i++)
			{
				if (text[i] == '(')
				{
					depth++;
				}
				else if (text[i] == ')' && --depth == 0)
				{
					close = i;

					return true;
				}
			}

			close = 0;

			return false;
		}

		/// <summary>Whether a selector spans more than one compound.</summary>
		private static bool IsComplex(string selector)
		{
			var depth = 0;

			for (var i = 0; i < selector.Length; i++)
			{
				var c = selector[i];

				if (c == '(') depth++;
				else if (c == ')') depth--;
				else if (depth == 0 && (char.IsWhiteSpace(c) || c == '>' || c == '+' || c == '~')) return true;
			}

			return false;
		}

		private static bool IsIdentifierPart(char c)
		{
			return char.IsLetterOrDigit(c) || c == '_' || c == '-';
		}
	}
}

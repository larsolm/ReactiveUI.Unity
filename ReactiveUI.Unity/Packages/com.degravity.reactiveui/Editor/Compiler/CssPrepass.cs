using System;
using System.Collections.Generic;
using System.Text;

namespace ReactiveUI
{
	/// <summary>
	/// Text-level passes over a sheet, for the few things the CSS library cannot be trusted to keep.
	/// </summary>
	internal static class CssPrepass
	{
		/// <summary>
		/// Blanks out any <c>@scope</c> written inside a style rule, with a diagnostic.
		/// </summary>
		/// <remarks>
		/// CSS allows <c>.card { @scope (.title) { … } }</c>, scoped to whatever the enclosing rule
		/// matched, but this framework does not support it yet. Left in, the CSS library — which does not
		/// know <c>@scope</c> — reads the block as a nested rule with a universal parent, and its rules
		/// come out applying everywhere. Removing the text is the only way to keep that from happening
		/// silently. Line breaks are kept, so positions in later diagnostics stay right.
		/// </remarks>
		internal static string RemoveScopesInStyleRules(string css, string sourceName, List<string> diagnostics)
		{
			if (css.IndexOf("@scope", StringComparison.OrdinalIgnoreCase) < 0)
				return css;

			StringBuilder? output = null;

			// Whether each open block holds rules (the sheet, @media, @scope …) or declarations (a style
			// rule, @font-face …). Only the second can hide a nested @scope.
			var blocks = new Stack<bool>();
			blocks.Push(true);

			var statementStart = 0;
			var i = 0;

			while (i < css.Length)
			{
				var c = css[i];

				if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
				{
					var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
					i = end < 0 ? css.Length : end + 2;

					continue;
				}

				if (c is '"' or '\'')
				{
					i = SkipString(css, i);

					continue;
				}

				if (c == '@' && !blocks.Peek() && IsKeywordAt(css, i, "@scope"))
				{
					var open = IndexOfBlock(css, i);
					var close = open < 0 ? -1 : MatchingBrace(css, open);
					var end = close < 0 ? css.Length : close + 1;

					diagnostics.Add(
						$"{sourceName}: an @scope inside a style rule is not supported and is ignored; move it to "
						+ "the top level and give it its own root, as in '@scope (.card .title) { … }'.");

					output ??= new StringBuilder(css);

					for (var k = i; k < end; k++)
					{
						if (output[k] != '\n')
							output[k] = ' ';
					}

					i = end;
					statementStart = i;

					continue;
				}

				switch (c)
				{
					case '{':
						blocks.Push(HoldsRules(css.Substring(statementStart, i - statementStart)));
						statementStart = i + 1;

						break;

					case '}':
						if (blocks.Count > 1)
							blocks.Pop();

						statementStart = i + 1;

						break;

					case ';':
						statementStart = i + 1;

						break;
				}

				i++;
			}

			return output?.ToString() ?? css;
		}

		/// <summary>
		/// Rewrites the body of an <c>@scope</c> block into rules the CSS library parses on their own.
		/// </summary>
		/// <remarks>
		/// Each run of declarations written directly in the block is wrapped in
		/// <c>:where(:scope) { … }</c>, and each selector that opens on a combinator (<c>&gt; .item</c>)
		/// gets <c>:where(:scope)</c> in front — which is how CSS reads both. Conditional group rules
		/// inside the block are rewritten the same way; anything else is copied as written.
		/// </remarks>
		internal static string RelativizeScopeBody(string body)
		{
			var output = new StringBuilder(body.Length + 32);
			RelativizeBlock(body, 0, body.Length, output);

			return output.ToString();
		}

		private const string ScopeRoot = ":where(:scope)";

		private static void RelativizeBlock(string text, int start, int end, StringBuilder output)
		{
			var statementStart = start;
			var wrapping = false;
			var depth = 0;
			var i = start;

			while (i < end)
			{
				var c = text[i];

				if (c == '/' && i + 1 < end && text[i + 1] == '*')
				{
					var close = text.IndexOf("*/", i + 2, end - i - 2, StringComparison.Ordinal);
					i = close < 0 ? end : close + 2;

					continue;
				}

				if (c is '"' or '\'')
				{
					i = Math.Min(SkipString(text, i), end);

					continue;
				}

				if (c == '(')
				{
					depth++;
				}
				else if (c == ')')
				{
					depth--;
				}
				else if (c == ';' && depth <= 0)
				{
					var statement = text.Substring(statementStart, i + 1 - statementStart);

					if (IsAtRule(statement))
						EndWrap(output, ref wrapping);
					else
						BeginWrap(output, ref wrapping);

					output.Append(statement);
					statementStart = i + 1;
				}
				else if (c == '{' && depth <= 0)
				{
					var close = MatchingBrace(text, i);
					var blockEnd = close < 0 || close >= end ? end : close;
					var prelude = text.Substring(statementStart, i - statementStart);

					EndWrap(output, ref wrapping);

					if (!IsAtRule(prelude))
					{
						output.Append(RelativizeSelectorList(prelude)).Append(text, i, Math.Min(blockEnd + 1, end) - i);
					}
					else if (IsConditionalGroup(prelude))
					{
						output.Append(prelude).Append('{');
						RelativizeBlock(text, i + 1, blockEnd, output);

						if (blockEnd < end)
							output.Append('}');
					}
					else
					{
						output.Append(text, statementStart, Math.Min(blockEnd + 1, end) - statementStart);
					}

					i = blockEnd + 1;
					statementStart = i;

					continue;
				}

				i++;
			}

			// A last declaration may leave off its semicolon.
			if (statementStart < end)
			{
				var rest = text.Substring(statementStart, end - statementStart);

				if (HasContent(rest))
					BeginWrap(output, ref wrapping);

				output.Append(rest);
			}

			EndWrap(output, ref wrapping);
		}

		private static void BeginWrap(StringBuilder output, ref bool wrapping)
		{
			if (wrapping)
				return;

			output.Append(ScopeRoot).Append(" {");
			wrapping = true;
		}

		private static void EndWrap(StringBuilder output, ref bool wrapping)
		{
			if (!wrapping)
				return;

			output.Append('}');
			wrapping = false;
		}

		/// <summary>Puts <c>:where(:scope)</c> before each selector in a list that opens on a combinator.</summary>
		private static string RelativizeSelectorList(string prelude)
		{
			var parts = new List<string>();
			SelectorDesugar.SplitList(prelude, parts);

			var changed = false;

			for (var i = 0; i < parts.Count; i++)
			{
				var first = FirstContentIndex(parts[i]);

				if (first < parts[i].Length && parts[i][first] is '>' or '+' or '~')
				{
					parts[i] = ScopeRoot + " " + parts[i];
					changed = true;
				}
			}

			return changed ? string.Join(", ", parts) + " " : prelude;
		}

		/// <summary>Whether a statement or prelude, past any whitespace and comments, is an at-rule.</summary>
		private static bool IsAtRule(string text)
		{
			var first = FirstContentIndex(text);

			return first < text.Length && text[first] == '@';
		}

		private static bool HasContent(string text) => FirstContentIndex(text) < text.Length;

		/// <summary>The rules inside these are scoped like the rules around them.</summary>
		private static bool IsConditionalGroup(string prelude)
		{
			var first = FirstContentIndex(prelude);

			return IsKeywordAt(prelude, first, "@media")
				|| IsKeywordAt(prelude, first, "@supports")
				|| IsKeywordAt(prelude, first, "@layer")
				|| IsKeywordAt(prelude, first, "@container");
		}

		/// <summary>The index of the first character that is neither whitespace nor inside a comment.</summary>
		internal static int FirstContentIndex(string text)
		{
			var i = 0;

			while (i < text.Length)
			{
				if (char.IsWhiteSpace(text[i]))
				{
					i++;
				}
				else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
				{
					var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
					i = end < 0 ? text.Length : end + 2;
				}
				else
				{
					break;
				}
			}

			return i;
		}

		/// <summary>
		/// The index of the first <c>{</c> at or after <paramref name="start"/> that is not inside a
		/// string, a comment or parentheses — where a rule's block opens.
		/// </summary>
		internal static int IndexOfBlock(string text, int start)
		{
			var depth = 0;
			var i = start;

			while (i < text.Length)
			{
				var c = text[i];

				if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
				{
					var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
					i = end < 0 ? text.Length : end + 2;

					continue;
				}

				if (c is '"' or '\'')
				{
					i = SkipString(text, i);

					continue;
				}

				if (c == '(')
					depth++;
				else if (c == ')')
					depth--;
				else if (c == '{' && depth <= 0)
					return i;

				i++;
			}

			return -1;
		}

		internal static int MatchingBrace(string text, int open)
		{
			var depth = 0;
			var i = open;

			while (i < text.Length)
			{
				var c = text[i];

				if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
				{
					var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
					i = end < 0 ? text.Length : end + 2;

					continue;
				}

				if (c is '"' or '\'')
				{
					i = SkipString(text, i);

					continue;
				}

				if (c == '{')
					depth++;
				else if (c == '}' && --depth == 0)
					return i;

				i++;
			}

			return -1;
		}

		internal static int SkipString(string text, int open)
		{
			var quote = text[open];

			for (var i = open + 1; i < text.Length; i++)
			{
				if (text[i] == '\\')
				{
					i++;

					continue;
				}

				if (text[i] == quote || text[i] == '\n')
					return i + 1;
			}

			return text.Length;
		}

		/// <summary>Whether the block a prelude opens holds rules rather than declarations.</summary>
		internal static bool HoldsRules(string prelude)
		{
			var text = prelude.Trim();

			if (text.Length == 0 || text[0] != '@')
				return false;

			return IsKeywordAt(text, 0, "@media")
				|| IsKeywordAt(text, 0, "@scope")
				|| IsKeywordAt(text, 0, "@layer")
				|| IsKeywordAt(text, 0, "@supports")
				|| IsKeywordAt(text, 0, "@container")
				|| IsKeywordAt(text, 0, "@document")
				|| IsKeywordAt(text, 0, "@keyframes");
		}

		internal static bool IsKeywordAt(string text, int index, string keyword)
		{
			if (string.Compare(text, index, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0)
				return false;

			var after = index + keyword.Length;

			return after >= text.Length || !(char.IsLetterOrDigit(text[after]) || text[after] == '-' || text[after] == '_');
		}
	}
}

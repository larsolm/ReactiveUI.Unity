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

		private static int MatchingBrace(string text, int open)
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

		private static int SkipString(string text, int open)
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
		private static bool HoldsRules(string prelude)
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

		private static bool IsKeywordAt(string text, int index, string keyword)
		{
			if (string.Compare(text, index, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0)
				return false;

			var after = index + keyword.Length;

			return after >= text.Length || !(char.IsLetterOrDigit(text[after]) || text[after] == '-' || text[after] == '_');
		}
	}
}

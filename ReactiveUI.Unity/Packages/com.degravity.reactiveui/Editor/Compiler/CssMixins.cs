using System;
using System.Collections.Generic;
using System.Text;

namespace ReactiveUI
{
	/// <summary>
	/// Expands <c>@mixin</c> / <c>@apply</c> (CSS Functions and Mixins) into plain nested CSS.
	/// </summary>
	/// <remarks>
	/// An <c>@apply</c> sees the mixins of its own sheet and of every sheet it imports, directly or
	/// not. Where a name is defined more than once the last definition in cascade order wins, imported
	/// sheets coming first. A parameter read with <c>var(--param)</c> is replaced by the argument text.
	/// </remarks>
	internal static class CssMixins
	{
		private sealed class Parameter
		{
			public string Name = string.Empty;
			public string? Default;
		}

		private sealed class Mixin
		{
			public string Name = string.Empty;
			public readonly List<Parameter> Parameters = new();
			public string Body = string.Empty;
		}

		private enum BlockKind
		{
			/// <summary>Holds rules: the sheet, or a grouping rule outside any style rule.</summary>
			Rules,

			/// <summary>Holds declarations: a style rule, <c>@scope</c>, or a group nested in either.</summary>
			Style,

			/// <summary>Anything else: <c>@font-face</c>, <c>@keyframes</c> and the like.</summary>
			Other,
		}

		/// <summary>
		/// Returns <paramref name="css"/> with every <c>@mixin</c> removed and every <c>@apply</c> replaced
		/// by what it applies.
		/// </summary>
		/// <param name="readSheet">Reads an imported sheet's text by asset path, or returns null.</param>
		/// <param name="dependencies">Receives the path of every imported sheet that was read.</param>
		internal static string Expand(
			string css,
			string sourceName,
			Func<string, string?>? readSheet,
			List<string> diagnostics,
			List<string> dependencies)
		{
			var hasApply = Contains(css, "@apply");

			if (!hasApply && !Contains(css, "@mixin"))
				return css;

			var mixins = new Dictionary<string, Mixin>(StringComparer.Ordinal);
			var visited = new HashSet<string>(StringComparer.Ordinal) { sourceName };
			var stripped = Gather(css, sourceName, hasApply ? readSheet : null, visited, mixins, diagnostics, dependencies);

			if (!hasApply && !Contains(stripped, "@mixin"))
				return stripped;

			var expander = new Expander(sourceName, mixins, diagnostics);
			var output = new StringBuilder(stripped.Length + 64);
			expander.ExpandBlock(stripped, 0, stripped.Length, BlockKind.Rules, output);

			return output.ToString();
		}

		private static bool Contains(string text, string keyword) =>
			text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

		/// <summary>
		/// Files a sheet's mixins, after those of the sheets it imports, and returns its text without them.
		/// </summary>
		private static string Gather(
			string css,
			string path,
			Func<string, string?>? readSheet,
			HashSet<string> visited,
			Dictionary<string, Mixin> into,
			List<string> diagnostics,
			List<string> dependencies)
		{
			var own = new List<Mixin>();
			var stripped = Collect(css, path, own, diagnostics);

			if (readSheet is not null && Contains(stripped, "@import"))
			{
				var scratch = new List<string>();
				var parsed = CssBuilder.Parse(stripped, path, scratch);

				if (parsed is not null)
				{
					foreach (var import in CssBuilder.ReadImports(parsed, path, scratch))
					{
						if (!visited.Add(import.Path))
							continue;

						var text = readSheet(import.Path);

						if (text is null)
							continue;

						dependencies.Add(import.Path);
						Gather(text, import.Path, readSheet, visited, into, scratch, dependencies);
					}
				}
			}

			foreach (var mixin in own)
				into[mixin.Name] = mixin;

			return stripped;
		}

		/// <summary>Reads every top-level <c>@mixin</c> into <paramref name="into"/> and returns the text without them.</summary>
		private static string Collect(string css, string sourceName, List<Mixin> into, List<string> diagnostics)
		{
			if (!Contains(css, "@mixin"))
				return css;

			var output = new StringBuilder(css.Length);
			var statementStart = 0;
			var copied = 0;
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
					i = CssPrepass.SkipString(css, i);

					continue;
				}

				if (c == '@' && AtStatementStart(css, statementStart, i) && CssPrepass.IsKeywordAt(css, i, "@mixin"))
				{
					var end = StatementEnd(css, i, css.Length, out var open, out var close);

					if (open < 0)
						diagnostics.Add($"{sourceName}: '{Abbreviate(css, i, end)}' has no body and is ignored.");
					else if (TryReadMixin(css.Substring(i + 6, open - i - 6), css.Substring(open + 1, close - open - 1), out var mixin, out var error))
						into.Add(mixin);
					else
						diagnostics.Add($"{sourceName}: '{Abbreviate(css, i, end)}' {error}");

					output.Append(css, copied, i - copied);
					copied = end;
					i = end;
					statementStart = i;

					continue;
				}

				switch (c)
				{
					case '{':
					{
						var close = CssPrepass.MatchingBrace(css, i);
						i = close < 0 ? css.Length : close + 1;
						statementStart = i;

						continue;
					}

					case ';':
						statementStart = i + 1;

						break;
				}

				i++;
			}

			return output.Append(css, copied, css.Length - copied).ToString();
		}

		/// <summary>Reads <c>--name(--p &lt;type&gt;: default, …)</c> and the body that follows.</summary>
		private static bool TryReadMixin(string prelude, string body, out Mixin mixin, out string error)
		{
			mixin = new Mixin { Body = body };
			error = string.Empty;

			var text = CssBuilder.StripComments(prelude).Trim();
			var nameEnd = IdentifierEnd(text, 0);

			mixin.Name = text.Substring(0, nameEnd);

			if (!mixin.Name.StartsWith("--", StringComparison.Ordinal) || mixin.Name.Length < 3)
			{
				error = "needs a name that starts with '--', as in '@mixin --card { … }'.";

				return false;
			}

			var rest = text.Substring(nameEnd).Trim();

			if (rest.Length == 0)
				return true;

			if (rest[0] != '(' || rest[rest.Length - 1] != ')')
			{
				error = "has an unreadable parameter list.";

				return false;
			}

			foreach (var part in SplitArguments(rest.Substring(1, rest.Length - 2)))
			{
				var parameter = ReadParameter(part);

				if (parameter is null)
				{
					error = $"has an unreadable parameter '{part.Trim()}'.";

					return false;
				}

				mixin.Parameters.Add(parameter);
			}

			return true;
		}

		/// <summary>Reads <c>--name &lt;type&gt;? [: default]?</c>; the type is not checked.</summary>
		private static Parameter? ReadParameter(string text)
		{
			text = text.Trim();

			var nameEnd = IdentifierEnd(text, 0);
			var name = text.Substring(0, nameEnd);

			if (!name.StartsWith("--", StringComparison.Ordinal) || name.Length < 3)
				return null;

			var parameter = new Parameter { Name = name };
			var colon = IndexOfTopLevel(text, ':', nameEnd);

			if (colon >= 0)
				parameter.Default = Unwrap(text.Substring(colon + 1));

			return parameter;
		}

		/// <summary>Walks a sheet, replacing each <c>@apply</c> with the mixin it names.</summary>
		private sealed class Expander
		{
			private readonly string _sourceName;
			private readonly Dictionary<string, Mixin> _mixins;
			private readonly List<string> _diagnostics;
			private readonly List<string> _applying = new();

			internal Expander(string sourceName, Dictionary<string, Mixin> mixins, List<string> diagnostics)
			{
				_sourceName = sourceName;
				_mixins = mixins;
				_diagnostics = diagnostics;
			}

			internal void ExpandBlock(string text, int start, int end, BlockKind kind, StringBuilder output)
			{
				var statementStart = start;
				var copied = start;
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
						i = Math.Min(CssPrepass.SkipString(text, i), end);

						continue;
					}

					if (c == '@' && depth <= 0 && AtStatementStart(text, statementStart, i))
					{
						var apply = CssPrepass.IsKeywordAt(text, i, "@apply");

						if (apply || CssPrepass.IsKeywordAt(text, i, "@mixin"))
						{
							var statementEnd = StatementEnd(text, i, end, out var open, out var close);

							output.Append(text, copied, i - copied);

							if (!apply)
							{
								_diagnostics.Add(
									$"{_sourceName}: '{Abbreviate(text, i, statementEnd)}' is ignored; @mixin must be "
									+ "written at the top level of a sheet.");
							}
							else if (kind != BlockKind.Style)
							{
								_diagnostics.Add(
									$"{_sourceName}: '{Abbreviate(text, i, statementEnd)}' is ignored; @apply only works "
									+ "inside a style rule.");
							}
							else
							{
								var preludeEnd = open < 0 ? statementEnd : open;
								var contents = open < 0 ? null : text.Substring(open + 1, close - open - 1);

								Apply(text.Substring(i + 6, preludeEnd - i - 6).TrimEnd().TrimEnd(';'), contents, output);
							}

							copied = statementEnd;
							i = statementEnd;
							statementStart = i;

							continue;
						}
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
						statementStart = i + 1;
					}
					else if (c == '{' && depth <= 0)
					{
						var close = CssPrepass.MatchingBrace(text, i);
						var blockEnd = close < 0 || close >= end ? end : close;

						output.Append(text, copied, i + 1 - copied);
						ExpandBlock(text, i + 1, blockEnd, KindOf(text.Substring(statementStart, i - statementStart), kind), output);

						if (blockEnd < end)
							output.Append('}');

						i = blockEnd + 1;
						copied = Math.Min(i, end);
						statementStart = copied;

						continue;
					}

					i++;
				}

				if (copied < end)
					output.Append(text, copied, end - copied);
			}

			/// <summary>Writes what <c>@apply &lt;prelude&gt; { contents }</c> stands for.</summary>
			private void Apply(string prelude, string? contents, StringBuilder output)
			{
				var text = CssBuilder.StripComments(prelude).Trim();
				var nameEnd = IdentifierEnd(text, 0);
				var name = text.Substring(0, nameEnd);
				var written = "@apply " + text;

				if (!_mixins.TryGetValue(name, out var mixin))
				{
					_diagnostics.Add($"{_sourceName}: '{written}' names no mixin, and does nothing.");

					return;
				}

				var arguments = new List<string?>();
				var rest = text.Substring(nameEnd).Trim();

				if (rest.Length > 0)
				{
					if (rest[0] != '(' || rest[rest.Length - 1] != ')')
					{
						_diagnostics.Add($"{_sourceName}: '{written}' has an unreadable argument list, and does nothing.");

						return;
					}

					var inner = rest.Substring(1, rest.Length - 2);

					if (inner.Trim().Length > 0)
					{
						foreach (var argument in SplitArguments(inner))
						{
							var value = Unwrap(argument);
							arguments.Add(value.Length == 0 ? null : value);
						}
					}
				}

				if (arguments.Count > mixin.Parameters.Count)
				{
					_diagnostics.Add(
						$"{_sourceName}: '{written}' passes {arguments.Count} arguments to a mixin that takes "
						+ $"{mixin.Parameters.Count}, and does nothing.");

					return;
				}

				if (_applying.Contains(name))
				{
					_diagnostics.Add(
						$"{_sourceName}: '{written}' applies itself ({string.Join(" → ", _applying)} → {name}), and does nothing.");

					return;
				}

				var values = new Dictionary<string, string?>(StringComparer.Ordinal);

				for (var p = 0; p < mixin.Parameters.Count; p++)
				{
					var parameter = mixin.Parameters[p];
					values[parameter.Name] = p < arguments.Count && arguments[p] is not null ? arguments[p] : parameter.Default;
				}

				var missing = new List<string>();
				var body = Substitute(mixin.Body, values, missing);

				if (missing.Count > 0)
				{
					_diagnostics.Add(
						$"{_sourceName}: '{written}' leaves {string.Join(", ", missing)} without a value, and does nothing.");

					return;
				}

				body = ReplaceContents(body, contents);

				_applying.Add(name);
				ExpandBlock(body, 0, body.Length, BlockKind.Style, output);
				_applying.RemoveAt(_applying.Count - 1);
			}

			/// <summary>The kind of block a prelude opens, inside a block of <paramref name="parent"/>'s kind.</summary>
			private static BlockKind KindOf(string prelude, BlockKind parent)
			{
				var first = CssPrepass.FirstContentIndex(prelude);

				if (first >= prelude.Length || prelude[first] != '@')
					return parent == BlockKind.Other ? BlockKind.Other : BlockKind.Style;

				if (CssPrepass.IsKeywordAt(prelude, first, "@scope"))
					return BlockKind.Style;

				if (CssPrepass.IsKeywordAt(prelude, first, "@media")
					|| CssPrepass.IsKeywordAt(prelude, first, "@supports")
					|| CssPrepass.IsKeywordAt(prelude, first, "@layer")
					|| CssPrepass.IsKeywordAt(prelude, first, "@container"))
					return parent;

				return BlockKind.Other;
			}
		}

		/// <summary>
		/// Replaces each <c>var(--param[, fallback])</c> naming a parameter with its value, or the fallback
		/// when it has none. A parameter with neither is added to <paramref name="missing"/>.
		/// </summary>
		private static string Substitute(string text, Dictionary<string, string?> values, List<string> missing)
		{
			if (values.Count == 0 || !Contains(text, "var("))
				return text;

			var output = new StringBuilder(text.Length);
			var copied = 0;
			var i = 0;

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
					i = CssPrepass.SkipString(text, i);

					continue;
				}

				if ((c == 'v' || c == 'V')
					&& string.Compare(text, i, "var(", 0, 4, StringComparison.OrdinalIgnoreCase) == 0
					&& (i == 0 || !IsIdentifierPart(text[i - 1])))
				{
					var close = MatchingParen(text, i + 3);

					if (close > 0)
					{
						var inner = text.Substring(i + 4, close - i - 4);
						var comma = IndexOfTopLevel(inner, ',', 0);
						var name = (comma < 0 ? inner : inner.Substring(0, comma)).Trim();

						if (values.TryGetValue(name, out var value))
						{
							if (value is null && comma >= 0)
								value = Substitute(inner.Substring(comma + 1).Trim(), values, missing);

							if (value is null && !missing.Contains(name))
								missing.Add(name);

							output.Append(text, copied, i - copied).Append(value);
							i = close + 1;
							copied = i;

							continue;
						}
					}
				}

				i++;
			}

			return output.Append(text, copied, text.Length - copied).ToString();
		}

		/// <summary>Replaces each <c>@contents [{ fallback }]</c> with the contents block, or with its fallback.</summary>
		private static string ReplaceContents(string body, string? contents)
		{
			if (!Contains(body, "@contents"))
				return body;

			var output = new StringBuilder(body.Length);
			var copied = 0;
			var i = 0;

			while (i < body.Length)
			{
				var c = body[i];

				if (c == '/' && i + 1 < body.Length && body[i + 1] == '*')
				{
					var end = body.IndexOf("*/", i + 2, StringComparison.Ordinal);
					i = end < 0 ? body.Length : end + 2;

					continue;
				}

				if (c is '"' or '\'')
				{
					i = CssPrepass.SkipString(body, i);

					continue;
				}

				if (c == '@' && CssPrepass.IsKeywordAt(body, i, "@contents"))
				{
					var end = StatementEnd(body, i, body.Length, out var open, out var close);
					var fallback = open < 0 ? string.Empty : body.Substring(open + 1, close - open - 1);

					output.Append(body, copied, i - copied).Append(contents ?? fallback);
					i = end;
					copied = i;

					continue;
				}

				i++;
			}

			return output.Append(body, copied, body.Length - copied).ToString();
		}

		/// <summary>
		/// Where the at-rule at <paramref name="start"/> ends: past its <c>;</c>, or past its block and any
		/// <c>;</c> right after it. <paramref name="open"/> and <paramref name="close"/> are the block's
		/// braces, or -1 when it has none.
		/// </summary>
		private static int StatementEnd(string text, int start, int end, out int open, out int close)
		{
			open = -1;
			close = -1;

			var depth = 0;
			var i = start;

			while (i < end)
			{
				var c = text[i];

				if (c == '/' && i + 1 < end && text[i + 1] == '*')
				{
					var commentEnd = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
					i = commentEnd < 0 ? end : commentEnd + 2;

					continue;
				}

				if (c is '"' or '\'')
				{
					i = CssPrepass.SkipString(text, i);

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
					return i + 1;
				}
				else if (c == '{' && depth <= 0)
				{
					open = i;
					close = CssPrepass.MatchingBrace(text, i);

					if (close < 0 || close >= end)
					{
						close = end;

						return end;
					}

					var after = close + 1;

					while (after < end && char.IsWhiteSpace(text[after]))
						after++;

					return after < end && text[after] == ';' ? after + 1 : close + 1;
				}

				i++;
			}

			return end;
		}

		private static bool AtStatementStart(string text, int statementStart, int index) =>
			CssPrepass.FirstContentIndex(text.Substring(statementStart, index - statementStart)) == index - statementStart;

		/// <summary>Splits on commas outside parentheses, brackets and braces.</summary>
		private static List<string> SplitArguments(string text)
		{
			var parts = new List<string>();
			var depth = 0;
			var start = 0;

			for (var i = 0; i < text.Length; i++)
			{
				var c = text[i];

				if (c is '"' or '\'')
				{
					i = CssPrepass.SkipString(text, i) - 1;

					continue;
				}

				if (c is '(' or '[' or '{')
				{
					depth++;
				}
				else if (c is ')' or ']' or '}')
				{
					depth--;
				}
				else if (c == ',' && depth <= 0)
				{
					parts.Add(text.Substring(start, i - start));
					start = i + 1;
				}
			}

			parts.Add(text.Substring(start));

			return parts;
		}

		/// <summary>Trims a value and drops the braces around one written as <c>{a, b}</c>.</summary>
		private static string Unwrap(string text)
		{
			text = text.Trim();

			if (text.Length >= 2 && text[0] == '{' && CssPrepass.MatchingBrace(text, 0) == text.Length - 1)
				text = text.Substring(1, text.Length - 2).Trim();

			return text;
		}

		private static int IndexOfTopLevel(string text, char target, int start)
		{
			var depth = 0;

			for (var i = start; i < text.Length; i++)
			{
				var c = text[i];

				if (c is '"' or '\'')
				{
					i = CssPrepass.SkipString(text, i) - 1;

					continue;
				}

				if (c is '(' or '[' or '{' or '<')
					depth++;
				else if (c is ')' or ']' or '}' or '>')
					depth--;
				else if (c == target && depth <= 0)
					return i;
			}

			return -1;
		}

		private static int MatchingParen(string text, int open)
		{
			var depth = 0;

			for (var i = open; i < text.Length; i++)
			{
				var c = text[i];

				if (c is '"' or '\'')
				{
					i = CssPrepass.SkipString(text, i) - 1;

					continue;
				}

				if (c == '(')
					depth++;
				else if (c == ')' && --depth == 0)
					return i;
			}

			return -1;
		}

		private static int IdentifierEnd(string text, int start)
		{
			var i = start;

			while (i < text.Length && IsIdentifierPart(text[i]))
				i++;

			return i;
		}

		private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';

		private static string Abbreviate(string text, int start, int end)
		{
			var statement = text.Substring(start, Math.Max(0, end - start));
			var cut = statement.IndexOfAny(new[] { '{', ';', '\n' });

			return (cut > 0 ? statement.Substring(0, cut) : statement).Trim();
		}
	}
}

using System;
using System.Collections.Generic;
using ExCSS;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Builds a <see cref="StyleSheet"/> from the CSS library's parse of one file. Editor only: sheets
	/// are compiled when they are imported, and nothing at runtime parses CSS.
	/// </summary>
	internal static class CssBuilder
	{
		internal static Stylesheet? Parse(string css, string sourceName, List<string> diagnostics)
		{
			var parser = new StylesheetParser(
				includeUnknownRules: true,
				includeUnknownDeclarations: true,
				tolerateInvalidSelectors: true,
				tolerateInvalidValues: true,
				tolerateInvalidConstraints: true,
				preserveComments: false,
				preserveDuplicateProperties: true,
				customPseudoClasses: UiStates.Names);

			try
			{
				return parser.Parse(CssPrepass.RemoveScopesInStyleRules(css, sourceName, diagnostics));
			}
			catch (Exception ex)
			{
				diagnostics.Add($"{sourceName}: {ex.Message}");

				return null;
			}
		}

		internal static StyleSheet Build(Stylesheet parsed, string sourceName, List<string> diagnostics)
		{
			var builder = new Builder(sourceName, diagnostics);

			// A depth-first pre-order walk is document order, which is what the cascade reads off the
			// rule array's index — and it has to cover the whole sheet rather than just its top-level
			// style rules, since a rule inside an `@media` sits in the cascade exactly where it was
			// written. The CSS library hoists declarations written after a nested block up into the
			// parent, so a rule's own declarations always precede everything it nests.
			foreach (var child in parsed.Children)
				builder.BuildNode(child, Position.TopLevel);

			return builder.ToSheet();
		}

		/// <summary>
		/// Reads a sheet's <c>@import</c>s, resolved to the assets they name.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Every sheet in the project is loaded exactly once, globally, so an import never pulls rules in
		/// a second time. What it does is order: an imported sheet cascades before the one importing it,
		/// whatever their paths. <c>layer(name)</c>, or an anonymous <c>layer</c>, also places the whole
		/// imported sheet in that cascade layer.
		/// </para>
		/// <para>
		/// The prelude is read from the text rather than from the CSS library's parse, which knows nothing
		/// of <c>layer()</c> and files it under media. A media query or <c>supports()</c> has nothing to
		/// act on when sheets are global, and is named rather than silently ignored.
		/// </para>
		/// </remarks>
		internal static CompiledStyleSheet.Import[] ReadImports(Stylesheet parsed, string sourceName, List<string> diagnostics)
		{
			List<CompiledStyleSheet.Import>? imports = null;
			var sawRule = false;
			var anonymous = 0;

			foreach (var child in parsed.Children)
			{
				if (child is not IImportRule import)
				{
					// @charset and the statement form of @layer may precede imports; anything else ends them.
					if (child is not IRule { Type: RuleType.Charset or RuleType.LayerStatement })
						sawRule = true;

					continue;
				}

				var text = import.StylesheetText?.Text ?? string.Empty;

				if (sawRule)
				{
					diagnostics.Add($"{sourceName}: '{text.Trim()}' comes after other rules and is ignored; @import must come first.");

					continue;
				}

				if (string.IsNullOrWhiteSpace(import.Href))
				{
					diagnostics.Add($"{sourceName}: '{text.Trim()}' does not name a sheet.");

					continue;
				}

				var path = ResolveImport(sourceName, import.Href.Trim());
				var layer = ReadImportLayer(text, import.Href, sourceName, diagnostics, ref anonymous);

				if (string.Equals(path, sourceName, StringComparison.Ordinal))
				{
					diagnostics.Add($"{sourceName}: imports itself.");

					continue;
				}

				imports ??= new List<CompiledStyleSheet.Import>();
				imports.Add(new CompiledStyleSheet.Import(path, layer));
			}

			return imports is null ? Array.Empty<CompiledStyleSheet.Import>() : imports.ToArray();
		}

		/// <summary>Resolves an import against the importing sheet's folder, the way a relative URL resolves.</summary>
		internal static string ResolveImport(string importer, string href)
		{
			href = href.Replace('\\', '/');

			if (href.StartsWith("Assets/", StringComparison.Ordinal) || href.StartsWith("Packages/", StringComparison.Ordinal))
				return Normalise(href);

			var slash = importer.LastIndexOf('/');
			var folder = slash < 0 ? string.Empty : importer.Substring(0, slash + 1);

			return Normalise(folder + href);
		}

		private static string Normalise(string path)
		{
			var segments = new List<string>();

			foreach (var segment in path.Split('/'))
			{
				if (segment.Length == 0 || segment == ".")
					continue;

				if (segment == ".." && segments.Count > 0 && segments[segments.Count - 1] != "..")
					segments.RemoveAt(segments.Count - 1);
				else
					segments.Add(segment);
			}

			return string.Join("/", segments);
		}

		private static string ReadImportLayer(string text, string href, string sourceName, List<string> diagnostics, ref int anonymous)
		{
			// Everything after the URL, up to the closing semicolon.
			var at = text.IndexOf(href, StringComparison.Ordinal);
			var rest = at < 0 ? string.Empty : text.Substring(at + href.Length);

			rest = StripComments(rest).Trim().TrimEnd(';').Trim();

			// What closes the url( or the string the href was read from.
			while (rest.Length > 0 && (rest[0] == '"' || rest[0] == '\'' || rest[0] == ')'))
				rest = rest.Substring(1).TrimStart();

			var layer = string.Empty;

			if (rest.StartsWith("layer", StringComparison.OrdinalIgnoreCase)
				&& (rest.Length == 5 || rest[5] == '(' || char.IsWhiteSpace(rest[5])))
			{
				rest = rest.Substring(5).TrimStart();

				if (rest.StartsWith("(", StringComparison.Ordinal))
				{
					var close = rest.IndexOf(')');

					if (close < 0)
					{
						diagnostics.Add($"{sourceName}: '{text.Trim()}' has an unclosed layer().");

						return string.Empty;
					}

					layer = rest.Substring(1, close - 1).Trim();
					rest = rest.Substring(close + 1).Trim();
				}

				if (layer.Length == 0)
					layer = CascadeLayers.AnonymousMarker + sourceName + "#import" + anonymous++;
			}

			if (rest.Length > 0)
			{
				diagnostics.Add(
					$"{sourceName}: '{text.Trim()}' has a condition ('{rest}'), which is ignored — every sheet is "
					+ "loaded once and applies everywhere, so an import cannot be made conditional. Put the "
					+ "condition on the imported sheet's rules instead.");
			}

			return layer;
		}

		/// <summary>Where a node sits: the conditions, scope and layer everything under it inherits.</summary>
		private readonly struct Position
		{
			internal static Position TopLevel => new(0, 0, 0, string.Empty, 0UL, 0);

			public readonly int Media;
			public readonly int Scope;
			public readonly int Layer;

			/// <summary>The full dotted name of <see cref="Layer"/>, so a nested layer can extend it.</summary>
			public readonly string LayerPath;

			/// <summary>Every pseudo-class the enclosing scopes' roots and limits depend on.</summary>
			public readonly ulong ScopeStateMask;

			/// <summary>The specificity a scope's <c>&amp;</c> carries: its root list's highest.</summary>
			public readonly int ScopeSpecificity;

			public Position(int media, int scope, int layer, string layerPath, ulong scopeStateMask, int scopeSpecificity)
			{
				Media = media;
				Scope = scope;
				Layer = layer;
				LayerPath = layerPath;
				ScopeStateMask = scopeStateMask;
				ScopeSpecificity = scopeSpecificity;
			}

			public Position WithMedia(int media) => new(media, Scope, Layer, LayerPath, ScopeStateMask, ScopeSpecificity);

			public Position WithLayer(int layer, string path) => new(Media, Scope, layer, path, ScopeStateMask, ScopeSpecificity);

			public Position WithScope(int scope, ulong stateMask, int specificity) => new(Media, scope, Layer, LayerPath, stateMask, specificity);
		}

		/// <summary>The tables one sheet is being built into.</summary>
		private sealed class Builder
		{
			private readonly string _sourceName;
			private readonly List<string> _diagnostics;
			private readonly List<SimpleSelector> _simples = new();
			private readonly List<CompoundSelector> _compounds = new();
			private readonly List<SelectorRecord> _selectors = new();
			private readonly List<RuleRecord> _rules = new();
			private readonly List<Declaration> _declarations = new();
			private readonly List<ScopeRecord> _scopes = new() { default };
			private readonly List<string> _layers = new();
			private readonly Dictionary<string, int> _layerIndices = new(StringComparer.Ordinal);
			private readonly List<KeyframesClip> _keyframes = new();
			private readonly List<FontFace> _fontFaces = new();
			private readonly List<string> _selectorTexts = new();
			private readonly SelectorParser _selectorParser;
			private readonly MediaQueryCompiler _media;
			private int _anonymousLayers;

			internal Builder(string sourceName, List<string> diagnostics)
			{
				_sourceName = sourceName;
				_diagnostics = diagnostics;
				_selectorParser = new SelectorParser(_simples, _compounds, diagnostics);
				_media = new MediaQueryCompiler(sourceName, diagnostics);
			}

			internal StyleSheet ToSheet()
			{
				return new StyleSheet(
					_sourceName,
					_simples.ToArray(),
					_compounds.ToArray(),
					_selectors.ToArray(),
					_rules.ToArray(),
					_declarations.ToArray(),
					_keyframes.Count == 0 ? KeyframesClip.s_none : _keyframes.ToArray(),
					_media.BuildQueries(),
					_media.BuildClauses(),
					_media.BuildFeatures(),
					_scopes.ToArray(),
					_layers.ToArray(),
					_fontFaces.ToArray());
			}

			/// <summary>
			/// Emits one node and everything under it.
			/// </summary>
			/// <remarks>
			/// The same method serves a sheet's children, a grouping rule's children and a rule's nested
			/// rules, because once the parser has resolved nesting they are all the same list of the same
			/// kinds of thing. One <c>@media</c> inside another is a logical AND, which the compiler
			/// records as a parent index rather than a flattened condition; layers and scopes nest the
			/// same way.
			/// </remarks>
			internal void BuildNode(IStylesheetNode node, in Position position)
			{
				switch (node)
				{
					case IStyleRule rule:
						BuildRule(rule, position);

						break;

					case IMediaRule block:
					{
						var query = _media.Compile(Prelude(block, "@media") ?? block.Media.MediaText, position.Media);

						foreach (var child in block.Rules)
							BuildNode(child, position.WithMedia(query));

						break;
					}

					case ILayerRule layer:
					{
						var name = string.IsNullOrWhiteSpace(layer.Name)
							? CascadeLayers.AnonymousMarker + "layer" + _anonymousLayers++
							: layer.Name.Trim();

						var path = Join(position.LayerPath, name);
						var index = DeclareLayer(path);

						foreach (var child in layer.Rules)
							BuildNode(child, position.WithLayer(index, path));

						break;
					}

					case IRule { Type: RuleType.LayerStatement } statement:
						foreach (var name in LayerStatementNames(statement))
							DeclareLayer(Join(position.LayerPath, name));

						break;

					case IRule { Type: RuleType.Unknown } unknown when IsAtRule(unknown, "@scope"):
						BuildScope(unknown.StylesheetText?.Text ?? string.Empty, position);

						break;

					// A clip is built once and filed by name, and a face is registered when the sheet loads.
					// Neither has anywhere to keep a condition, so a conditional one is named rather than
					// half-honoured.
					case IKeyframesRule keyframes when position.Media != 0:
						_diagnostics.Add(
							$"{_sourceName}: @keyframes '{keyframes.Name}' inside an @media block is ignored; "
							+ "move it to the top level.");

						break;

					case IKeyframesRule keyframes:
						BuildKeyframes(keyframes);

						break;

					case IFontFaceRule when position.Media != 0:
						_diagnostics.Add(
							$"{_sourceName}: @font-face inside an @media block is ignored; move it to the top level.");

						break;

					case IFontFaceRule fontFace:
						ReadFontFace(fontFace, _sourceName, _fontFaces, _diagnostics);

						break;
				}
			}

			private int DeclareLayer(string path)
			{
				// A dotted name implies every layer above it, declared first, as CSS does.
				var dot = path.LastIndexOf('.');

				if (dot > 0)
					DeclareLayer(path.Substring(0, dot));

				if (_layerIndices.TryGetValue(path, out var index))
					return index;

				_layers.Add(path);
				index = _layers.Count;
				_layerIndices[path] = index;

				return index;
			}

			private static string Join(string prefix, string name) => prefix.Length == 0 ? name : prefix + "." + name;

			private static IEnumerable<string> LayerStatementNames(IRule statement)
			{
				var text = StripComments(statement.StylesheetText?.Text ?? string.Empty).Trim();

				if (text.StartsWith("@layer", StringComparison.OrdinalIgnoreCase))
					text = text.Substring(6);

				foreach (var name in text.TrimEnd(';').Split(','))
				{
					var trimmed = name.Trim();

					if (trimmed.Length > 0)
						yield return trimmed;
				}
			}

			/// <summary>
			/// Emits an <c>@scope (&lt;root&gt;) to (&lt;limit&gt;) { … }</c> block.
			/// </summary>
			/// <remarks>
			/// <para>
			/// The CSS library does not know <c>@scope</c> and keeps it whole as an unknown rule, so the
			/// prelude is read here and the block is parsed again as a sheet of its own. That is also the
			/// right shape for it: a rule directly inside a scope is not nested in a parent rule, so it
			/// must not be prefixed with one.
			/// </para>
			/// <para>
			/// Root and limit selectors become ordinary entries in the selector table. A rule inside the
			/// scope that names neither <c>:scope</c> nor <c>&amp;</c> is made relative to the root, as if
			/// written <c>:where(:scope) …</c> — matched below the root, with no added specificity.
			/// Declarations written directly in the block style the root itself, as
			/// <c>:where(:scope) { … }</c>; see <see cref="CssPrepass.RelativizeScopeBody"/>.
			/// </para>
			/// </remarks>
			private void BuildScope(string text, in Position position)
			{
				if (!TrySplitScope(text, out var prelude, out var body))
				{
					_diagnostics.Add($"{_sourceName}: could not read '{Abbreviate(text)}'.");

					return;
				}

				if (!TrySplitScopePrelude(prelude, out var rootText, out var limitText))
				{
					_diagnostics.Add(
						$"{_sourceName}: '@scope {prelude.Trim()}' needs a root selector, as in '@scope (.card) {{ … }}' or "
						+ "'@scope (.card) to (.slot) { … }'. A scope with no root is rooted at whatever owns the "
						+ "stylesheet, and a sheet here is owned by nothing.");

					return;
				}

				var nested = position.Scope != 0;

				// A nested scope's root is found inside the outer scope, so `:scope` and `&` in it mean the
				// outer root; at the top level they mean the root of the whole tree.
				var rootContext = nested ? new ScopeContext(true, position.ScopeSpecificity) : default;

				var rootStart = _selectors.Count;
				var rootSpecificity = ParseSelectorList(rootText, rootContext, out var rootMask);
				var rootCount = _selectors.Count - rootStart;

				if (rootCount == 0)
				{
					_diagnostics.Add($"{_sourceName}: '@scope {prelude.Trim()}' has no usable root selector; the block is ignored.");

					return;
				}

				var limitStart = _selectors.Count;
				var limitMask = 0UL;

				if (limitText is not null)
					ParseSelectorList(limitText, new ScopeContext(true, rootSpecificity), out limitMask);

				var limitCount = _selectors.Count - limitStart;

				var index = _scopes.Count;
				_scopes.Add(new ScopeRecord(rootStart, rootCount, limitStart, limitCount, position.Scope));

				var parsed = Parse(CssPrepass.RelativizeScopeBody(body), _sourceName, _diagnostics);

				if (parsed is null)
					return;

				var inner = position.WithScope(index, position.ScopeStateMask | rootMask | limitMask, rootSpecificity);

				foreach (var child in parsed.Children)
					BuildNode(child, inner);
			}

			/// <summary>
			/// Adds every selector in a comma-separated list to the selector table, and reports the
			/// highest specificity among them and every pseudo-class they depend on.
			/// </summary>
			private int ParseSelectorList(string text, in ScopeContext context, out ulong stateMask)
			{
				var specificity = 0;
				stateMask = 0UL;

				_selectorTexts.Clear();
				SelectorDesugar.Expand(text, _sourceName, _selectorTexts, _diagnostics);

				foreach (var selector in _selectorTexts)
				{
					if (!_selectorParser.TryParse(selector, out var record, context))
						continue;

					_selectors.Add(record);
					stateMask |= record.SelfStateMask | record.AncestorStateMask;

					if (record.Specificity > specificity)
						specificity = record.Specificity;
				}

				return specificity;
			}

			/// <summary>
			/// Emits a rule and everything nested inside it.
			/// </summary>
			/// <remarks>
			/// A block holding nothing but nested rules — <c>.tile { &amp;--big { … } }</c> — emits no rule of
			/// its own, but its children still have to be walked, so the empty check guards the emit rather
			/// than the descent. That is also what makes an <c>@media</c> written inside a rule work: the
			/// parser leaves behind an implicit rule carrying the parent's selector, which is empty whenever
			/// the block held only further rules.
			/// </remarks>
			private void BuildRule(IStyleRule rule, in Position position)
			{
				var declarationStart = _declarations.Count;
				ReadDeclarations(rule.Style, _sourceName, _declarations, _diagnostics);

				if (_declarations.Count > declarationStart)
				{
					// Nesting is resolved to plain selectors before the parser sees them, and a selector
					// list shares one declaration block, so each selector becomes its own rule pointing at
					// the same range.
					//
					// One authored selector can expand into several, because a `:is(…)` holding a list has no
					// flat spelling. CSS scores such a list at its most specific argument, so a group is
					// collected first and every member stamped with the highest specificity in it. The two
					// lists are per rule and live only while a sheet is built.
					var parts = new List<string>();
					var group = new List<SelectorRecord>();
					var context = position.Scope != 0 ? new ScopeContext(true, position.ScopeSpecificity) : default;

					SelectorDesugar.SplitList(rule.SelectorText, parts);

					foreach (var part in parts)
					{
						_selectorTexts.Clear();
						group.Clear();

						SelectorDesugar.ExpandPart(part, rule.SelectorText, _sourceName, _selectorTexts, _diagnostics);

						var specificity = 0;

						foreach (var authored in _selectorTexts)
						{
							var text = authored;
							var relative = position.Scope != 0 && !SelectorParser.ReferencesScope(text);

							// The root is required but adds nothing to specificity.
							if (relative)
								text = ":where(:scope) " + text;

							if (!_selectorParser.TryParse(text, out var record, context)) continue;

							group.Add(record);

							// Packed as (classes << 8) | types, so comparing the ints is the lexicographic
							// compare CSS orders specificity by, and the maximum is the most specific member.
							// A per-column maximum would invent a score no member of the group actually had.
							if (record.Specificity > specificity) specificity = record.Specificity;
						}

						foreach (var record in group)
						{
							// A scoped rule also depends on whatever states its scope's root and limit read,
							// so the per-frame check has to run for it whenever they could move.
							_selectors.Add(new SelectorRecord(
								record.CompoundStart,
								record.CompoundCount,
								specificity,
								record.SelfStateMask,
								record.AncestorStateMask | position.ScopeStateMask));

							_rules.Add(new RuleRecord(
								_selectors.Count - 1,
								declarationStart,
								_declarations.Count - declarationStart,
								position.Media,
								position.Scope,
								position.Layer));
						}
					}
				}

				foreach (var nested in rule.NestedRules)
					BuildNode(nested, position);
			}

			/// <summary>
			/// Reads one <c>@keyframes</c> block into a clip.
			/// </summary>
			/// <remarks>
			/// <para>
			/// The CSS library normalises <c>from</c>/<c>to</c> to <c>0%</c>/<c>100%</c> and keeps a
			/// multi-stop selector (<c>0%, 100% { … }</c>) as one rule with several stops, so both fall
			/// out of walking <see cref="IKeyframeRule.Key"/>.
			/// </para>
			/// <para>
			/// A property with no channel behind it is named in a diagnostic rather than dropped in
			/// silence. That is the one place this differs from <c>transition</c>, which still snaps such
			/// a property without comment — inside a keyframe block there is no sensible snapped value to
			/// fall back to, since the block describes nothing but the motion.
			/// </para>
			/// </remarks>
			private void BuildKeyframes(IKeyframesRule keyframes)
			{
				var name = keyframes.Name?.Trim();

				if (string.IsNullOrEmpty(name))
				{
					_diagnostics.Add($"{_sourceName}: @keyframes needs a name.");

					return;
				}

				var builder = new KeyframesBuilder();
				var stopDeclarations = new List<Declaration>();

				foreach (var frame in keyframes.Rules)
				{
					if (frame is not IKeyframeRule stop)
						continue;

					stopDeclarations.Clear();
					ReadDeclarations(stop.Style, _sourceName, stopDeclarations, _diagnostics);

					// A stop may set its own timing function, which in CSS shapes the segment that
					// begins there rather than the animation as a whole.
					var easing = Easing.InOutQuad;
					var hasEasing = false;

					for (var i = 0; i < stopDeclarations.Count; i++)
					{
						if (stopDeclarations[i].Id != PropId.AnimationTimingFunction)
							continue;

						easing = (Easing)stopDeclarations[i].Value.AsKeyword();
						hasEasing = true;
					}

					foreach (var offset in Offsets(stop, name!, _sourceName, _diagnostics))
					{
						for (var i = 0; i < stopDeclarations.Count; i++)
						{
							var declaration = stopDeclarations[i];

							// The animation longhands describe the run, not the node, and CSS ignores
							// them inside a block. The timing function is the documented exception,
							// and was read off above.
							if (declaration.IsCustom || IsAnimationProperty(declaration.Id))
								continue;

							// A keyframe block is built once, here, so a calc still waiting on a custom
							// property has no scope to resolve against and would interpolate towards
							// nothing. Named rather than dropped, since the value looks fine.
							if (declaration.Value.Reference is CalcExpr)
							{
								_diagnostics.Add(
									$"{_sourceName}: @keyframes {name} cannot use a calc() that reads a custom property.");

								continue;
							}

							if (declaration.Value.Reference is PendingTransform)
							{
								_diagnostics.Add(
									$"{_sourceName}: @keyframes {name} cannot use a transform that reads a custom property.");

								// One diagnostic for the shorthand, not one per channel it expanded into.
								i += PendingTransform.Longhands.Length - 1;

								continue;
							}

							if (!MotionChannels.TryChannelFor(declaration.Id, out var channel))
							{
								_diagnostics.Add(
									$"{_sourceName}: @keyframes {name} cannot animate '{declaration.Id}'; it has no channel to interpolate on.");

								continue;
							}

							builder.Add(channel, offset, declaration.Value, easing, hasEasing);
						}
					}
				}

				if (!builder.IsEmpty)
					_keyframes.Add(builder.Build(ClassTable.Intern(name!)));
			}
		}

		private static bool IsAtRule(IRule rule, string keyword)
		{
			var text = rule.StylesheetText?.Text;

			return text is not null
				&& text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
				&& (text.Length == keyword.Length || !IsIdentifierPart(text[keyword.Length]));
		}

		private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';

		/// <summary>Splits <c>@scope … { body }</c> into its prelude and the text between the braces.</summary>
		private static bool TrySplitScope(string text, out string prelude, out string body)
		{
			prelude = string.Empty;
			body = string.Empty;

			var open = CssPrepass.IndexOfBlock(text, 0);

			if (open < 0)
				return false;

			var close = text.LastIndexOf('}');

			if (close <= open)
				return false;

			prelude = StripComments(text.Substring(6, open - 6));
			body = text.Substring(open + 1, close - open - 1);

			return true;
		}

		/// <summary>Reads <c>(root) to (limit)</c>, either half of which CSS lets you leave out.</summary>
		private static bool TrySplitScopePrelude(string prelude, out string root, out string? limit)
		{
			root = string.Empty;
			limit = null;

			var text = prelude.Trim();

			if (!text.StartsWith("(", StringComparison.Ordinal))
				return false;

			var close = MatchingParen(text, 0);

			if (close < 0)
				return false;

			root = text.Substring(1, close - 1).Trim();

			var rest = text.Substring(close + 1).Trim();

			if (rest.Length == 0)
				return root.Length > 0;

			if (!rest.StartsWith("to", StringComparison.OrdinalIgnoreCase))
				return false;

			rest = rest.Substring(2).Trim();

			if (!rest.StartsWith("(", StringComparison.Ordinal))
				return false;

			var limitClose = MatchingParen(rest, 0);

			if (limitClose != rest.Length - 1)
				return false;

			limit = rest.Substring(1, limitClose - 1).Trim();

			return root.Length > 0;
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

		private static string Abbreviate(string text)
		{
			var line = text.Trim();
			var end = line.IndexOfAny(new[] { '{', '\n' });

			return end > 0 ? line.Substring(0, end).Trim() : line;
		}

		/// <summary>
		/// The condition as it was written. The media compiler reads it itself rather than taking the
		/// CSS library's parse, which replaces anything unreadable with <c>not all</c> — and so that a
		/// query in a sheet means exactly what the same query means passed to <c>UseMedia</c>.
		/// </summary>
		private static string? Prelude(IStylesheetNode block, string keyword)
		{
			var text = block.StylesheetText?.Text;

			if (text is null)
				return null;

			var brace = CssPrepass.IndexOfBlock(text, 0);
			var prelude = brace >= 0 ? text.Substring(0, brace) : text;

			// Drop the at-keyword; what is left is the condition.
			var at = prelude.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);

			return StripComments(at >= 0 ? prelude.Substring(at + keyword.Length) : prelude).Trim();
		}

		internal static string StripComments(string text)
		{
			var open = text.IndexOf("/*", StringComparison.Ordinal);

			if (open < 0)
				return text;

			var builder = new System.Text.StringBuilder(text.Length);
			var copied = 0;

			while (open >= 0)
			{
				builder.Append(text, copied, open - copied).Append(' ');

				var close = text.IndexOf("*/", open + 2, StringComparison.Ordinal);
				copied = close < 0 ? text.Length : close + 2;
				open = copied < text.Length ? text.IndexOf("/*", copied, StringComparison.Ordinal) : -1;
			}

			return builder.Append(text, copied, text.Length - copied).ToString();
		}

		private static bool IsAnimationProperty(PropId id) =>
			id >= PropId.AnimationName && id <= PropId.AnimationPlayState;

		private static IEnumerable<float> Offsets(IKeyframeRule stop, string name, string sourceName, List<string> diagnostics)
		{
			var any = false;

			foreach (var percent in stop.Key.Stops)
			{
				any = true;

				yield return Mathf.Clamp01(percent.NormalizedValue);
			}

			if (!any)
				diagnostics.Add($"{sourceName}: @keyframes {name} has a stop with no offset ('{stop.KeyText}').");
		}

		private static void ReadDeclarations(StyleDeclaration rule, string sourceName, List<Declaration> into, List<string> diagnostics)
		{
			foreach (var property in rule)
			{
				var name = property.Name;
				var value = property.Value;

				if (string.IsNullOrEmpty(value)) continue;

				// Custom properties cascade and inherit like anything else, but are addressed by
				// name — this is what lets a per-instance accent colour live in CSS at all.
				if (name.StartsWith("--", StringComparison.Ordinal))
				{
					if (TryParseCustomValue(value, out var custom))
					{
						into.Add(new Declaration(ClassTable.Intern(name), custom));
					}
					else
					{
						diagnostics.Add($"{sourceName}: cannot parse custom property '{name}: {value}'.");
					}

					continue;
				}

				// `initial` and friends mean "leave this unset", which this model expresses by
				// simply not emitting a declaration. The CSS library inserts them when expanding a
				// shorthand that omits a longhand, so they are routine rather than a mistake.
				if (IsUnsetKeyword(value)) continue;

				if (Shorthand.TryExpand(name, value, into, diagnostics, sourceName)) continue;

				if (!PropertyRegistry.TryGet(name, out var info))
				{
					// Vendor-prefixed properties are how the framework's own extensions are spelled,
					// so an unknown one is worth naming rather than swallowing.
					diagnostics.Add($"{sourceName}: unknown property '{name}'.");

					continue;
				}

				if (info._syntax == ValueSyntax.FontReference
					&& !value.TrimStart().StartsWith("var(", StringComparison.OrdinalIgnoreCase))
				{
					into.Add(new Declaration(info._id, ResolveFont(value)));

					continue;
				}

				if (!ValueParser.TryParse(value, info, out var parsed))
				{
					diagnostics.Add($"{sourceName}: cannot parse '{name}: {value}'.");

					continue;
				}

				into.Add(new Declaration(info._id, parsed));
			}
		}

		private static bool TryParseCustomValue(string value, out StyleValue parsed)
		{
			var text = value.Trim();

			// A custom property has no declared syntax, so its calc is whatever the arithmetic works
			// out to. One reading another property resolves where this one's own scope is built.
			if (Calc.IsCalc(text))
				return Calc.TryParse(text, CalcOutput.Untyped, allowVars: true, out parsed);

			// One custom property may alias another. The reference is carried through and resolved
			// where the scope is built, so `--accent: var(--banana-400)` behaves as CSS says.
			if (text.StartsWith("var(", StringComparison.OrdinalIgnoreCase))
			{
				var name = ValueParser.Inner(text, "var");

				if (name is not null)
				{
					var comma = name.IndexOf(',');
					if (comma >= 0) name = name.Substring(0, comma);

					parsed = StyleValue.OfVar(ClassTable.Intern(name.Trim()));

					return true;
				}
			}

			// A value of several top-level tokens — a shadow, a transition list — is only meaningful to the
			// property that receives it, and read as one scalar it would keep its first token and silently
			// lose the rest.
			if (ValueParser.SplitTop(text, ' ').Count > 1)
			{
				parsed = StyleValue.OfReference(text);

				return true;
			}

			if (ValueParser.TryParseColor(text, out parsed)) return true;
			if (ValueParser.TryParseLength(text, out parsed)) return true;

			// A bare number — `--columns: 8`. CSS lets only a unitless zero be a length, so the parse
			// above refuses it, but arithmetic counts with it and every `Number` property reads one.
			// Inline custom properties have always carried numbers; this is the stylesheet's half.
			if (ValueParser.TryParseBareNumber(text, out parsed)) return true;

			parsed = StyleValue.OfReference(text);

			return true;
		}

		/// <summary>
		/// Keeps <c>font-family</c> as the name it was written as.
		/// </summary>
		/// <remarks>
		/// The face used to be resolved here, which is what made <c>font-weight</c> unimplementable:
		/// weight is a separate declaration that cascades on its own, so nothing at parse time knows
		/// which weight this family will end up paired with. Resolving in the text host instead costs
		/// a dictionary lookup per restyle and makes the pair mean what CSS says it means. It also
		/// drops the ordering constraint that every `@font-face` had to be registered before any
		/// sheet that used it was built.
		/// </remarks>
		private static StyleValue ResolveFont(string value)
		{
			return StyleValue.OfReference(ValueParser.Unquote(value));
		}

		/// <summary>
		/// Records an <c>@font-face</c>. The font asset is looked up when the sheet is loaded, which is
		/// when there is somewhere to register it.
		/// </summary>
		private static void ReadFontFace(IFontFaceRule rule, string sourceName, List<FontFace> into, List<string> diagnostics)
		{
			var family = ValueParser.Unquote(rule.Family ?? string.Empty);
			var source = rule.Source ?? string.Empty;
			var path = ValueParser.Inner(source, "resource") ?? ValueParser.Inner(source, "url");

			if (string.IsNullOrEmpty(family) || path is null)
			{
				diagnostics.Add($"{sourceName}: @font-face needs a font-family and a resource(\"...\") source.");

				return;
			}

			into.Add(new FontFace(family, ValueParser.Unquote(path), FontFaceWeight(rule, sourceName, diagnostics)));
		}

		/// <summary>
		/// Reads the <c>font-weight</c> descriptor a face was filed under, defaulting to 400.
		/// </summary>
		private static int FontFaceWeight(IFontFaceRule rule, string sourceName, List<string> diagnostics)
		{
			var weight = rule.Weight;

			if (string.IsNullOrEmpty(weight))
				return UiFonts.NormalWeight;

			var resolved = Keywords.Resolve(KeywordSet.FontWeight, weight.Trim());

			if (resolved >= 0)
				return resolved;

			diagnostics.Add($"{sourceName}: @font-face has an unrecognised font-weight '{weight}'.");

			return UiFonts.NormalWeight;
		}

		private static bool IsUnsetKeyword(string value)
		{
			var text = value.Trim();

			return text.Equals("initial", StringComparison.OrdinalIgnoreCase)
				|| text.Equals("unset", StringComparison.OrdinalIgnoreCase)
				|| text.Equals("inherit", StringComparison.OrdinalIgnoreCase);
		}
	}
}

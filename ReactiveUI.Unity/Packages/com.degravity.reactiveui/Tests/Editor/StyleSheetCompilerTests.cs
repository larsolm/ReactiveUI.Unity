using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ReactiveUI.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ReactiveUI.Tests
{
	/// <summary>A stand-in host: just enough tree for the matcher to walk.</summary>
	internal sealed class Node : IMatchTarget
	{
		private readonly Node _parent;
		private ClassSet _classes;

		public Node(Node parent, params string[] classes)
		{
			_parent = parent;

			foreach (var name in classes)
				_classes = _classes | ClassName.Intern(name);
		}

		public ulong State { get; set; }

		/// <summary>The host type name the node answers to, or null for none.</summary>
		public string Type { get; set; }

		public IMatchTarget MatchParent => _parent;
		public IMatchTarget MatchPreviousSibling => null;
		public ClassSet MatchClasses => _classes;
		public ulong MatchState => State;
		public bool MatchesType(int typeId) => Type is not null && ClassTable.Intern(Type) == typeId;
	}

	public sealed class StyleSheetCompilerTests
	{
		private readonly List<Object> _created = new();

		[TearDown]
		public void TearDown()
		{
			foreach (var created in _created)
				Object.DestroyImmediate(created);

			_created.Clear();
		}

		/// <summary>Gives the editor back the fonts the test libraries cleared.</summary>
		[OneTimeTearDown]
		public void RestoreCatalog() => CssCatalog.Reload(changed: null);

		private CompiledStyleSheet CompileAsset(
			string path,
			string css,
			List<string> diagnostics = null,
			System.Func<string, string> readSheet = null)
		{
			var result = CssCompiler.Compile(css, path, readSheet);

			Assert.IsNotNull(result.Sheet, string.Join("\n", result.Diagnostics));
			diagnostics?.AddRange(result.Diagnostics);

			var asset = ScriptableObject.CreateInstance<CompiledStyleSheet>();
			result.WriteTo(asset, path);
			_created.Add(asset);

			return asset;
		}

		/// <summary>Compiles, orders and links sheets the way a player does, reading each on demand.</summary>
		private StyleSheetLibrary Library(params (string Path, string Css)[] files)
		{
			var sheets = Sheets(files);
			var sources = CssAssets.Order(
				files.Select(file => CompileAsset(file.Path, file.Css, readSheet: path => sheets.TryGetValue(path, out var css) ? css : null)).ToList(),
				report: false);
			var library = new StyleSheetLibrary();

			library.Load(sources);

			return library;
		}

		/// <summary>Compiles, orders and links sheets, and returns an engine over them.</summary>
		private StyleEngine Load(params (string Path, string Css)[] files) => Engine(Library(files));

		private static StyleEngine Engine(IStyleSheetSource source)
		{
			var engine = new StyleEngine(new StyleContext(16f));
			engine.SetSheets(source);

			return engine;
		}

		private StyleEngine Load(string css) => Load(("Assets/Test.css", css));

		/// <summary>The opacity a node resolves to, which every test uses to tell which rule won.</summary>
		private static float Opacity(StyleEngine engine, Node node)
		{
			var ruleSet = engine.Match(node);
			var mask = engine.EvaluateConditions(ruleSet, node);

			return engine.Resolve(ruleSet, mask, 0, 0).Number(PropId.Opacity, -1f);
		}

		#region Serialization

		private const string Everything = @"
@font-face { font-family: 'Body'; src: resource('Fonts/Body'); font-weight: 700; }
@layer base, theme;
:root { --accent: #ff8800; --pad: 4px; --fast: 120ms; --shade: rgba(0, 0, 0, 0.5); }
.card {
	padding: calc(var(--pad) * 2);
	color: rgba(var(--accent), 0.5);
	background-color: var(--accent);
	background-image: linear-gradient(90deg, var(--accent), #000 80%);
	box-shadow: 0 2px 4px var(--shade), 0 0 1px #fff;
	-rui-checker: 8px rgba(255, 255, 255, 0.1);
	transition: opacity var(--fast);
	animation-name: pulse;
	animation-duration: 1s;
	font-family: 'Body';
	&:hover { opacity: 0.8; }
	.title { font-size: 1.5rem; }
}
@media (max-width: 40rem) and (orientation: portrait), (input-device: gamepad) { .card { width: 100%; } }
@keyframes pulse { from { opacity: 0; } 50% { opacity: 1; animation-timing-function: linear; } to { opacity: 0.5; } }
@layer theme { .card { opacity: 0.9; } }
@scope (.card) to (.slot) { :scope > .label { opacity: 0.1; } & .icon { opacity: 0.2; } .label { opacity: 0.3; } }
@mixin --raised(--depth: 2px) { box-shadow: 0 var(--depth) 4px var(--shade); &:hover { opacity: 0.95; } }
.panel { @apply --raised(6px); }
";

		[Test]
		public void RoundTrip_ReproducesTheSameBytes()
		{
			var result = CssCompiler.Compile(Everything, "Assets/Everything.css");

			Assert.IsNotNull(result.Sheet, string.Join("\n", result.Diagnostics));
			CollectionAssert.IsEmpty(result.Diagnostics);

			var reread = StyleSheetSerializer.Read(result.Data, "Assets/Everything.css");

			Assert.AreEqual(result.Data, StyleSheetSerializer.Write(reread));
		}

		[Test]
		public void RoundTrip_KeepsTheShapeOfTheSheet()
		{
			var result = CssCompiler.Compile(Everything, "Assets/Everything.css");
			var built = result.Sheet;
			var reread = StyleSheetSerializer.Read(result.Data, "Assets/Everything.css");

			Assert.AreEqual(built.Rules.Length, reread.Rules.Length);
			Assert.AreEqual(built.Declarations.Length, reread.Declarations.Length);
			Assert.AreEqual(built.Scopes.Length, reread.Scopes.Length);
			Assert.AreEqual(2, reread.Scopes.Length);
			CollectionAssert.AreEqual(new[] { "base", "theme" }, reread.LayerNames);
			Assert.AreEqual(1, reread.Keyframes.Length);
			Assert.AreEqual(1, reread.FontFaces.Length);
			Assert.AreEqual("Fonts/Body", reread.FontFaces[0].ResourcePath);
			Assert.AreEqual(700, reread.FontFaces[0].Weight);

			for (var i = 0; i < built.Declarations.Length; i++)
			{
				var before = built.Declarations[i];
				var after = reread.Declarations[i];

				Assert.AreEqual(before.Id, after.Id);
				Assert.AreEqual(before.CustomNameId, after.CustomNameId);
				Assert.AreEqual(before.Value.Tag, after.Value.Tag, $"declaration {i} ({before.Id})");
				Assert.AreEqual(before.Value.Reference?.GetType(), after.Value.Reference?.GetType());

				if (before.Value.Reference is not PendingShorthand)
					Assert.AreEqual(before.Value.Reference, after.Value.Reference, $"declaration {i} ({before.Id})");
			}

			Assert.AreEqual(ClassName.Intern("pulse")._id, reread.Keyframes[0].NameId);
		}

		[Test]
		public void RoundTrip_EveryProjectSheet()
		{
			foreach (var sheet in CssAssets.LoadAll())
			{
				if (!sheet.HasData)
					continue;

				var reread = sheet.Load();
				var data = StyleSheetSerializer.Write(reread);

				Assert.AreEqual(data, StyleSheetSerializer.Write(StyleSheetSerializer.Read(data, sheet.SourcePath)), sheet.SourcePath);
			}
		}

		/// <summary>
		/// Compiles and round-trips every <c>.css</c> under the folder named by
		/// <c>REACTIVEUI_CSS_CORPUS</c> — another project's sheets, say — and logs what the compiler said.
		/// </summary>
		[Test]
		public void RoundTrip_Corpus()
		{
			var folder = System.Environment.GetEnvironmentVariable("REACTIVEUI_CSS_CORPUS");

			if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
				Assert.Ignore("Set REACTIVEUI_CSS_CORPUS to a folder of stylesheets to run this.");

			var files = System.IO.Directory.GetFiles(folder, "*.css", System.IO.SearchOption.AllDirectories);

			foreach (var file in files)
			{
				var path = file.Replace('\\', '/');
				var result = CssCompiler.Compile(System.IO.File.ReadAllText(file), path);

				Assert.IsNotNull(result.Sheet, path);

				var reread = StyleSheetSerializer.Read(result.Data, path);

				Assert.AreEqual(result.Data, StyleSheetSerializer.Write(reread), path);
				Assert.AreEqual(result.Sheet.Rules.Length, reread.Rules.Length, path);

				Debug.Log($"[corpus] {path}: {reread.Rules.Length} rules, {reread.Declarations.Length} declarations, {result.Data.Length} bytes");

				foreach (var diagnostic in result.Diagnostics)
					Debug.Log($"[corpus-diagnostic] {diagnostic}");
			}

			Assert.That(files.Length, Is.GreaterThan(0));
		}

		#endregion

		#region Media queries

		[TestCase("(max-width: 40rem)")]
		[TestCase("screen and (min-width: 600px) and (orientation: landscape)")]
		[TestCase("(width >= 40rem)")]
		[TestCase("(40rem <= width)")]
		[TestCase("(20rem < width <= 60rem)")]
		[TestCase("not print, (aspect-ratio: 16/9)")]
		[TestCase("only screen and (input-device: gamepad)")]
		public void Media_ReadsWithoutComplaint(string query)
		{
			var diagnostics = new List<string>();

			MediaQueryCompiler.CompileCondition(query, diagnostics);

			CollectionAssert.IsEmpty(diagnostics, query);
		}

		[TestCase("(width > 10rem) or (height > 10rem)")]
		[TestCase("(hover)")]
		[TestCase("(colour: red)")]
		[TestCase("screen and")]
		[TestCase("(width: 10%)")]
		public void Media_NamesWhatItCannotRead(string query)
		{
			var diagnostics = new List<string>();

			MediaQueryCompiler.CompileCondition(query, diagnostics);

			CollectionAssert.IsNotEmpty(diagnostics, query);
		}

		#endregion

		#region @scope

		[Test]
		public void Scope_ConfinesRulesBetweenRootAndLimit()
		{
			var engine = Load("@scope (.card) to (.slot) { .label { opacity: 0.5; } .slot { opacity: 0.4; } }");

			var card = new Node(null, "card");
			var slot = new Node(card, "slot");

			Assert.AreEqual(0.5f, Opacity(engine, new Node(card, "label")));
			Assert.AreEqual(0.5f, Opacity(engine, new Node(new Node(card, "row"), "label")));
			Assert.AreEqual(-1f, Opacity(engine, new Node(slot, "label")), "below the limit");
			Assert.AreEqual(-1f, Opacity(engine, slot), "the limit itself is out of scope");
			Assert.AreEqual(-1f, Opacity(engine, new Node(null, "label")), "outside any root");
		}

		[Test]
		public void Scope_NearerRootWinsOverDocumentOrder()
		{
			var engine = Load(
				"@scope (.inner) { .label { opacity: 0.2; } }"
				+ "@scope (.outer) { .label { opacity: 0.1; } }");

			var inner = new Node(new Node(null, "outer"), "inner");

			Assert.AreEqual(0.2f, Opacity(engine, new Node(inner, "label")));
		}

		[Test]
		public void Scope_ScopedBeatsUnscopedAtEqualSpecificity()
		{
			var engine = Load("@scope (.card) { .label { opacity: 0.3; } } .label { opacity: 0.4; } .label.big { opacity: 0.6; }");
			var card = new Node(null, "card");

			Assert.AreEqual(0.3f, Opacity(engine, new Node(card, "label")));
			Assert.AreEqual(0.6f, Opacity(engine, new Node(card, "label", "big")), "specificity still comes first");
			Assert.AreEqual(0.4f, Opacity(engine, new Node(null, "label")));
		}

		[Test]
		public void Scope_ScopeAndAmpersandCountTowardSpecificity()
		{
			// `:scope > .label` is (0,2,0) against the implicit `.label`'s (0,1,0); `&` carries the root's
			// own (0,2,0), so `& .icon` is (0,3,0) and beats a later `.card .icon` at (0,2,0).
			var engine = Load(
				"@scope (.card.wide) { :scope > .label { opacity: 0.1; } .label { opacity: 0.2; } & .icon { opacity: 0.7; } }"
				+ ".card .icon { opacity: 0.8; }");

			var card = new Node(null, "card", "wide");

			Assert.AreEqual(0.1f, Opacity(engine, new Node(card, "label")));
			Assert.AreEqual(0.2f, Opacity(engine, new Node(new Node(card, "row"), "label")), ":scope > needs a direct child");
			Assert.AreEqual(0.7f, Opacity(engine, new Node(card, "icon")));
		}

		[Test]
		public void Scope_NestedRulesResolveInsideTheScope()
		{
			var engine = Load("@scope (.card) { .button { opacity: 0.5; &:hover { opacity: 0.9; } } }");
			var button = new Node(new Node(null, "card"), "button");

			Assert.AreEqual(0.5f, Opacity(engine, button));

			button.State = UiStates.s_hover.Mask;

			Assert.AreEqual(0.9f, Opacity(engine, button));
		}

		[Test]
		public void Scope_InsideAStyleRuleIsRemovedAndNamed()
		{
			var diagnostics = new List<string>();
			var asset = CompileAsset("Assets/Nested.css", ".outer { opacity: 0.1; @scope (.z) { .w { opacity: 0.2; } } }", diagnostics);
			var library = new StyleSheetLibrary();
			library.Load(new[] { asset });
			var engine = Engine(library);

			Assert.That(diagnostics, Has.Some.Contains("@scope inside a style rule"));
			Assert.AreEqual(-1f, Opacity(engine, new Node(null, "w")), "must not leak to every .w");
			Assert.AreEqual(0.1f, Opacity(engine, new Node(null, "outer")));
		}

		[Test]
		public void Scope_ClassesInScopesAreNotUnscopedClaims()
		{
			var asset = CompileAsset("Assets/Claims.css", "@scope (.card) { .label { opacity: 1; } } .title { opacity: 1; }");

			CollectionAssert.AreEqual(new[] { "title" }, asset.UnscopedClasses);
			CollectionAssert.AreEqual(new[] { "card", "label", "title" }, asset.Classes);
		}

		[Test]
		public void Scope_BareDeclarationsStyleTheRoot()
		{
			var engine = Load("@scope (.card) { opacity: 0.5; .label { opacity: 0.3; } }");
			var card = new Node(null, "card");

			Assert.AreEqual(0.5f, Opacity(engine, card));
			Assert.AreEqual(0.3f, Opacity(engine, new Node(card, "label")));
			Assert.AreEqual(-1f, Opacity(engine, new Node(card, "row")), "only the root itself");
		}

		[Test]
		public void Scope_BareDeclarationsAddNoSpecificity()
		{
			// Bare declarations are `:where(:scope)` at (0,0,0) and lose to `.card` at (0,1,0) whatever the
			// order; `:scope` ties `.card`, and the scoped rule wins the tie.
			var bare = Load(".card { opacity: 0.2; } @scope (.card) { opacity: 0.1; }");
			var pseudo = Load(".card { opacity: 0.2; } @scope (.card) { :scope { opacity: 0.3; } }");

			Assert.AreEqual(0.2f, Opacity(bare, new Node(null, "card")));
			Assert.AreEqual(0.3f, Opacity(pseudo, new Node(null, "card")));
		}

		[Test]
		public void Scope_LeadingCombinatorIsRelativeToTheRoot()
		{
			var engine = Load("@scope (.card) { > .label { opacity: 0.5; > .icon { opacity: 0.6; } } }");
			var card = new Node(null, "card");
			var label = new Node(card, "label");

			Assert.AreEqual(0.5f, Opacity(engine, label));
			Assert.AreEqual(-1f, Opacity(engine, new Node(new Node(card, "row"), "label")), "needs a direct child");
			Assert.AreEqual(0.6f, Opacity(engine, new Node(label, "icon")));
			Assert.AreEqual(-1f, Opacity(engine, new Node(new Node(label, "row"), "icon")), "nested `>` holds too");
		}

		[Test]
		public void Scope_BareDeclarationsInsideGroupRules()
		{
			var engine = Load("@scope (.card) { @layer base { opacity: 0.5; > .label { opacity: 0.4; } } }");
			var card = new Node(null, "card");

			Assert.AreEqual(0.5f, Opacity(engine, card));
			Assert.AreEqual(0.4f, Opacity(engine, new Node(card, "label")));
		}

		[Test]
		public void Scope_BareDeclarationsKeepTheRulesAfterThem()
		{
			var diagnostics = new List<string>();
			var asset = CompileAsset(
				"Assets/Screen.css",
				"@scope (.screen) { flex-grow: 1; > .prompt { margin-top: 1px; > .text { opacity: 1; } } align-items: center }",
				diagnostics);

			CollectionAssert.IsEmpty(diagnostics);
			CollectionAssert.AreEqual(new[] { "prompt", "screen", "text" }, asset.Classes);
		}

		#endregion

		#region @mixin and @apply

		private static Dictionary<string, string> Sheets(params (string Path, string Css)[] files) =>
			files.ToDictionary(file => file.Path, file => file.Css);

		private static CssCompiler.Result CompileWithImports(string path, string css, Dictionary<string, string> sheets) =>
			CssCompiler.Compile(css, path, other => sheets.TryGetValue(other, out var text) ? text : null);

		[Test]
		public void Mixin_AppliesItsDeclarations()
		{
			var engine = Load("@mixin --faded { opacity: 0.3; } .x { @apply --faded; }");

			Assert.AreEqual(0.3f, Opacity(engine, new Node(null, "x")));
		}

		[Test]
		public void Mixin_LaterDeclarationsWin()
		{
			var engine = Load("@mixin --faded { opacity: 0.3; } .x { @apply --faded; opacity: 0.6; } .y { opacity: 0.6; @apply --faded; }");

			Assert.AreEqual(0.6f, Opacity(engine, new Node(null, "x")));
			Assert.AreEqual(0.3f, Opacity(engine, new Node(null, "y")));
		}

		[TestCase("@apply --fade(0.4);", 0.4f)]
		[TestCase("@apply --fade;", 0.7f)]
		[TestCase("@apply --fade();", 0.7f)]
		[TestCase("@apply --fade({0.25});", 0.25f)]
		public void Mixin_ParameterTakesArgumentOrDefault(string apply, float expected)
		{
			var engine = Load($"@mixin --fade(--amount <number>: 0.7) {{ opacity: var(--amount); }} .x {{ {apply} }}");

			Assert.AreEqual(expected, Opacity(engine, new Node(null, "x")));
		}

		[Test]
		public void Mixin_VarFallbackCoversAParameterWithNoValue()
		{
			var engine = Load("@mixin --fade(--amount) { opacity: var(--amount, 0.15); } .x { @apply --fade; }");

			Assert.AreEqual(0.15f, Opacity(engine, new Node(null, "x")));
		}

		[Test]
		public void Mixin_ArgumentWithCommasIsWrappedInBraces()
		{
			var result = CssCompiler.Compile(
				"@mixin --shadow(--layers, --amount) { box-shadow: var(--layers); opacity: var(--amount); }"
				+ ".x { @apply --shadow({0 1px 2px #000, 0 0 1px #fff}, 0.5); }",
				"Assets/Shadow.css");

			CollectionAssert.IsEmpty(result.Diagnostics);
			Assert.AreEqual(1, result.Sheet.Rules.Length);
			Assert.That(result.Sheet.Declarations.Select(d => d.Id), Has.Member(PropId.Opacity));
		}

		[Test]
		public void Mixin_ContentsTakesTheBlockOrItsFallback()
		{
			const string mixin = "@mixin --hover { &:hover { @contents { opacity: 0.1; } } }";

			var passed = Load(mixin + " .x { @apply --hover { opacity: 0.8; } }");
			var fallback = Load(mixin + " .x { @apply --hover; }");
			var x = new Node(null, "x") { State = UiStates.s_hover.Mask };

			Assert.AreEqual(0.8f, Opacity(passed, x));
			Assert.AreEqual(0.1f, Opacity(fallback, x));
		}

		[Test]
		public void Mixin_ContentsBlockDoesNotSeeTheParameters()
		{
			var diagnostics = new List<string>();
			var expanded = CssMixins.Expand(
				"@mixin --wrap(--amount: 0.1) { opacity: var(--amount); @contents; } .x { @apply --wrap { width: var(--amount); } }",
				"Assets/Wrap.css",
				null,
				diagnostics,
				new List<string>());

			CollectionAssert.IsEmpty(diagnostics);
			StringAssert.Contains("opacity: 0.1;", expanded);
			StringAssert.Contains("width: var(--amount);", expanded);
		}

		[Test]
		public void Mixin_NestedRulesNestUnderTheApplyingRule()
		{
			var engine = Load("@mixin --button { opacity: 0.5; &:hover { opacity: 0.9; } .icon { opacity: 0.4; } } .b { @apply --button; }");
			var button = new Node(null, "b");

			Assert.AreEqual(0.5f, Opacity(engine, button));
			Assert.AreEqual(0.4f, Opacity(engine, new Node(button, "icon")));
			Assert.AreEqual(-1f, Opacity(engine, new Node(null, "icon")), "must not leak to every .icon");

			button.State = UiStates.s_hover.Mask;

			Assert.AreEqual(0.9f, Opacity(engine, button));
		}

		[Test]
		public void Mixin_MediaInsideTheBodyStaysConditional()
		{
			var result = CssCompiler.Compile(
				"@mixin --narrow { @media (max-width: 40rem) { opacity: 0.5; } } .x { @apply --narrow; }",
				"Assets/Narrow.css");

			CollectionAssert.IsEmpty(result.Diagnostics);
			Assert.AreEqual(1, result.Sheet.Rules.Length);
			Assert.AreNotEqual(0, result.Sheet.Rules[0].MediaQueryIndex);
		}

		[Test]
		public void Mixin_AppliesOtherMixinsWithItsParameters()
		{
			var engine = Load(
				"@mixin --fade(--amount) { opacity: var(--amount); }"
				+ "@mixin --dim(--level: 0.35) { @apply --fade(var(--level)); }"
				+ ".x { @apply --dim; } .y { @apply --dim(0.45); }");

			Assert.AreEqual(0.35f, Opacity(engine, new Node(null, "x")));
			Assert.AreEqual(0.45f, Opacity(engine, new Node(null, "y")));
		}

		[Test]
		public void Mixin_LastDefinitionWinsAndMayFollowItsUse()
		{
			var engine = Load(".x { @apply --fade; } @mixin --fade { opacity: 0.1; } @mixin --fade { opacity: 0.2; }");

			Assert.AreEqual(0.2f, Opacity(engine, new Node(null, "x")));
		}

		[TestCase(".x { @apply --nothing; }", "names no mixin")]
		[TestCase("@mixin --m { opacity: 1; } .x { @apply --m(1); }", "passes 1 arguments")]
		[TestCase("@mixin --m(--a) { opacity: var(--a); } .x { @apply --m; }", "leaves --a without a value")]
		[TestCase("@mixin --a { @apply --b; } @mixin --b { @apply --a; } .x { @apply --a; }", "applies itself")]
		[TestCase("@mixin --m { opacity: 1; } @apply --m;", "only works inside a style rule")]
		[TestCase("@keyframes k { from { @apply --m; } } @mixin --m { opacity: 1; }", "only works inside a style rule")]
		[TestCase("@layer base { @mixin --m { opacity: 1; } }", "must be written at the top level")]
		[TestCase("@mixin m { opacity: 1; }", "needs a name that starts with '--'")]
		public void Mixin_MistakesAreNamed(string css, string diagnostic)
		{
			var result = CssCompiler.Compile(css, "Assets/Mistakes.css");

			Assert.IsNotNull(result.Sheet);
			Assert.That(result.Diagnostics, Has.Some.Contains(diagnostic));
		}

		[Test]
		public void Mixin_UnusedMixinsCompileToNothing()
		{
			var result = CssCompiler.Compile("@mixin --card { .title { opacity: 1; } }", "Assets/Mixins.css");

			CollectionAssert.IsEmpty(result.Diagnostics);
			CollectionAssert.IsEmpty(result.Classes);
			Assert.AreEqual(0, result.Sheet.Rules.Length);
		}

		[Test]
		public void Mixin_ClassesBelongToTheApplyingSheet()
		{
			var result = CssCompiler.Compile("@mixin --card { .title { opacity: 1; } } .card { @apply --card; }", "Assets/Card.css");

			CollectionAssert.AreEqual(new[] { "card", "title" }, result.Classes);
		}

		[Test]
		public void Mixin_ComesFromImportedSheets()
		{
			var engine = Load(
				("Assets/UI/Card.css", "@import \"../Theme/Theme.css\"; .card { @apply --fade(0.35); } .tile { @apply --base; }"),
				("Assets/Theme/Theme.css", "@import \"Base.css\"; @mixin --fade(--amount) { opacity: var(--amount); }"),
				("Assets/Theme/Base.css", "@mixin --base { opacity: 0.55; }"));

			Assert.AreEqual(0.35f, Opacity(engine, new Node(null, "card")));
			Assert.AreEqual(0.55f, Opacity(engine, new Node(null, "tile")));
		}

		[Test]
		public void Mixin_ImportedSheetsAreDependencies()
		{
			var sheets = Sheets(
				("Assets/Theme/Theme.css", "@import \"Base.css\"; @mixin --fade { opacity: 0.1; }"),
				("Assets/Theme/Base.css", "@import \"Theme.css\"; @mixin --base { opacity: 0.2; }"));

			var result = CompileWithImports("Assets/Card.css", "@import \"Theme/Theme.css\"; .x { @apply --base; }", sheets);

			CollectionAssert.IsEmpty(result.Diagnostics);
			CollectionAssert.AreEquivalent(new[] { "Assets/Theme/Theme.css", "Assets/Theme/Base.css" }, result.Dependencies);
		}

		[Test]
		public void Mixin_OwnDefinitionOverridesAnImportedOne()
		{
			var engine = Load(
				("Assets/Card.css", "@import \"Theme.css\"; .x { @apply --fade; } @mixin --fade { opacity: 0.2; }"),
				("Assets/Theme.css", "@mixin --fade { opacity: 0.1; }"));

			Assert.AreEqual(0.2f, Opacity(engine, new Node(null, "x")));
		}

		[Test]
		public void Mixin_SheetWithoutApplyReadsNoImports()
		{
			var result = CompileWithImports("Assets/Card.css", "@import \"Theme.css\"; .x { opacity: 1; }", Sheets(("Assets/Theme.css", "")));

			CollectionAssert.IsEmpty(result.Dependencies);
		}

		[Test]
		public void Mixin_AppliesInsideScope()
		{
			var engine = Load(
				"@mixin --fade(--amount) { opacity: var(--amount); }"
				+ "@scope (.card) { @apply --fade(0.25); .label { @apply --fade(0.75); } }");

			var card = new Node(null, "card");

			Assert.AreEqual(0.25f, Opacity(engine, card));
			Assert.AreEqual(0.75f, Opacity(engine, new Node(card, "label")));
			Assert.AreEqual(-1f, Opacity(engine, new Node(null, "label")));
		}

		#endregion

		#region @layer and @import

		[Test]
		public void Layer_LaterLayerBeatsSpecificity()
		{
			var engine = Load(
				"@layer base, theme;"
				+ "@layer theme { .x { opacity: 0.2; } }"
				+ "@layer base { .x.y { opacity: 0.1; } }");

			Assert.AreEqual(0.2f, Opacity(engine, new Node(null, "x", "y")));
		}

		[Test]
		public void Layer_UnlayeredBeatsEveryLayer()
		{
			var engine = Load("@layer theme { .x.y { opacity: 0.2; } } .x { opacity: 0.9; }");

			Assert.AreEqual(0.9f, Opacity(engine, new Node(null, "x", "y")));
		}

		[Test]
		public void Layer_ParentRulesBeatSublayers()
		{
			var engine = Load("@layer ui { .x { opacity: 0.5; } @layer reset { .x.y { opacity: 0.1; } } }");

			Assert.AreEqual(0.5f, Opacity(engine, new Node(null, "x", "y")));
		}

		[Test]
		public void Import_OrdersTheImportedSheetFirst()
		{
			// By path alone Z.css would come last and win; importing it puts it first.
			var engine = Load(
				("Assets/B.css", "@import \"Z.css\"; .x { opacity: 0.5; }"),
				("Assets/Z.css", ".x { opacity: 0.7; }"));

			Assert.AreEqual(0.5f, Opacity(engine, new Node(null, "x")));
		}

		[Test]
		public void Import_LayerPutsTheWholeSheetInIt()
		{
			var engine = Load(
				("Assets/A.css", "@import url(\"Theme/C.css\") layer(base); .x { opacity: 0.1; }"),
				("Assets/Theme/C.css", ".x.y { opacity: 0.8; }"));

			Assert.AreEqual(0.1f, Opacity(engine, new Node(null, "x", "y")));
		}

		[Test]
		public void Import_ResolvesRelativePaths()
		{
			Assert.AreEqual("Assets/UI/Theme.css", CssBuilder.ResolveImport("Assets/UI/Card/Card.css", "../Theme.css"));
			Assert.AreEqual("Assets/UI/Card/Parts.css", CssBuilder.ResolveImport("Assets/UI/Card/Card.css", "./Parts.css"));
			Assert.AreEqual("Packages/com.x/Base.css", CssBuilder.ResolveImport("Assets/UI/Card.css", "Packages/com.x/Base.css"));
		}

		#endregion

		#region Rule index and lazy loading

		[Test]
		public void Index_MatchesByTypeAndUniversally()
		{
			var engine = Load("Text { opacity: 0.1; } * { opacity: 0.2; } .ix-row Text { opacity: 0.3; }");

			Assert.AreEqual(0.1f, Opacity(engine, new Node(null) { Type = "Text" }));
			Assert.AreEqual(0.3f, Opacity(engine, new Node(new Node(null, "ix-row")) { Type = "Text" }));
			Assert.AreEqual(0.2f, Opacity(engine, new Node(null) { Type = "View" }));
		}

		[Test]
		public void Index_CompoundOfClassesNeedsEveryClass()
		{
			var engine = Load(".ix-a.ix-b { opacity: 0.4; } .ix-b { opacity: 0.1; }");

			Assert.AreEqual(0.4f, Opacity(engine, new Node(null, "ix-a", "ix-b")));
			Assert.AreEqual(0.4f, Opacity(engine, new Node(null, "ix-b", "ix-a")));
			Assert.AreEqual(-1f, Opacity(engine, new Node(null, "ix-a")));
			Assert.AreEqual(0.1f, Opacity(engine, new Node(null, "ix-b")));
		}

		[Test]
		public void Index_NeedsPointerFindsHoverCompounds()
		{
			var byClass = Load(".ix-card:hover .ix-title { opacity: 0.5; }");

			Assert.IsTrue(byClass.NeedsPointer(new Node(null, "ix-card")));
			Assert.IsFalse(byClass.NeedsPointer(new Node(null, "ix-title")));

			var byType = Load("Text:hover { opacity: 0.5; }");

			Assert.IsTrue(byType.NeedsPointer(new Node(null) { Type = "Text" }));
			Assert.IsFalse(byType.NeedsPointer(new Node(null) { Type = "View" }));

			Assert.IsTrue(Load(":hover { opacity: 0.5; }").NeedsPointer(new Node(null)));
		}

		[TestCase(".lz-a { opacity: 1; } .lz-b .lz-c { opacity: 1; }", false)]
		[TestCase(".lz-a:hover { opacity: 1; } @scope (View) { .lz-b { opacity: 1; } }", false)]
		[TestCase("Text { opacity: 1; }", true)]
		[TestCase("View:hover .lz-a { opacity: 1; }", true)]
		[TestCase("@scope (.lz-card) { :scope { opacity: 1; } }", true)]
		[TestCase(".lz-a { animation-name: lz-k; } @keyframes lz-k { from { opacity: 0; } }", true)]
		public void Lazy_ClassifiesSheets(string css, bool eager)
		{
			Assert.AreEqual(eager, CompileAsset("Assets/Lazy.css", css).Eager);
		}

		[Test]
		public void Lazy_ReadsASheetWhenItsClassFirstAppears()
		{
			var library = Library(
				("Assets/A.css", "Text { opacity: 0.1; }"),
				("Assets/B.css", ".lz-b { opacity: 0.5; }"),
				("Assets/C.css", ".lz-c { opacity: 0.6; }"));
			var engine = Engine(library);

			Assert.AreEqual(1, library.ReadCount, "only the eager sheet");
			Assert.AreEqual(-1f, Opacity(engine, new Node(null, "lz-other")));
			Assert.AreEqual(1, library.ReadCount);

			Assert.AreEqual(0.5f, Opacity(engine, new Node(null, "lz-b")));
			Assert.AreEqual(2, library.ReadCount);

			Assert.AreEqual(0.5f, Opacity(engine, new Node(null, "lz-b")));
			Assert.AreEqual(2, library.ReadCount);
		}

		[Test]
		public void Lazy_AncestorClassReadsTheSheet()
		{
			var library = Library(("Assets/A.css", ".lz-panel .lz-label { opacity: 0.7; }"));
			var engine = Engine(library);
			var panel = new Node(null, "lz-panel");

			Opacity(engine, panel);

			Assert.AreEqual(1, library.ReadCount);
			Assert.AreEqual(0.7f, Opacity(engine, new Node(panel, "lz-label")));
		}

		[Test]
		public void Lazy_KeepsLayerAndDocumentOrder()
		{
			var engine = Load(
				("Assets/A.css", "@layer base, theme; Text { opacity: 0.1; }"),
				("Assets/B.css", "@layer theme { .lz-x { opacity: 0.2; } }"),
				("Assets/C.css", "@layer base { .lz-x.lz-y { opacity: 0.3; } }"),
				("Assets/D.css", ".lz-z { opacity: 0.4; }"),
				("Assets/E.css", ".lz-z { opacity: 0.7; }"));

			Assert.AreEqual(0.2f, Opacity(engine, new Node(null, "lz-x", "lz-y")), "a later layer beats specificity");
			Assert.AreEqual(0.7f, Opacity(engine, new Node(null, "lz-z")), "the later sheet wins a tie");
		}

		[Test]
		public void Lazy_FontsLoadOnFirstResolve()
		{
			UiFonts.Clear();
			UiFonts.Declare("LzMissing", "ReactiveUI/NoSuchFont", UiFonts.NormalWeight, "Assets/Fonts.css");

			LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no font asset at Resources/ReactiveUI/NoSuchFont"));
			Assert.IsNull(UiFonts.Resolve("LzMissing"));

			// The missing face is dropped, so a second resolve warns about the family instead.
			LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("No font registered for font-family 'LzMissing'"));
			Assert.IsNull(UiFonts.Resolve("LzMissing"));
		}

		private sealed class ListRecorder : IStylePreloadRecorder
		{
			public readonly List<(string Root, PreloadKind Kind, string Path)> Records = new();
			public readonly List<(string Root, string Font, string Text)> Characters = new();

			public void Record(string root, PreloadKind kind, string path) => Records.Add((root, kind, path));

			public void RecordCharacters(string root, string font, string text) => Characters.Add((root, font, text));
		}

		/// <summary>Runs <paramref name="body"/> recording into a fresh recorder as <paramref name="root"/>.</summary>
		private static ListRecorder Recording(string root, System.Action body)
		{
			var recorder = new ListRecorder();
			var previousRecorder = StylePreloads.Recorder;
			var previousCurrent = StylePreloads.Current;

			StylePreloads.Recorder = recorder;
			StylePreloads.Current = root;

			try
			{
				body();
			}
			finally
			{
				StylePreloads.Recorder = previousRecorder;
				StylePreloads.Current = previousCurrent;
			}

			return recorder;
		}

		[Test]
		public void Preload_ActivatesASheetBeforeItsClassesAppear()
		{
			var library = Library(
				("Assets/A.css", "Text { opacity: 0.1; }"),
				("Assets/B.css", ".pl-b { opacity: 0.5; }"));
			var engine = Engine(library);

			engine.Preload(library.SlotOf("Assets/B.css"));
			engine.Preload(library.SlotOf("Assets/Missing.css"));

			Assert.AreEqual(2, library.ReadCount);
			Assert.AreEqual(0.5f, Opacity(engine, new Node(null, "pl-b")));
		}

		[Test]
		public void Preload_RecordsOnlySheetsActivatedOnDemand()
		{
			var library = Library(
				("Assets/A.css", "Text { opacity: 0.1; }"),
				("Assets/B.css", ".pl-b { opacity: 0.5; }"),
				("Assets/C.css", ".pl-c { opacity: 0.6; }"));
			var engine = Engine(library);

			engine.Preload(library.SlotOf("Assets/B.css"));

			var recorder = Recording("Game.Root", () =>
			{
				Opacity(engine, new Node(null, "pl-b"));
				Opacity(engine, new Node(null, "pl-c"));
				Opacity(engine, new Node(null, "pl-c"));
				Opacity(engine, new Node(null) { Type = "Text" });
			});

			CollectionAssert.AreEqual(new[] { ("Game.Root", PreloadKind.Sheet, "Assets/C.css") }, recorder.Records);
		}

		[Test]
		public void Preload_LoadsDeclaredFontsAndRecordsResolvedOnes()
		{
			const string path = "Fonts & Materials/LiberationSans SDF";

			UiFonts.Clear();
			UiFonts.Declare("PlSans", path, UiFonts.NormalWeight, "Assets/Fonts.css");

			Assert.IsFalse(UiFonts.IsLoaded(path));

			UiFonts.Preload(path);

			Assert.IsTrue(UiFonts.IsLoaded(path));

			var recorder = Recording("Game.Root", () => Assert.IsNotNull(UiFonts.Resolve("PlSans")));

			CollectionAssert.AreEqual(new[] { ("Game.Root", PreloadKind.Font, path) }, recorder.Records);
		}

		[Test]
		public void Preload_ManifestKeepsEachListSortedAndUnique()
		{
			var manifest = ScriptableObject.CreateInstance<StylePreloadManifest>();
			_created.Add(manifest);

			Assert.IsTrue(manifest.Add("Game.Root", PreloadKind.Sheet, "Assets/B.css", out var first));
			Assert.IsTrue(manifest.Add("Game.Root", PreloadKind.Sheet, "Assets/A.css", out var second));
			Assert.IsFalse(manifest.Add("Game.Root", PreloadKind.Sheet, "Assets/B.css", out _));
			Assert.IsTrue(manifest.Add("Game.Root", PreloadKind.Font, "Fonts/Body", out _));

			Assert.IsTrue(first);
			Assert.IsFalse(second);
			Assert.IsNull(manifest.Find("Game.Other"));

			var entry = manifest.Find("Game.Root");

			CollectionAssert.AreEqual(new[] { "Assets/A.css", "Assets/B.css" }, entry.Sheets);
			CollectionAssert.AreEqual(new[] { "Fonts/Body" }, entry.Fonts);
			CollectionAssert.IsEmpty(entry.Textures);
		}

		[Test]
		public void Preload_ManifestKeepsCharactersSortedAndUnique()
		{
			var manifest = ScriptableObject.CreateInstance<StylePreloadManifest>();
			_created.Add(manifest);

			Assert.IsTrue(manifest.AddCharacters("Game.Root", "Fonts/Body", StylePreloadManifest.CodePoints("cab"), out var newRoot));
			Assert.IsTrue(manifest.AddCharacters("Game.Root", "Fonts/Body", StylePreloadManifest.CodePoints("bd\U0001F600"), out _));
			Assert.IsFalse(manifest.AddCharacters("Game.Root", "Fonts/Body", StylePreloadManifest.CodePoints("ab"), out _));

			Assert.IsTrue(newRoot);

			var characters = manifest.Find("Game.Root").Characters;

			Assert.AreEqual(1, characters.Count);
			Assert.AreEqual("abcd\U0001F600", characters[0].Characters, "a surrogate pair stays whole");
		}

		[Test]
		public void Preload_AddsCharactersToADynamicAtlas()
		{
			var source = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
			var font = TMPro.TMP_FontAsset.CreateFontAsset(source);

			_created.Add(font);
			_created.Add(font.material);
			_created.AddRange(font.atlasTextures);

			Assert.IsFalse(font.HasCharacters("Qz"));

			UiFonts.AddCharacters(font, "Qz");

			Assert.IsTrue(font.HasCharacters("Qz"));
		}

		[Test]
		public void Preload_TextRecordsTheCharactersItRenders()
		{
			const string path = "Fonts & Materials/LiberationSans SDF";

			var library = Library(("Assets/Fonts.css",
				"@font-face { font-family: 'TxSans'; src: resource('" + path + "'); } Text { font-family: 'TxSans'; text-transform: uppercase; }"));
			var container = new GameObject("Container", typeof(RectTransform));
			var runtime = new UiRuntime((RectTransform)container.transform, 32f, null, library, "Game.Root");
			var recorder = new ListRecorder();
			var previous = StylePreloads.Recorder;

			StylePreloads.Recorder = recorder;

			try
			{
				runtime.SetRoot(() => new Text(ClassName.Intern("tx-label"), new TextProps("abc")));
				runtime.Update();
			}
			finally
			{
				StylePreloads.Recorder = previous;
				runtime.Dispose();
				Object.DestroyImmediate(container);
			}

			CollectionAssert.Contains(recorder.Characters, ("Game.Root", path, "ABC"), "recorded as rendered, after text-transform");
		}

		[Test]
		public void Import_ConditionsAreNamed()
		{
			var diagnostics = new List<string>();
			var asset = CompileAsset("Assets/A.css", "@import \"B.css\" screen; .x { opacity: 1; }", diagnostics);

			Assert.AreEqual(1, asset.Imports.Count);
			Assert.That(diagnostics, Has.Some.Contains("is ignored"));
		}

		#endregion
	}
}

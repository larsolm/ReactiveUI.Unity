using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ReactiveUI.Editor;
using UnityEngine;

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

		public IMatchTarget MatchParent => _parent;
		public IMatchTarget MatchPreviousSibling => null;
		public ClassSet MatchClasses => _classes;
		public ulong MatchState => State;
		public bool MatchesType(int typeId) => false;
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

		private CompiledStyleSheet CompileAsset(string path, string css, List<string> diagnostics = null)
		{
			var result = CssCompiler.Compile(css, path);

			Assert.IsNotNull(result.Sheet, string.Join("\n", result.Diagnostics));
			diagnostics?.AddRange(result.Diagnostics);

			var asset = ScriptableObject.CreateInstance<CompiledStyleSheet>();
			asset.Set(path, result.Data, result.Classes, result.UnscopedClasses, result.Imports, result.Diagnostics.ToArray());
			_created.Add(asset);

			return asset;
		}

		/// <summary>Compiles, orders and links sheets the way the catalog does, and returns an engine over them.</summary>
		private StyleEngine Load(params (string Path, string Css)[] files)
		{
			var sources = CssAssets.Order(files.Select(file => CompileAsset(file.Path, file.Css)).ToList(), report: false);
			var sheets = sources.Select(source => source.Load()).ToList();
			var diagnostics = new List<string>();

			CascadeLayers.Rank(sources, sheets, diagnostics);

			var engine = new StyleEngine(new StyleContext(16f));
			engine.SetSheets(sheets);

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
			var engine = new StyleEngine(new StyleContext(16f));
			engine.SetSheets(new[] { asset.Load() });

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

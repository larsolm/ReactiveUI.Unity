using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ReactiveUI.Generators.Tests
{
	public sealed class StyleClassGeneratorTests
	{
		private const string ManifestPath = "Assets/Plugins/ReactiveUI/StyleClasses.ReactiveUI.Generators.additionalfile";

		/// <summary>
		/// A companion needs only to be partial; whether it is also a component is the other
		/// generator's business, covered by <see cref="BothGenerators_ShareOneComponent"/>.
		/// </summary>
		private const string Button = """
			namespace Game.UI
			{
				public partial struct Button
				{
				}
			}
			""";

		private static GeneratorResult Run(string manifest, params (string Path, string Source)[] sources) =>
			Harness.Run(new StyleClassGenerator(), sources, new[] { (ManifestPath, manifest) });

		[Fact]
		public void Manifest_ParsesComponentAndGlobalEntries()
		{
			var entries = StyleClassManifest.Parse("""
				# comment
				sheet Assets/UI/Button.css
				assembly Game.UI
				namespace Game.UI
				class btn
				class btn__label

				sheet Assets/UI/My Layout.css
				assembly Game.UI
				namespace Game.UI
				class row
				""");

			Assert.Equal(2, entries.Count);

			Assert.Equal("Assets/UI/Button.css", entries[0].Sheet);
			Assert.Equal("Game.UI", entries[0].Assembly);
			Assert.Equal("Game.UI", entries[0].Namespace);
			Assert.Equal(new[] { "btn", "btn__label" }, entries[0].Classes);

			Assert.Equal("Assets/UI/My Layout.css", entries[1].Sheet);
			Assert.Equal(new[] { "row" }, entries[1].Classes);
		}

		[Fact]
		public void ColocatedSheet_GeneratesANestedStylesTable_ThatCompiles()
		{
			var result = Run(
				"""
				sheet Assets/UI/Button.css
				assembly Game.UI
				namespace Game.UI
				class btn
				class btn__label
				""",
				("Assets/UI/Button.cs", Button),
				("Assets/UI/Usage.cs", """
					namespace Game.UI
					{
						public static class Usage
						{
							public static ReactiveUI.ClassName Label => Button.Styles.BtnLabel;
						}
					}
					"""));

			Assert.Empty(result.Diagnostics);
			Assert.Empty(result.CompileDiagnostics);

			var source = result.Single();

			Assert.Contains("partial struct Button", source);
			Assert.Contains("public static class Styles", source);
			Assert.Contains("BtnLabel = global::ReactiveUI.ClassName.Intern(\"btn__label\");", source);
		}

		[Fact]
		public void TreePathsResolvedByTheCompiler_StillMatchProjectRelativeManifestPaths()
		{
			var result = Run(
				"""
				sheet Assets/UI/Button.css
				assembly Game.UI
				namespace Game.UI
				class btn
				""",
				("C:\\Projects\\Game\\Assets\\UI\\Button.cs", Button));

			Assert.Empty(result.Diagnostics);
			Assert.Single(result.Sources);
		}

		[Fact]
		public void SharedSheet_GeneratesIntoTheUiTable()
		{
			var result = Run(
				"""
				sheet Assets/UI/layout.css
				assembly Game.UI
				namespace Game.UI
				class row
				class col-2
				""",
				("Assets/UI/Usage.cs", """
					namespace Game.UI
					{
						public static class Usage
						{
							public static ReactiveUI.ClassName Row => Ui.Row;
						}
					}
					"""));

			Assert.Empty(result.CompileDiagnostics);

			var source = result.Single();

			Assert.Contains("public static partial class Ui", source);
			Assert.Contains("Col2 = global::ReactiveUI.ClassName.Intern(\"col-2\");", source);
		}

		[Fact]
		public void EntriesForAnotherAssembly_AreIgnored()
		{
			var result = Run(
				"""
				sheet Assets/Other/layout.css
				assembly Game.Other
				namespace Game.Other
				class row
				""");

			Assert.Empty(result.Sources);
			Assert.Empty(result.Diagnostics);
		}

		[Fact]
		public void ClassesMappingToTheSameMember_KeepTheFirstAndWarn()
		{
			var result = Run(
				"""
				sheet Assets/UI/Button.css
				assembly Game.UI
				namespace Game.UI
				class btn-label
				class btn_label
				""",
				("Assets/UI/Button.cs", Button));

			var diagnostic = Assert.Single(result.Diagnostics);

			Assert.Equal("RUI0003", diagnostic.Id);
			Assert.Single(result.Single().Split('\n'), line => line.Contains("BtnLabel"));
			Assert.Empty(result.CompileDiagnostics);
		}

		[Fact]
		public void SharedTableNames_AreReconciledAcrossSheets()
		{
			var result = Run(
				"""
				sheet Assets/UI/a.css
				assembly Game.UI
				namespace Game.UI
				class row

				sheet Assets/UI/b.css
				assembly Game.UI
				namespace Game.UI
				class row
				class Row
				""");

			// The same CSS name in two sheets is one class and quietly shares the member; a different
			// name mapping onto it is a collision.
			Assert.Equal("RUI0003", Assert.Single(result.Diagnostics).Id);
			Assert.Empty(result.CompileDiagnostics);
		}

		[Fact]
		public void ReservedAndLeadingDigitNames_AreMadeValid()
		{
			var result = Run(
				"""
				sheet Assets/UI/Button.css
				assembly Game.UI
				namespace Game.UI
				class equals
				class 2col
				class styles
				""",
				("Assets/UI/Button.cs", Button));

			Assert.Empty(result.CompileDiagnostics);

			var source = result.Single();

			Assert.Contains(" Equals_ =", source);
			Assert.Contains(" _2col =", source);
			Assert.Contains(" Styles_ =", source);
		}

		[Fact]
		public void TheSameManifest_RoutesBySheetWhetherACompanionExists()
		{
			const string manifest = """
				sheet Assets/UI/Button.css
				assembly Game.UI
				namespace Game.UI
				class btn
				""";

			// No Button.cs yet: the classes are shared. Adding it moves them into Button.Styles with the
			// manifest untouched — the editor never has to notice.
			var without = Run(manifest);
			var with = Run(manifest, ("Assets/UI/Button.cs", Button));

			Assert.Contains("public static partial class Ui", without.Single());
			Assert.Contains("public static class Styles", with.Single());
		}

		[Fact]
		public void CompanionNotPartial_IsReported()
		{
			var result = Run(
				"""
				sheet Assets/UI/Button.css
				assembly Game.UI
				namespace Game.UI
				class btn
				""",
				("Assets/UI/Button.cs", """
					namespace Game.UI
					{
						public struct Button { }
					}
					"""));

			Assert.Empty(result.Sources);
			Assert.Equal("RUI0002", Assert.Single(result.Diagnostics).Id);
		}

		[Fact]
		public void CompanionWithoutAMatchingType_IsReported()
		{
			var result = Run(
				"""
				sheet Assets/UI/Button.css
				assembly Game.UI
				namespace Game.UI
				class btn
				""",
				("Assets/UI/Button.cs", """
					namespace Game.UI
					{
						public static class Helpers { }
					}
					"""));

			Assert.Empty(result.Sources);
			Assert.Equal("RUI0005", Assert.Single(result.Diagnostics).Id);
		}

		[Fact]
		public void UnityStaticsCleanupAttribute_IsAppliedWhenAvailable()
		{
			var result = Run(
				"""
				sheet Assets/UI/layout.css
				assembly Game.UI
				namespace Game.UI
				class row
				""",
				("Assets/Stub.cs", """
					namespace Unity.Scripting.LifecycleManagement
					{
						public sealed class NoAutoStaticsCleanupAttribute : System.Attribute { }
					}
					"""));

			Assert.Empty(result.CompileDiagnostics);
			Assert.Contains("[global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanupAttribute] public static readonly", result.Single());
		}

		[Fact]
		public void BothGenerators_ShareOneComponent()
		{
			var result = Harness.Run(
				new IIncrementalGenerator[] { new ComponentGenerator(), new StyleClassGenerator() },
				new[]
				{
					("Assets/UI/Button.cs", """
						using ReactiveUI;

						namespace Game.UI
						{
							public partial struct Button : IComponent
							{
								public Element Render() => default;
							}
						}
						"""),
				},
				new[]
				{
					(ManifestPath, """
						sheet Assets/UI/Button.css
						assembly Game.UI
						namespace Game.UI
						class btn
						"""),
				});

			Assert.Equal(new[] { "Game.UI.Button.Component.g.cs", "Game.UI.Button.Styles.g.cs" }, result.Sources.Keys.OrderBy(key => key));
			Assert.Empty(result.CompileDiagnostics);
		}

		[Fact]
		public void NonManifestAdditionalFiles_AreIgnored()
		{
			var result = Harness.Run(
				new StyleClassGenerator(),
				new[] { ("Assets/UI/Button.cs", Button) },
				new[] { ("Library/Bee/Game.UI.UnityAdditionalFile.txt", "sheet Assets/UI/x.css\nassembly Game.UI\nglobal Game.UI\nclass row") });

			Assert.Empty(result.Sources);
		}
	}
}

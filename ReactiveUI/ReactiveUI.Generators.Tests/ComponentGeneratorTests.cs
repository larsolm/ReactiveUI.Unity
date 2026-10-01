using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ReactiveUI.Generators.Tests
{
	public sealed class ComponentGeneratorTests
	{
		private static GeneratorResult Run(string source) =>
			Harness.Run(new ComponentGenerator(), new[] { ("Assets/UI/Source.cs", source) });

		[Fact]
		public void ComponentWithProps_GetsBothConstructors_AndCompiles()
		{
			var result = Run("""
				using ReactiveUI;

				namespace Game.UI
				{
					public readonly record struct CounterProps(int Start);

					public partial struct Counter : IComponent<CounterProps>
					{
						public Element Render(in CounterProps props) => default;
					}

					public static class Usage
					{
						public static Element Bare() => new Counter();
						public static Element WithProps() => new Counter(new CounterProps(3));
						public static Element? Conditional(bool show) => show & new Counter();
					}
				}
				""");

			Assert.Empty(result.Diagnostics);
			Assert.Empty(result.CompileDiagnostics);

			var source = result.Single();

			Assert.Contains("namespace Game.UI", source);
			Assert.Contains("partial struct Counter", source);
			Assert.Contains("public Counter() => Handle = global::ReactiveUI.Element.Of<Counter, global::Game.UI.CounterProps>(null);", source);
			Assert.Contains("public Counter(global::Game.UI.CounterProps? props = null)", source);
		}

		[Fact]
		public void ComponentWithoutProps_GetsParameterlessConstructor()
		{
			var result = Run("""
				using ReactiveUI;

				namespace Game.UI
				{
					public partial struct Screen : IComponent
					{
						public Element Render() => default;
					}
				}
				""");

			Assert.Empty(result.CompileDiagnostics);
			Assert.Contains("public Screen() => Handle = global::ReactiveUI.Element.Of<Screen>();", result.Single());
		}

		[Fact]
		public void NestedComponent_ReopensEveryEnclosingType()
		{
			var result = Run("""
				using ReactiveUI;

				namespace Game.UI
				{
					public static partial class Screens
					{
						public partial struct Menu : IComponent
						{
							public Element Render() => default;
						}
					}
				}
				""");

			Assert.Empty(result.CompileDiagnostics);

			var source = result.Single();

			Assert.Contains("partial class Screens", source);
			Assert.Contains("partial struct Menu", source);
		}

		[Fact]
		public void FileScopedAndGlobalNamespaces_AreBothHandled()
		{
			var scoped = Run("""
				using ReactiveUI;

				namespace Game.UI;

				public partial struct Panel : IComponent
				{
					public Element Render() => default;
				}
				""");

			var global = Run("""
				using ReactiveUI;

				public partial struct Panel : IComponent
				{
					public Element Render() => default;
				}
				""");

			Assert.Empty(scoped.CompileDiagnostics);
			Assert.Contains("namespace Game.UI", scoped.Single());

			Assert.Empty(global.CompileDiagnostics);
			Assert.DoesNotContain("namespace", global.Single());
		}

		[Fact]
		public void RecordStructComponent_IsGenerated()
		{
			var result = Run("""
				using ReactiveUI;

				namespace Game.UI
				{
					public partial record struct Badge : IComponent
					{
						public Element Render() => default;
					}
				}
				""");

			Assert.Empty(result.CompileDiagnostics);
			Assert.Contains("partial record struct Badge", result.Single());
		}

		[Fact]
		public void MissingPartial_IsAnError_NamingEveryTypeThatNeedsIt()
		{
			var result = Run("""
				using ReactiveUI;

				namespace Game.UI
				{
					public static class Screens
					{
						public struct Menu : IComponent
						{
							public Element Render() => default;
						}
					}
				}
				""");

			Assert.Empty(result.Sources);

			var diagnostic = Assert.Single(result.Diagnostics);

			Assert.Equal("RUI0001", diagnostic.Id);
			Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
			Assert.Contains("'Screens', 'Menu'", diagnostic.GetMessage());
		}

		[Fact]
		public void GenericConstraint_IsNotMistakenForADeclaration()
		{
			var result = Run("""
				using ReactiveUI;

				namespace Game.UI
				{
					public struct Holder<T> where T : struct, IComponent
					{
					}
				}
				""");

			Assert.Empty(result.Sources);
			Assert.Empty(result.Diagnostics);
		}

		[Fact]
		public void ComponentInCommentsOrStrings_IsIgnored()
		{
			var result = Run("""
				namespace Game.UI
				{
					// public partial struct Fake : IComponent { }
					/* public partial struct Other : IComponent { } */
					public static class Text
					{
						public const string Value = "public partial struct Quoted : IComponent { }";
					}
				}
				""");

			Assert.Empty(result.Sources);
		}

		[Fact]
		public void UnrelatedInterfaceNamedIComponent_IsIgnored()
		{
			var result = Run("""
				namespace Other
				{
					public interface IComponent { }

					public partial struct NotOurs : IComponent { }
				}
				""");

			Assert.Empty(result.Sources);
			Assert.Empty(result.Diagnostics);
		}

		[Fact]
		public void InterfaceListedOnTwoParts_GeneratesOnce()
		{
			var result = Harness.Run(new ComponentGenerator(), new[]
			{
				("Assets/UI/Split.cs", """
					using ReactiveUI;

					namespace Game.UI
					{
						public partial struct Split : IComponent
						{
							public Element Render() => default;
						}
					}
					"""),
				("Assets/UI/Split.More.cs", """
					using ReactiveUI;

					namespace Game.UI
					{
						public partial struct Split : IComponent
						{
						}
					}
					"""),
			});

			Assert.Single(result.Sources);
			Assert.Empty(result.CompileDiagnostics);
		}

		[Fact]
		public void GenericComponent_IsReported()
		{
			var result = Run("""
				using ReactiveUI;

				namespace Game.UI
				{
					public partial struct Cell<T> : IComponent
					{
						public Element Render() => default;
					}
				}
				""");

			Assert.Empty(result.Sources);
			Assert.Equal("RUI0004", Assert.Single(result.Diagnostics).Id);
		}

		[Fact]
		public void TwoComponentsInOneFile_EachGetTheirOwnFile()
		{
			var result = Run("""
				using ReactiveUI;

				namespace Game.UI
				{
					public partial struct First : IComponent { public Element Render() => default; }
					public partial struct Second : IComponent { public Element Render() => default; }
				}
				""");

			Assert.Equal(new[] { "Game.UI.First.Component.g.cs", "Game.UI.Second.Component.g.cs" }, result.Sources.Keys.OrderBy(key => key));
			Assert.Empty(result.CompileDiagnostics);
		}
	}
}

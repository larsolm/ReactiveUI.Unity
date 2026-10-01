using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ReactiveUI.Generators.Tests
{
	/// <summary>
	/// Runs a generator over in-memory sources the way Unity's compiler would, then compiles the result.
	/// </summary>
	internal static class Harness
	{
		/// <summary>
		/// The slice of the ReactiveUI runtime that generated code binds to, with the same signatures.
		/// </summary>
		public const string RuntimeStub = """
			namespace ReactiveUI
			{
				public interface IElement : System.Collections.IEnumerable
				{
					Element Handle { get; }
				}

				public interface IComponent<TProps> : IElement
					where TProps : struct, System.IEquatable<TProps>
				{
					Element Render(in TProps props);
				}

				public interface IComponent : IElement
				{
					Element Render();
				}

				public readonly struct Element : System.IEquatable<Element>
				{
					public bool Equals(Element other) => true;

					public static Element Of<TComponent, TProps>(TProps? props)
						where TComponent : struct, IComponent<TProps>
						where TProps : struct, System.IEquatable<TProps> => default;

					public static Element Of<TComponent>()
						where TComponent : struct, IComponent => default;
				}

				public static partial class Ui
				{
					public static System.Exception NotEnumerable(string name) => new System.NotSupportedException(name);
				}

				public readonly struct ClassName
				{
					public static ClassName Intern(string name) => default;
				}
			}
			""";

		private static readonly Lazy<MetadataReference[]> s_references = new(() =>
			((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
				.Split(Path.PathSeparator)
				.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
				.ToArray());

		private static readonly CSharpParseOptions s_parse = new(LanguageVersion.CSharp10);

		public static GeneratorResult Run(
			IIncrementalGenerator generator,
			IEnumerable<(string Path, string Source)> sources,
			IEnumerable<(string Path, string Text)>? additional = null,
			string assemblyName = "Game.UI")
		{
			return Run(new[] { generator }, sources, additional, assemblyName);
		}

		/// <summary>
		/// Runs several generators over one compilation, as Unity runs every generator in the package.
		/// </summary>
		public static GeneratorResult Run(
			IIncrementalGenerator[] generators,
			IEnumerable<(string Path, string Source)> sources,
			IEnumerable<(string Path, string Text)>? additional = null,
			string assemblyName = "Game.UI")
		{
			var trees = sources
				.Append((Path: "Packages/com.degravity.reactiveui/Runtime/Stub.cs", Source: RuntimeStub))
				.Select(source => CSharpSyntaxTree.ParseText(source.Source, s_parse, source.Path))
				.ToArray();

			var compilation = CSharpCompilation.Create(
				assemblyName,
				trees,
				s_references.Value,
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

			var texts = (additional ?? Array.Empty<(string, string)>())
				.Select(text => (AdditionalText)new InMemoryText(text.Path, text.Text))
				.ToImmutableArray();

			GeneratorDriver driver = CSharpGeneratorDriver.Create(
				generators.Select(generator => generator.AsSourceGenerator()),
				texts,
				s_parse);

			driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

			var run = driver.GetRunResult();

			return new GeneratorResult(
				run.GeneratedTrees.ToDictionary(tree => Path.GetFileName(tree.FilePath), tree => tree.ToString()),
				run.Diagnostics,
				output.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning).ToImmutableArray());
		}

		private sealed class InMemoryText : AdditionalText
		{
			private readonly string _text;

			public InMemoryText(string path, string text)
			{
				Path = path;
				_text = text;
			}

			public override string Path { get; }

			public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(_text);
		}
	}

	/// <param name="Sources">Generated files by hint name.</param>
	/// <param name="Diagnostics">What the generator reported.</param>
	/// <param name="CompileDiagnostics">Warnings and errors from compiling the sources with the generated
	/// files added — empty when the output is valid C#.</param>
	internal sealed record GeneratorResult(
		IReadOnlyDictionary<string, string> Sources,
		ImmutableArray<Diagnostic> Diagnostics,
		ImmutableArray<Diagnostic> CompileDiagnostics)
	{
		public string Single() => Sources.Values.Single();

		public string CompileErrors => string.Join("\n", CompileDiagnostics);
	}
}

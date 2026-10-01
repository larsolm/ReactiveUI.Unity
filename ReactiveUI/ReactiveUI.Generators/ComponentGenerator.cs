using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveUI.Generators
{
	/// <summary>
	/// Writes the half of every component that never varies — the handle, the constructors, the
	/// conversion to <c>Element</c> and the unused enumerator.
	/// </summary>
	/// <remarks>
	/// An element is a struct, so a component cannot inherit any of this from a base class, and a
	/// struct has no way to intercept its own construction. Every component therefore needs the same
	/// members spelled out, and one of them — the explicit parameterless constructor — fails silently
	/// when it is left out: <c>new Foo()</c> binds to the implicit one and zero-initialises into a
	/// handle to no element, so the component and everything under it disappears with nothing logged.
	/// Generating the set is what makes that unreachable, and <see cref="Diagnostics.ComponentNotPartial"/>
	/// is what makes forgetting <c>partial</c> a compile error instead of the same silent failure.
	/// </remarks>
	[Generator(LanguageNames.CSharp)]
	public sealed class ComponentGenerator : IIncrementalGenerator
	{
		private const string ComponentInterface = "IComponent";
		private const string PropsComponentMetadataName = "ReactiveUI.IComponent`1";
		private const string ComponentMetadataName = "ReactiveUI.IComponent";

		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var components = context.SyntaxProvider
				.CreateSyntaxProvider(
					static (node, _) => IsCandidate(node),
					static (context, token) => Describe(context, token))
				.Where(static model => model is not null);

			context.RegisterSourceOutput(components, static (context, model) => Emit(context, model!));
		}

		/// <summary>
		/// A struct naming something called <c>IComponent</c> in its base list — cheap enough to run on
		/// every node, and narrow enough that the semantic check behind it rarely runs.
		/// </summary>
		private static bool IsCandidate(SyntaxNode node)
		{
			return node is TypeDeclarationSyntax { BaseList: { } } type
				&& IsStruct(type)
				&& MentionsComponent(type);
		}

		private static bool IsStruct(TypeDeclarationSyntax type)
		{
			return type is StructDeclarationSyntax
				|| (type is RecordDeclarationSyntax record && record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword));
		}

		private static bool MentionsComponent(TypeDeclarationSyntax type)
		{
			foreach (var entry in type.BaseList!.Types)
			{
				if (Declarations.SimpleName(entry.Type) == ComponentInterface)
					return true;
			}

			return false;
		}

		private static ComponentModel? Describe(GeneratorSyntaxContext context, CancellationToken token)
		{
			var declaration = (TypeDeclarationSyntax)context.Node;

			if (context.SemanticModel.GetDeclaredSymbol(declaration, token) is not { } symbol)
				return null;

			// A partial struct can name the interface in more than one of its parts. Only the first
			// such part generates, so the members are never emitted twice.
			if (!IsFirstComponentPart(symbol, declaration, token))
				return null;

			var compilation = context.SemanticModel.Compilation;
			var withProps = compilation.GetTypeByMetadataName(PropsComponentMetadataName);
			var withoutProps = compilation.GetTypeByMetadataName(ComponentMetadataName);

			string? props = null;
			var implements = false;

			foreach (var implemented in symbol.AllInterfaces)
			{
				if (withProps is not null && SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, withProps))
				{
					props = implemented.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
					implements = true;

					break;
				}

				if (withoutProps is not null && SymbolEqualityComparer.Default.Equals(implemented, withoutProps))
					implements = true;
			}

			// Something else called IComponent — System.ComponentModel has one.
			if (!implements)
				return null;

			return new ComponentModel(Declarations.Describe(declaration), props, symbol.IsGenericType);
		}

		private static bool IsFirstComponentPart(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration, CancellationToken token)
		{
			foreach (var reference in symbol.DeclaringSyntaxReferences)
			{
				if (reference.GetSyntax(token) is TypeDeclarationSyntax { BaseList: { } } part && MentionsComponent(part))
					return part == declaration;
			}

			return false;
		}

		private static void Emit(SourceProductionContext context, ComponentModel model)
		{
			var type = model.Type;

			if (model.Generic)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					Diagnostics.GenericComponent,
					type.Location.ToLocation(),
					type.FullName));

				return;
			}

			if (type.NotPartial.Count > 0)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					Diagnostics.ComponentNotPartial,
					type.Location.ToLocation(),
					type.FullName,
					string.Join(", ", type.NotPartial.Select(name => $"'{name}'"))));

				return;
			}

			var name = type.Self.Name;
			var file = SourceBuilder.File(name);
			var opened = file.OpenType(type);

			file.Line("public global::ReactiveUI.Element Handle { get; }");
			file.Line();

			if (model.Props is null)
			{
				file.Line($"public {name}() => Handle = global::ReactiveUI.Element.Of<{name}>();");
			}
			else
			{
				file.Line($"public {name}() => Handle = global::ReactiveUI.Element.Of<{name}, {model.Props}>(null);");
				file.Line($"public {name}({model.Props}? props = null) => Handle = global::ReactiveUI.Element.Of<{name}, {model.Props}>(props);");
			}

			file.Line();
			file.Line($"public static implicit operator global::ReactiveUI.Element({name} self) => self.Handle;");
			file.Line($"public static global::ReactiveUI.Element? operator &(bool value, {name} self) => value ? self.Handle : (global::ReactiveUI.Element?)null;");
			file.Line($"public static global::ReactiveUI.Element? operator &({name} self, bool value) => value ? self.Handle : (global::ReactiveUI.Element?)null;");
			file.Line();
			file.Line($"global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw global::ReactiveUI.Ui.NotEnumerable(nameof({name}));");

			file.Close(opened);

			context.AddSource(HintName.For(type.FullName, "Component"), file.ToString());
		}
	}

	/// <param name="Props">The fully qualified props type, or null for a component that takes none.</param>
	internal sealed record ComponentModel(DeclaredType Type, string? Props, bool Generic);
}

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ReactiveUI.Generators
{
	/// <summary>
	/// One type in a declaration's nesting chain, as the generated partial has to restate it.
	/// </summary>
	/// <param name="Keyword"><c>struct</c>, <c>class</c>, <c>record struct</c> and so on, as declared.</param>
	/// <param name="Name">The name, with its type parameter list when it has one.</param>
	/// <param name="Partial">Whether this part was declared <c>partial</c>.</param>
	internal sealed record TypeScope(string Keyword, string Name, bool Partial);

	/// <summary>
	/// A <see cref="Location"/> that can live in an incremental model, which a real one cannot — it
	/// holds its syntax tree, so two equal locations from successive compilations never compare equal.
	/// </summary>
	internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
	{
		public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

		public static LocationInfo Of(SyntaxToken token)
		{
			var location = token.GetLocation();

			return new LocationInfo(token.SyntaxTree?.FilePath ?? string.Empty, location.SourceSpan, location.GetLineSpan().Span);
		}
	}

	/// <summary>
	/// Everything a generator needs to reopen a type in a partial of its own.
	/// </summary>
	/// <param name="Namespace">The enclosing namespace, or empty for the global one.</param>
	/// <param name="Containing">The types this one is declared inside, outermost first.</param>
	/// <param name="Self">The type itself.</param>
	/// <param name="NotPartial">This type and any containing type not declared <c>partial</c>, which a
	/// generated partial cannot reach.</param>
	internal sealed record DeclaredType(
		string Namespace,
		EquatableArray<TypeScope> Containing,
		TypeScope Self,
		EquatableArray<string> NotPartial,
		LocationInfo Location)
	{
		/// <summary>
		/// The namespace-qualified name with nesting, for diagnostics and hint names.
		/// </summary>
		public string FullName
		{
			get
			{
				var parts = new List<string>();

				if (Namespace.Length > 0)
					parts.Add(Namespace);

				foreach (var scope in Containing)
					parts.Add(scope.Name);

				parts.Add(Self.Name);

				return string.Join(".", parts);
			}
		}
	}

	internal static class Declarations
	{
		/// <summary>
		/// Reads a type declaration's namespace, nesting and <c>partial</c> status from syntax alone.
		/// </summary>
		public static DeclaredType Describe(TypeDeclarationSyntax declaration)
		{
			var containing = new List<TypeScope>();
			var notPartial = new List<string>();
			var namespaces = new List<string>();

			for (var parent = declaration.Parent; parent is not null; parent = parent.Parent)
			{
				switch (parent)
				{
					case TypeDeclarationSyntax type:
						containing.Insert(0, Scope(type));
						break;

					case BaseNamespaceDeclarationSyntax ns:
						namespaces.Insert(0, ns.Name.WithoutTrivia().ToString());
						break;
				}
			}

			var self = Scope(declaration);

			foreach (var scope in containing)
			{
				if (!scope.Partial)
					notPartial.Add(scope.Name);
			}

			if (!self.Partial)
				notPartial.Add(self.Name);

			return new DeclaredType(
				string.Join(".", namespaces),
				new EquatableArray<TypeScope>(containing.ToArray()),
				self,
				new EquatableArray<string>(notPartial.ToArray()),
				LocationInfo.Of(declaration.Identifier));
		}

		private static TypeScope Scope(TypeDeclarationSyntax type)
		{
			var name = type.Identifier.ValueText;

			if (type.TypeParameterList is { } parameters)
				name += parameters.WithoutTrivia().ToString();

			return new TypeScope(Keyword(type), name, type.Modifiers.Any(SyntaxKind.PartialKeyword));
		}

		private static string Keyword(TypeDeclarationSyntax type)
		{
			var keyword = type switch
			{
				RecordDeclarationSyntax record => record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
				StructDeclarationSyntax => "struct",
				InterfaceDeclarationSyntax => "interface",
				_ => "class",
			};

			// Every part of a ref struct has to say so; readonly and the rest merge across parts.
			return type.Modifiers.Any(SyntaxKind.RefKeyword) ? "ref " + keyword : keyword;
		}

		/// <summary>
		/// The last identifier of a base-list entry: <c>IComponent</c> for <c>IComponent</c>,
		/// <c>IComponent&lt;P&gt;</c> and <c>ReactiveUI.IComponent&lt;P&gt;</c> alike.
		/// </summary>
		public static string? SimpleName(TypeSyntax type)
		{
			return type switch
			{
				SimpleNameSyntax simple => simple.Identifier.ValueText,
				QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
				AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
				_ => null,
			};
		}

		/// <summary>
		/// True when <paramref name="treePath"/> — usually absolute, since the compiler resolves source
		/// paths against its working directory — is the file <paramref name="projectPath"/> names
		/// relative to the Unity project.
		/// </summary>
		public static bool SamePath(string treePath, string projectPath)
		{
			var tree = treePath.Replace('\\', '/');
			var project = projectPath.Replace('\\', '/');

			return string.Equals(tree, project, StringComparison.OrdinalIgnoreCase)
				|| tree.EndsWith("/" + project, StringComparison.OrdinalIgnoreCase);
		}
	}
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveUI.Generators
{
	/// <summary>
	/// Emits a <c>ClassName</c> constant for every class selector in the project's stylesheets, so a
	/// class is spelled once in CSS and referred to by symbol everywhere else.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A stylesheet colocated with a type — <c>Button.css</c> beside a <c>Button.cs</c> that declares
	/// <c>Button</c> — generates <c>Button.Styles.Btn</c> into a partial of that type, so the names a
	/// component can use are exactly the names it declares. Every other sheet generates into the shared
	/// <c>Ui</c> table in its assembly's root namespace.
	/// </para>
	/// <para>
	/// The sheets arrive through <see cref="StyleClassManifest"/>, but which of the two a sheet is gets
	/// decided here, from the compilation. That keeps the manifest a record of the CSS alone, so adding
	/// or removing the <c>.cs</c> beside a sheet reroutes its classes without the editor having to
	/// rewrite anything — which matters, because code using a table that does not exist yet cannot
	/// compile, and the editor code that would rewrite the manifest does not run until something does.
	/// </para>
	/// </remarks>
	[Generator(LanguageNames.CSharp)]
	public sealed class StyleClassGenerator : IIncrementalGenerator
	{
		private const string NoAutoStaticsCleanup = "Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanupAttribute";

		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var facts = context.CompilationProvider.Select(static (compilation, _) => new CompilationFacts(
				compilation.AssemblyName ?? string.Empty,
				compilation.GetTypeByMetadataName(NoAutoStaticsCleanup) is not null));

			var manifests = context.AdditionalTextsProvider
				.Where(static text => StyleClassManifest.IsManifest(text.Path))
				.Select(static (text, token) => StyleClassManifest.Parse(text.GetText(token)?.ToString() ?? string.Empty))
				.Collect();

			// Only this assembly's entries go downstream, so a class added to another assembly's sheet
			// leaves this compilation's output untouched.
			var sheets = manifests
				.Combine(facts)
				.Select(static (pair, _) => Owned(pair.Left, pair.Right.AssemblyName));

			var companions = context.SyntaxProvider
				.CreateSyntaxProvider(
					static (node, _) => IsCompanion(node),
					static (context, _) => new CompanionType(
						context.Node.SyntaxTree.FilePath,
						Declarations.Describe((TypeDeclarationSyntax)context.Node)))
				.Collect()
				.Select(static (types, _) => new EquatableArray<CompanionType>(types.ToArray()));

			// Every source file, so a companion that exists but declares no matching type can be told
			// apart from no companion at all.
			var files = context.SyntaxProvider
				.CreateSyntaxProvider(
					static (node, _) => node is CompilationUnitSyntax,
					static (context, _) => context.Node.SyntaxTree.FilePath)
				.Collect()
				.Select(static (paths, _) => new EquatableArray<string>(paths.ToArray()));

			context.RegisterSourceOutput(
				sheets.Combine(companions).Combine(files).Combine(facts),
				static (context, input) => Emit(
					context,
					new Inputs(input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right)));
		}

		private static EquatableArray<SheetEntry> Owned(ImmutableArray<EquatableArray<SheetEntry>> manifests, string assembly)
		{
			var owned = new List<SheetEntry>();

			foreach (var manifest in manifests)
			{
				foreach (var entry in manifest)
				{
					if (string.Equals(entry.Assembly, assembly, StringComparison.Ordinal))
						owned.Add(entry);
				}
			}

			return new EquatableArray<SheetEntry>(owned.ToArray());
		}

		/// <summary>
		/// A type named after the file it is declared in — the only kind a colocated sheet can attach to.
		/// </summary>
		private static bool IsCompanion(SyntaxNode node)
		{
			return node is TypeDeclarationSyntax type
				&& string.Equals(
					type.Identifier.ValueText,
					Path.GetFileNameWithoutExtension(type.SyntaxTree.FilePath),
					StringComparison.Ordinal);
		}

		private static void Emit(SourceProductionContext context, Inputs inputs)
		{
			// Members of the shared table arrive from many sheets, so their names are reconciled across
			// the whole assembly rather than per sheet.
			var globalNames = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach (var sheet in inputs.Sheets)
			{
				if (sheet.Classes.Count == 0)
					continue;

				var companionPath = Path.ChangeExtension(sheet.Sheet, ".cs");
				var companion = inputs.Companions.FirstOrDefault(candidate => Declarations.SamePath(candidate.FilePath, companionPath));

				if (companion is not null)
				{
					EmitComponent(context, sheet, companion.Type, inputs.Facts);
				}
				else if (inputs.Files.Any(file => Declarations.SamePath(file, companionPath)))
				{
					context.ReportDiagnostic(Diagnostic.Create(
						Diagnostics.CompanionTypeMissing,
						Location.None,
						sheet.Sheet,
						companionPath,
						Path.GetFileNameWithoutExtension(companionPath)));
				}
				else
				{
					EmitGlobal(context, sheet, globalNames, inputs.Facts);
				}
			}
		}

		private static void EmitComponent(
			SourceProductionContext context,
			SheetEntry sheet,
			DeclaredType type,
			CompilationFacts facts)
		{
			if (type.NotPartial.Count > 0)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					Diagnostics.StylesNotPartial,
					type.Location.ToLocation(),
					type.FullName,
					sheet.Sheet,
					string.Join(", ", type.NotPartial.Select(name => $"'{name}'"))));

				return;
			}

			var members = Map(context, sheet, new Dictionary<string, string>(StringComparer.Ordinal));

			if (members.Count == 0)
				return;

			var file = SourceBuilder.File(sheet.Sheet);
			var opened = file.OpenType(type);

			file.Line($"/// <summary>Class names declared in <c>{Path.GetFileName(sheet.Sheet)}</c>.</summary>");

			if (facts.HasNoAutoStaticsCleanup)
				file.Line($"[global::{NoAutoStaticsCleanup}]");

			file.Open($"private static class {ClassNames.StylesClass}");
			AppendMembers(file, members, string.Empty);
			file.Close();

			file.Close(opened);

			context.AddSource(HintName.For(type.FullName, ClassNames.StylesClass), file.ToString());
		}

		private static void EmitGlobal(
			SourceProductionContext context,
			SheetEntry sheet,
			Dictionary<string, string> globalNames,
			CompilationFacts facts)
		{
			var members = Map(context, sheet, globalNames);

			if (members.Count == 0)
				return;

			var file = SourceBuilder.File(sheet.Sheet);
			var opened = 0;

			if (sheet.Namespace.Length > 0)
			{
				file.Open($"namespace {sheet.Namespace}");
				opened++;
			}

			file.Line("/// <summary>Class names shared across the UI.</summary>");
			file.Open($"public static partial class {ClassNames.GlobalClass}");
			AppendMembers(file, members, facts.HasNoAutoStaticsCleanup ? $"[global::{NoAutoStaticsCleanup}] " : string.Empty);
			file.Close();

			file.Close(opened);

			context.AddSource(HintName.For(sheet.Sheet, ClassNames.GlobalClass), file.ToString());
		}

		/// <summary>
		/// Turns the sheet's CSS names into member names, dropping any that collide with one already
		/// taken in the same table.
		/// </summary>
		private static List<(string Member, string Css)> Map(
			SourceProductionContext context,
			SheetEntry sheet,
			Dictionary<string, string> taken)
		{
			var members = new List<(string Member, string Css)>(sheet.Classes.Count);

			foreach (var css in sheet.Classes)
			{
				var member = ClassNames.Identifier(css);

				if (taken.TryGetValue(member, out var existing))
				{
					if (!string.Equals(existing, css, StringComparison.Ordinal))
					{
						context.ReportDiagnostic(Diagnostic.Create(
							Diagnostics.ClassNameCollision,
							Location.None,
							css,
							existing,
							member,
							sheet.Sheet));
					}

					continue;
				}

				taken[member] = css;
				members.Add((member, css));
			}

			return members;
		}

		/// <remarks>
		/// The constants must survive Unity's static cleanup when entering Play Mode without a domain
		/// reload: a <c>ClassName</c> is an index into a process-wide intern table that is kept too, and
		/// a field reset to its default would point at the wrong class. A <c>Styles</c> table is marked
		/// once on the class; the shared <c>Ui</c> table marks each field, because it is a partial that
		/// spans every global sheet and may share a type with hand-written statics that should reset.
		/// </remarks>
		private static void AppendMembers(SourceBuilder file, List<(string Member, string Css)> members, string attribute)
		{
			foreach (var (member, css) in members)
			{
				var literal = SymbolDisplay.FormatLiteral(css, quote: true);

				file.Line($"{attribute}public static readonly global::ReactiveUI.ClassName {member} = global::ReactiveUI.ClassName.Intern({literal});");
			}
		}
	}

	internal sealed record CompanionType(string FilePath, DeclaredType Type);

	internal sealed record Inputs(
		EquatableArray<SheetEntry> Sheets,
		EquatableArray<CompanionType> Companions,
		EquatableArray<string> Files,
		CompilationFacts Facts);

	internal sealed record CompilationFacts(string AssemblyName, bool HasNoAutoStaticsCleanup);
}

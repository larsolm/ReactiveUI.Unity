using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace ReactiveUI.Editor
{
	/// <summary>
	/// Imports a <c>.css</c> file as a <see cref="CompiledStyleSheet"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is where CSS is parsed — once per file, when the file changes. Unity caches the result
	/// like any other import, so a domain reload, a branch switch back or a fresh checkout with a
	/// warm cache costs no parsing at all, and saving one sheet recompiles only that sheet.
	/// </para>
	/// <para>
	/// What the compiler has to say is reported against the asset, once per import, rather than on
	/// every domain reload. A file that does not parse still imports — as an empty sheet — so that
	/// fixing it is an ordinary reimport rather than a missing asset.
	/// </para>
	/// </remarks>
	[ScriptedImporter(version: 2, ext: "css")]
	internal sealed class CssImporter : ScriptedImporter
	{
		public override void OnImportAsset(AssetImportContext ctx)
		{
			ctx.DependsOnCustomDependency(CssCompilerVersion.Name);

			string css;

			try
			{
				css = File.ReadAllText(ctx.assetPath);
			}
			catch (Exception ex)
			{
				ctx.LogImportError($"[ReactiveUI] Could not read {ctx.assetPath}: {ex.Message}");
				css = string.Empty;
			}

			var result = CssCompiler.Compile(css, ctx.assetPath);
			var compiled = ScriptableObject.CreateInstance<CompiledStyleSheet>();

			compiled.Set(
				ctx.assetPath,
				result.Data,
				result.Classes,
				result.UnscopedClasses,
				result.Imports,
				result.Diagnostics.ToArray());

			ctx.AddObjectToAsset("stylesheet", compiled);
			ctx.SetMainObject(compiled);

			if (result.Sheet is null)
				ctx.LogImportError($"[ReactiveUI] {ctx.assetPath} could not be parsed and is empty.");

			foreach (var diagnostic in result.Diagnostics)
				ctx.LogImportWarning($"[ReactiveUI] {diagnostic}");
		}
	}

	/// <summary>
	/// The custom dependency every compiled sheet carries on the compiler that built it.
	/// </summary>
	/// <remarks>
	/// A compiled sheet holds enum values, keyword ids and property ids raw, so it is only valid for
	/// the build of the compiler and runtime that wrote it. Keying the import on both assemblies'
	/// identities makes Unity recompile every sheet whenever either changes — which in a project
	/// consuming the package is only on upgrade — and never otherwise.
	/// </remarks>
	[InitializeOnLoad]
	internal static class CssCompilerVersion
	{
		internal const string Name = "ReactiveUI/CssCompiler";

		private const string StampPath = "Library/ReactiveUI/CssCompiler.hash";

		static CssCompilerVersion()
		{
			var hash = new Hash128();
			hash.Append(StyleSheetSerializer.FormatVersion);
			hash.Append(typeof(StyleSheet).Assembly.ManifestModule.ModuleVersionId.ToString());
			hash.Append(typeof(CssCompiler).Assembly.ManifestModule.ModuleVersionId.ToString());

			AssetDatabase.RegisterCustomDependency(Name, hash);

			// Registering a new value does not reimport anything by itself; a refresh does. Only asked
			// for when the value actually moved, since a refresh on every domain reload is not free.
			var stamp = hash.ToString();
			string? previous = null;

			try
			{
				if (File.Exists(StampPath))
					previous = File.ReadAllText(StampPath);
			}
			catch (IOException)
			{
			}

			if (string.Equals(previous, stamp, StringComparison.Ordinal))
				return;

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(StampPath)!);
				File.WriteAllText(StampPath, stamp);
			}
			catch (IOException)
			{
			}

			EditorApplication.delayCall += () => AssetDatabase.Refresh();
		}
	}
}

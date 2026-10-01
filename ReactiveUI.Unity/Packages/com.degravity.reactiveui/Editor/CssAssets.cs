using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ReactiveUI.Editor
{
	/// <summary>
	/// Finds the project's stylesheets and puts them in the one canonical order.
	/// </summary>
	/// <remarks>
	/// Everything that consumes stylesheets — the live catalog, the build manifest, the class-name
	/// generator — goes through here, because the order <em>is</em> the cascade's document order. Two
	/// consumers disagreeing about it would mean two equally specific rules resolving differently
	/// depending on who asked, which is exactly the bug the manifest exists to close.
	/// </remarks>
	internal static class CssAssets
	{
		/// <summary>Every compiled sheet in the project, in cascade order.</summary>
		internal static List<CompiledStyleSheet> LoadAll()
		{
			var sheets = new List<CompiledStyleSheet>();

			// Not restricted to Assets/: FindAssets covers packages too, and a package that ships its
			// own sheets should style the same way in a build as it does in the editor.
			foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(CompiledStyleSheet)))
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);

				if (!path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
					continue;

				var sheet = AssetDatabase.LoadAssetAtPath<CompiledStyleSheet>(path);

				if (sheet != null)
					sheets.Add(sheet);
			}

			return Order(sheets);
		}

		/// <summary>Whether every <c>.css</c> in the project has been imported as a compiled sheet.</summary>
		internal static bool AllCompiled()
		{
			foreach (var path in AssetDatabase.GetAllAssetPaths())
			{
				if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
					&& AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(CompiledStyleSheet))
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Orders sheets so that every sheet comes after the sheets it imports, and by ordinal asset path
		/// otherwise.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The path order is what a project with no imports has always had, and it stays the tie-break:
		/// sheets are taken in path order, each preceded by whatever it imports that has not been placed
		/// yet. A sheet only moves when an import says it must, and then it moves to just before the
		/// first sheet that needs it.
		/// </para>
		/// <para>
		/// An import of a file that is not a stylesheet in the project, and an import cycle, are both
		/// named. A cycle is broken by path order, as if the import closing it had not been written.
		/// </para>
		/// </remarks>
		internal static List<CompiledStyleSheet> Order(IReadOnlyList<CompiledStyleSheet> sheets, bool report = true)
		{
			var byPath = new SortedDictionary<string, CompiledStyleSheet>(StringComparer.Ordinal);

			foreach (var sheet in sheets)
				byPath[SourcePath(sheet)] = sheet;

			var ordered = new List<CompiledStyleSheet>(byPath.Count);
			var state = new Dictionary<string, Visit>(StringComparer.Ordinal);

			foreach (var path in byPath.Keys)
				Place(path, byPath, state, ordered, new List<string>(), report);

			return ordered;
		}

		private enum Visit
		{
			InProgress,
			Done,
		}

		/// <summary>Places a sheet after everything it imports, depth first.</summary>
		/// <remarks>
		/// Visiting paths in ordinal order and each sheet's imports in ordinal order is what makes the
		/// result the path order whenever nothing forces otherwise.
		/// </remarks>
		private static void Place(
			string path,
			SortedDictionary<string, CompiledStyleSheet> byPath,
			Dictionary<string, Visit> state,
			List<CompiledStyleSheet> ordered,
			List<string> chain,
			bool report)
		{
			if (state.TryGetValue(path, out var visit))
			{
				if (visit == Visit.InProgress && report)
				{
					var start = chain.IndexOf(path);
					var cycle = string.Join(" → ", chain.GetRange(start, chain.Count - start)) + " → " + path;

					Debug.LogWarning($"[ReactiveUI] Stylesheet import cycle: {cycle}. The import closing it is ignored for ordering.");
				}

				return;
			}

			var sheet = byPath[path];
			state[path] = Visit.InProgress;
			chain.Add(path);

			var imports = new List<string>();

			foreach (var import in sheet.Imports)
			{
				if (byPath.ContainsKey(import.Path))
					imports.Add(import.Path);
				else if (report)
					Debug.LogWarning($"[ReactiveUI] {path} imports {import.Path}, which is not a stylesheet in the project.");
			}

			imports.Sort(StringComparer.Ordinal);

			foreach (var import in imports)
				Place(import, byPath, state, ordered, chain, report);

			chain.RemoveAt(chain.Count - 1);
			state[path] = Visit.Done;
			ordered.Add(sheet);
		}

		/// <summary>The sheet's asset path, which is what imports name it by.</summary>
		internal static string SourcePath(CompiledStyleSheet sheet)
		{
			var path = AssetDatabase.GetAssetPath(sheet);

			return string.IsNullOrEmpty(path) ? sheet.SourcePath : path;
		}
	}
}

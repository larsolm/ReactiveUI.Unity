using System;
using System.Collections.Generic;
using System.IO;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;

namespace ReactiveUI.Editor
{
	/// <summary>
	/// Loads every stylesheet in the project, and reloads one whenever it is reimported.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the whole hot-reload story, and it is why styles moved out of C# in the first place:
	/// a stylesheet is data, so changing one never triggers a domain reload. Edit, save, and the
	/// running game restyles — in Play Mode, with all its state intact.
	/// </para>
	/// <para>
	/// Nothing here parses CSS. Each file is compiled by <see cref="CssImporter"/> when it changes, and
	/// the catalog reads the result exactly as a player does, through <see cref="StyleSheetLibrary"/>.
	/// A save reloads only the sheets that were reimported; the rest stay as they were.
	/// </para>
	/// </remarks>
	[NoAutoStaticsCleanup]
	internal static class CssCatalog
	{
		private static StyleSheetLibrary s_library = null!;

		/// <summary>The sheets in cascade order, and what each loaded as.</summary>
		private static readonly List<CompiledStyleSheet> s_sources = new();

		private static readonly Dictionary<CompiledStyleSheet, StyleSheet?> s_loaded = new();

		/// <summary>The sheets in cascade order, as of the last load.</summary>
		internal static IReadOnlyList<CompiledStyleSheet> Sources => s_sources;

		[InitializeOnLoadMethod]
		private static void Initialise()
		{
			s_library = new StyleSheetLibrary();
			StyleSheets.SetSource(s_library);
			Reload(changed: null);

			// Writing assets while the domain is still loading is unreliable, and neither of these is
			// needed for the editor to render — they are what a build and the compiler read. Defer past
			// the reload.
			EditorApplication.delayCall += SyncGeneratedAssets;
		}

		/// <summary>Recompiles every stylesheet in the project.</summary>
		[MenuItem("Tools/ReactiveUI/Rebuild Stylesheets")]
		internal static void RebuildAll()
		{
			AssetDatabase.StartAssetEditing();

			try
			{
				foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(CompiledStyleSheet)))
					AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}

			// The reimports reach the watcher, but a batch-mode caller may quit before it would run.
			Reload(changed: null);
			SyncGeneratedAssets();
		}

		/// <summary>
		/// Brings the build manifest and the style class manifest — what the compiler generates the
		/// class-name constants from — back in step with the sheets that were just loaded.
		/// </summary>
		private static void SyncGeneratedAssets()
		{
			// While sheets are still on their way through the importer — the first load after an
			// upgrade, say — the list is incomplete, and writing it down would drop classes the code
			// still uses. The import that finishes the job comes back through the watcher.
			if (!CssAssets.AllCompiled())
				return;

			StyleSheetManifestBuilder.Rebuild(s_sources);
			StyleClassManifestWriter.Rebuild(s_sources);
		}

		/// <summary>
		/// Rebuilds the sheet list and hands it to the runtime.
		/// </summary>
		/// <param name="changed">
		/// The sheets that were reimported, which are read again; every other sheet keeps what it loaded
		/// as. Null reads everything.
		/// </param>
		internal static void Reload(HashSet<string>? changed)
		{
			var sources = CssAssets.LoadAll();
			var sheets = new List<StyleSheet?>(sources.Count);
			var live = new HashSet<CompiledStyleSheet>();

			foreach (var source in sources)
			{
				live.Add(source);

				if (changed is not null
					&& !changed.Contains(CssAssets.SourcePath(source))
					&& s_loaded.TryGetValue(source, out var kept))
				{
					sheets.Add(kept);

					continue;
				}

				var sheet = StyleSheetLibrary.TryLoad(source);

				s_loaded[source] = sheet;
				sheets.Add(sheet);
			}

			// A deleted asset leaves a destroyed key behind; drop it rather than let the cache grow.
			var stale = new List<CompiledStyleSheet>();

			foreach (var key in s_loaded.Keys)
			{
				if (key == null || !live.Contains(key))
					stale.Add(key!);
			}

			foreach (var key in stale)
				s_loaded.Remove(key);

			s_sources.Clear();
			s_sources.AddRange(sources);
			s_library.Set(sources, sheets);
		}

		private sealed class Watcher : AssetPostprocessor
		{
			private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] movedTo, string[] movedFrom)
			{
				if (s_library is null)
					return;

				var changed = new HashSet<string>(StringComparer.Ordinal);
				var any = Collect(imported, changed) | Collect(deleted, changed) | Collect(movedFrom, changed);

				// A compiled sheet records the path it was compiled at, and its imports were resolved
				// against it, so a moved sheet is compiled again where it now lives.
				var moved = new HashSet<string>(StringComparer.Ordinal);

				if (Collect(movedTo, moved))
				{
					foreach (var path in moved)
						AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

					any = true;
				}

				if (!any)
					return;

				Reload(changed);
				SyncGeneratedAssets();
			}

			private static bool Collect(IReadOnlyList<string> paths, HashSet<string> into)
			{
				var any = false;

				for (var i = 0; i < paths.Count; i++)
				{
					if (!string.Equals(Path.GetExtension(paths[i]), ".css", StringComparison.OrdinalIgnoreCase))
						continue;

					into.Add(paths[i]);
					any = true;
				}

				return any;
			}
		}
	}
}

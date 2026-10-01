using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ReactiveUI.Editor
{
	/// <summary>
	/// Keeps the <see cref="StyleSheetManifest"/> in step with the project's <c>.css</c> files.
	/// </summary>
	/// <remarks>
	/// Nothing about styling should need a serialized field maintained by hand. The editor already
	/// knows every sheet in the project — this writes that answer down in the one form a build can
	/// read, in the same order the editor cascades them.
	/// </remarks>
	internal static class StyleSheetManifestBuilder
	{
		private const string DefaultFolder = "Assets/Plugins/ReactiveUI/Resources/ReactiveUI";
		private const string AssetName = "StyleSheets.asset";

		[MenuItem("Tools/ReactiveUI/Rebuild Stylesheet Manifest")]
		internal static void Rebuild() => Rebuild(CssAssets.LoadAll());

		/// <param name="sheets">Every sheet in the project, already in cascade order.</param>
		public static void Rebuild(IReadOnlyList<CompiledStyleSheet> sheets)
		{
			var manifest = FindOrCreate();

			if (manifest == null)
				return;

			if (Matches(manifest.Sheets, sheets))
				return;

			var array = new CompiledStyleSheet[sheets.Count];

			for (var i = 0; i < array.Length; i++)
				array[i] = sheets[i];

			manifest.SetSheets(array);
			EditorUtility.SetDirty(manifest);
			AssetDatabase.SaveAssetIfDirty(manifest);
		}

		private static bool Matches(IReadOnlyList<CompiledStyleSheet> current, IReadOnlyList<CompiledStyleSheet> next)
		{
			if (current.Count != next.Count)
				return false;

			for (var i = 0; i < next.Count; i++)
			{
				if (current[i] != next[i])
					return false;
			}

			return true;
		}

		private static StyleSheetManifest? FindOrCreate()
		{
			var guids = AssetDatabase.FindAssets("t:" + nameof(StyleSheetManifest));

			if (guids.Length > 0)
			{
				var paths = new List<string>(guids.Length);

				foreach (var guid in guids)
					paths.Add(AssetDatabase.GUIDToAssetPath(guid));

				paths.Sort(StringComparer.Ordinal);

				if (paths.Count > 1)
				{
					Debug.LogWarning(
						$"[ReactiveUI] Found {paths.Count} style sheet manifests; using {paths[0]}. "
						+ "Delete the others — Resources.Load picks by path, so the extras are dead weight "
						+ "that still drags every sheet into the build.");
				}

				return AssetDatabase.LoadAssetAtPath<StyleSheetManifest>(paths[0]);
			}

			if (!EnsureFolder(DefaultFolder))
				return null;

			var manifest = ScriptableObject.CreateInstance<StyleSheetManifest>();
			AssetDatabase.CreateAsset(manifest, DefaultFolder + "/" + AssetName);

			return manifest;
		}

		private static bool EnsureFolder(string folder)
		{
			var segments = folder.Split('/');
			var current = segments[0];

			for (var i = 1; i < segments.Length; i++)
			{
				var next = current + "/" + segments[i];

				if (!AssetDatabase.IsValidFolder(next))
				{
					var guid = AssetDatabase.CreateFolder(current, segments[i]);

					if (string.IsNullOrEmpty(guid))
					{
						Debug.LogError($"[ReactiveUI] Could not create {next} for the style sheet manifest.");

						return false;
					}
				}

				current = next;
			}

			return true;
		}
	}
}

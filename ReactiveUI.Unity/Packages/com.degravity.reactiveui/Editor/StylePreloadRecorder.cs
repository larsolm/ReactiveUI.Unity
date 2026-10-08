using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;

namespace ReactiveUI.Editor
{
	/// <summary>
	/// Records into the <see cref="StylePreloadManifest"/> every sheet, font and texture a root's tree
	/// loads in Play mode, so the next run loads them when the root starts.
	/// </summary>
	[NoAutoStaticsCleanup]
	internal sealed class StylePreloadRecorder : IStylePreloadRecorder
	{
		private const string AssetName = "StylePreloads.asset";

		private static StylePreloadRecorder? s_instance;

		private readonly HashSet<(string Root, PreloadKind Kind, string Path)> _seen = new();

		private readonly Dictionary<(string Root, string Font), HashSet<int>> _seenCharacters = new();

		/// <summary>Roots first recorded this session, which are announced once rather than per item.</summary>
		private readonly HashSet<string> _newRoots = new();

		private StylePreloadManifest? _pendingSave;

		[InitializeOnLoadMethod]
		private static void Initialise()
		{
			s_instance = new StylePreloadRecorder();
			StylePreloads.Recorder = s_instance;

			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
		}

		/// <summary>Forgets what this session saw, so an entry removed by hand is recorded again.</summary>
		private static void OnPlayModeStateChanged(PlayModeStateChange change)
		{
			if (change != PlayModeStateChange.EnteredPlayMode || s_instance is null)
				return;

			s_instance._seen.Clear();
			s_instance._seenCharacters.Clear();
			s_instance._newRoots.Clear();
		}

		public void Record(string root, PreloadKind kind, string path)
		{
			if (!_seen.Add((root, kind, path)))
				return;

			var manifest = StylePreloads.Manifest ?? Create();

			if (manifest == null || !manifest.Add(root, kind, path, out var newRoot))
				return;

			EditorUtility.SetDirty(manifest);
			QueueSave(manifest);

			if (newRoot)
			{
				_newRoots.Add(root);
				Debug.Log(
					$"[ReactiveUI] Recording the stylesheets, fonts and textures {root} loads into "
					+ $"{AssetDatabase.GetAssetPath(manifest)}. Commit it: from the next run, {root} loads them when it starts.");

				return;
			}

			if (_newRoots.Contains(root))
				return;

			Debug.LogWarning(
				$"[ReactiveUI] {root} loaded the {Describe(kind)} {path} after it started, which can hitch. "
				+ $"Recorded in {AssetDatabase.GetAssetPath(manifest)}; from the next run {root} loads it when it starts.");
		}

		public void RecordCharacters(string root, string font, string text)
		{
			if (!_seenCharacters.TryGetValue((root, font), out var seen))
			{
				seen = new HashSet<int>();
				_seenCharacters[(root, font)] = seen;
			}

			List<int>? fresh = null;

			foreach (var codePoint in StylePreloadManifest.CodePoints(text))
			{
				// Control characters never reach the atlas.
				if (codePoint >= 0x20 && seen.Add(codePoint))
					(fresh ??= new List<int>()).Add(codePoint);
			}

			if (fresh is null)
				return;

			var manifest = StylePreloads.Manifest ?? Create();

			if (manifest == null || !manifest.AddCharacters(root, font, fresh, out var newRoot))
				return;

			if (newRoot)
				_newRoots.Add(root);

			EditorUtility.SetDirty(manifest);
			QueueSave(manifest);
		}

		private static string Describe(PreloadKind kind) => kind switch
		{
			PreloadKind.Sheet => "stylesheet",
			PreloadKind.Font => "font",
			_ => "texture",
		};

		/// <summary>Saves once per editor update, however many paths were recorded in it.</summary>
		private void QueueSave(StylePreloadManifest manifest)
		{
			if (_pendingSave != null)
				return;

			_pendingSave = manifest;
			EditorApplication.delayCall += Save;
		}

		private void Save()
		{
			if (_pendingSave != null)
				AssetDatabase.SaveAssetIfDirty(_pendingSave);

			_pendingSave = null;
		}

		private static StylePreloadManifest? Create()
		{
			if (!StyleSheetManifestBuilder.EnsureFolder(StyleSheetManifestBuilder.DefaultFolder))
				return null;

			var manifest = ScriptableObject.CreateInstance<StylePreloadManifest>();
			AssetDatabase.CreateAsset(manifest, StyleSheetManifestBuilder.DefaultFolder + "/" + AssetName);
			StylePreloads.Manifest = manifest;

			return manifest;
		}
	}
}

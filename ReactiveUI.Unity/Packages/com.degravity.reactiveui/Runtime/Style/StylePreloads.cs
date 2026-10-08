using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>What a root can load ahead of time.</summary>
	internal enum PreloadKind
	{
		Sheet,
		Font,
		Texture,
	}

	/// <summary>Writes down what a root loaded, so the next run can load it at start.</summary>
	internal interface IStylePreloadRecorder
	{
		void Record(string root, PreloadKind kind, string path);

		/// <summary>Records that <paramref name="font"/> rendered <paramref name="text"/> under <paramref name="root"/>.</summary>
		void RecordCharacters(string root, string font, string text);
	}

	/// <summary>
	/// The recorded preloads, and who to tell about anything a root loads on demand.
	/// </summary>
	// The editor sets the recorder from [InitializeOnLoadMethod], which does not re-run on entering Play mode.
	[NoAutoStaticsCleanup]
	internal static class StylePreloads
	{
		private static StylePreloadManifest? s_manifest;
		private static bool s_looked;

		/// <summary>The recorded manifest, or null if there is none.</summary>
		internal static StylePreloadManifest? Manifest
		{
			get
			{
				if (!s_looked)
				{
					s_manifest = Resources.Load<StylePreloadManifest>(StylePreloadManifest.ResourcePath);
					s_looked = true;
				}

				return s_manifest;
			}
			set
			{
				s_manifest = value;
				s_looked = true;
			}
		}

		/// <summary>Set in the editor; null in a player, where nothing is recorded.</summary>
		internal static IStylePreloadRecorder? Recorder { get; set; }

		/// <summary>The root whose runtime is updating, which anything loaded now is recorded against.</summary>
		internal static string? Current { get; set; }

		/// <summary>
		/// Loads the sheets, fonts and textures recorded in <paramref name="entry"/> into the caches every
		/// runtime shares.
		/// </summary>
		internal static void Warm(StylePreloadManifest.Entry entry, IStyleSheetSource? source)
		{
			using var marker = UiMarkers.Preload.Auto();

			if (source is not null)
			{
				foreach (var path in entry.Sheets)
				{
					var slot = source.SlotOf(path);

					if (slot >= 0)
						source.Get(slot);
				}
			}

			foreach (var path in entry.Fonts)
				UiFonts.Preload(path);

			foreach (var characters in entry.Characters)
				UiFonts.AddCharacters(characters.Font, characters.Characters);

			foreach (var path in entry.Textures)
				UiTextures.Resolve(path);
		}

		/// <summary>Whether anything noted now is recorded.</summary>
		internal static bool Recording => Recorder is not null && Current is not null;

		/// <summary>Records that the current root used <paramref name="path"/>.</summary>
		internal static void Note(PreloadKind kind, string? path)
		{
			if (Recorder is null || Current is null || string.IsNullOrEmpty(path))
				return;

			Recorder.Record(Current, kind, path!);
		}

		/// <summary>Records that the current root rendered <paramref name="text"/> in the font at <paramref name="font"/>.</summary>
		internal static void NoteCharacters(string? font, string? text)
		{
			if (Recorder is null || Current is null || string.IsNullOrEmpty(font) || string.IsNullOrEmpty(text))
				return;

			Recorder.RecordCharacters(Current, font!, text!);
		}
	}
}

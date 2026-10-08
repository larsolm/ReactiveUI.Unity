using System;
using System.Collections.Generic;
using TMPro;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Maps CSS <c>font-family</c> names and weights to TextMeshPro font assets.
	/// </summary>
	/// <remarks>
	/// Fonts declared with <c>@font-face</c> are registered automatically, and loaded when a root that
	/// recorded them starts or else on first use.
	/// </remarks>
	// Only ever repopulated by a sheet rebuild, which entering Play mode does not trigger.
	[NoAutoStaticsCleanup]
	public static partial class UiFonts
	{
		/// <summary>
		/// The weight of a normal face, 400.
		/// </summary>
		public const int NormalWeight = 400;

		/// <summary>A registered face, or a declared one whose asset loads on first use.</summary>
		private struct Face
		{
			public int Weight;
			public TMP_FontAsset? Font;
			public string? ResourcePath;
			public string? DeclaredIn;
		}

		private static readonly Dictionary<string, List<Face>> s_families = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>The <c>Resources</c> path each loaded declared face came from.</summary>
		private static readonly Dictionary<TMP_FontAsset, string> s_paths = new();

		/// <summary>Fonts whose lookup tables have been built.</summary>
		private static readonly HashSet<TMP_FontAsset> s_warmed = new();

		// Resolution happens per restyle rather than once per sheet, so an unregistered family would
		// otherwise warn every frame it is styled.
		private static readonly HashSet<(string Family, int Weight)> s_warned = new();

		/// <summary>
		/// Registers <paramref name="font"/> as the face of <paramref name="family"/> at <paramref name="weight"/>.
		/// </summary>
		/// <remarks>
		/// A family resolves to its registered face nearest the requested weight.
		/// </remarks>
		public static void Register(string family, TMP_FontAsset font, int weight = NormalWeight)
		{
			if (string.IsNullOrEmpty(family) || font == null)
				return;

			Add(family, new Face { Weight = weight, Font = font });
		}

		/// <summary>
		/// Declares the face of <paramref name="family"/> at <paramref name="weight"/> as the font asset
		/// at <paramref name="resourcePath"/> under a <c>Resources</c> folder, loaded when first resolved.
		/// </summary>
		internal static void Declare(string family, string resourcePath, int weight, string declaredIn)
		{
			if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(resourcePath))
				return;

			Add(family, new Face { Weight = weight, ResourcePath = resourcePath, DeclaredIn = declaredIn });
		}

		private static void Add(string family, Face face)
		{
			if (!s_families.TryGetValue(family, out var faces))
			{
				faces = new List<Face>(1);
				s_families[family] = faces;
			}

			for (var i = 0; i < faces.Count; i++)
			{
				if (faces[i].Weight != face.Weight)
					continue;

				faces[i] = face;

				return;
			}

			faces.Add(face);
		}

		/// <summary>
		/// Resolves a family at a weight, warning once rather than silently rendering in the wrong
		/// face. A family with no face at that exact weight falls back to its nearest one, so a sheet
		/// that never mentions <c>font-weight</c> keeps working against a single registered face.
		/// </summary>
		internal static TMP_FontAsset? Resolve(string? family, int weight = NormalWeight)
		{
			if (string.IsNullOrEmpty(family))
				return null;

			if (s_families.TryGetValue(family!, out var faces))
			{
				// A declared face whose asset is missing is dropped, and the next nearest tried.
				while (faces.Count > 0)
				{
					var best = 0;

					for (var i = 1; i < faces.Count; i++)
					{
						if (Mathf.Abs(faces[i].Weight - weight) < Mathf.Abs(faces[best].Weight - weight))
							best = i;
					}

					var face = faces[best];

					if (face.Font != null)
					{
						StylePreloads.Note(PreloadKind.Font, face.ResourcePath);

						return face.Font;
					}

					face.Font = Load(face.ResourcePath!);

					if (face.Font != null)
					{
						faces[best] = face;
						StylePreloads.Note(PreloadKind.Font, face.ResourcePath);

						return face.Font;
					}

					Debug.LogWarning($"[ReactiveUI] {face.DeclaredIn}: no font asset at Resources/{face.ResourcePath}.");
					faces.RemoveAt(best);
				}
			}

			if (s_warned.Add((family, weight)))
			{
				Debug.LogWarning(
					$"[ReactiveUI] No font registered for font-family '{family}' at weight {weight}. "
					+ "Declare it with @font-face, or call UiFonts.Register.");
			}

			return null;
		}

		/// <summary>
		/// Whether <paramref name="font"/> carries its own typeface for <paramref name="weight"/> in
		/// TextMeshPro's <b>Font Weights</b> table.
		/// </summary>
		/// <remarks>
		/// This is the question "would TextMeshPro swap typeface if it were handed this weight",
		/// and it is what decides whether <c>TextHost</c> hands it over at all. An asset with an
		/// entry means the family spells its weights through the table, and TMP does the work — at
		/// the cost of a second material and so a sub-mesh object per text node. No entry means
		/// either the family registered a face per weight through <c>@font-face</c>, in which case
		/// <see cref="Resolve"/> has already picked it, or there is no such face anywhere and
		/// asking would send every glyph down TMP's missing-character path.
		/// </remarks>
		internal static bool HasWeightFace(TMP_FontAsset? font, int weight)
		{
			// TMP indexes the table by hundreds — Thin at 1 through Black at 9 — and 400 is the
			// base face rather than a row in it.
			if (font == null || weight == NormalWeight)
				return false;

			var index = weight / 100;
			var table = font.fontWeightTable;

			return index >= 1 && table != null && index < table.Length && table[index].regularTypeface != null;
		}

		/// <summary>
		/// Loads every declared face at <paramref name="resourcePath"/> that has not loaded yet, and has
		/// TextMeshPro build the asset's lookup tables.
		/// </summary>
		/// <remarks>A face whose asset is missing is left for <see cref="Resolve"/> to report.</remarks>
		internal static void Preload(string resourcePath) => PreloadFont(resourcePath);

		private static TMP_FontAsset? PreloadFont(string resourcePath)
		{
			TMP_FontAsset? font = null;

			foreach (var faces in s_families.Values)
			{
				for (var i = 0; i < faces.Count; i++)
				{
					var face = faces[i];

					if (!string.Equals(face.ResourcePath, resourcePath, StringComparison.Ordinal))
						continue;

					if (face.Font != null)
					{
						font = face.Font;

						continue;
					}

					font ??= Load(resourcePath);

					if (font == null)
						return null;

					face.Font = font;
					faces[i] = face;
				}
			}

			if (font != null && s_warmed.Add(font))
			{
				using (UiMarkers.WarmFont.Auto())
					font.ReadFontAssetDefinition();
			}

			return font;
		}

		/// <summary>
		/// Adds <paramref name="characters"/> to the atlas of the declared font at
		/// <paramref name="resourcePath"/>, or of its fallbacks for any it does not have.
		/// </summary>
		internal static void AddCharacters(string resourcePath, string characters)
		{
			if (!string.IsNullOrEmpty(characters) && PreloadFont(resourcePath) is { } font)
				AddCharacters(font, characters);
		}

		/// <summary>
		/// Adds <paramref name="characters"/> to the atlas of <paramref name="font"/>, or of its fallbacks
		/// for any it does not have.
		/// </summary>
		/// <remarks>A static atlas takes nothing, and is left as it is.</remarks>
		internal static void AddCharacters(TMP_FontAsset font, string characters)
		{
			var unicodes = new List<uint>(characters.Length);

			foreach (var codePoint in StylePreloadManifest.CodePoints(characters))
				unicodes.Add((uint)codePoint);

			using var marker = UiMarkers.WarmFont.Auto();

			if (TryAdd(font, unicodes.ToArray(), out var missing) || font.fallbackFontAssetTable is not { } fallbacks)
				return;

			foreach (var fallback in fallbacks)
			{
				if (fallback != null && TryAdd(fallback, missing, out missing))
					return;
			}
		}

		/// <summary>Adds what it can of <paramref name="unicodes"/>, and says whether nothing is left.</summary>
		private static bool TryAdd(TMP_FontAsset font, uint[] unicodes, out uint[] missing)
		{
			if (font.atlasPopulationMode == AtlasPopulationMode.Static)
			{
				missing = unicodes;

				return false;
			}

			font.TryAddCharacters(unicodes, out missing);

			return missing is not { Length: > 0 };
		}

		/// <summary>The <c>Resources</c> path <paramref name="font"/> was loaded from, or null if it was registered directly.</summary>
		internal static string? PathOf(TMP_FontAsset? font) =>
			font != null && s_paths.TryGetValue(font, out var path) ? path : null;

		private static TMP_FontAsset? Load(string resourcePath)
		{
			TMP_FontAsset? font;

			using (UiMarkers.LoadFont.Auto())
				font = Resources.Load<TMP_FontAsset>(resourcePath);

			if (font != null)
				s_paths[font] = resourcePath;

			return font;
		}

		/// <summary>Whether a declared face at <paramref name="resourcePath"/> has its asset loaded.</summary>
		internal static bool IsLoaded(string resourcePath)
		{
			foreach (var faces in s_families.Values)
			{
				foreach (var face in faces)
				{
					if (face.Font != null && string.Equals(face.ResourcePath, resourcePath, StringComparison.Ordinal))
						return true;
				}
			}

			return false;
		}

		internal static void Clear()
		{
			s_families.Clear();
			s_paths.Clear();
			s_warmed.Clear();
			s_warned.Clear();
		}
	}

	/// <summary>
	/// Maps <c>background-image: resource("…")</c> paths to textures.
	/// </summary>
	/// <remarks>
	/// Unregistered paths are loaded from <c>Resources</c>.
	/// </remarks>
	public static class UiTextures
	{
		[NoAutoStaticsCleanup]
		private static readonly Dictionary<string, Texture?> s_textures = new(StringComparer.Ordinal);

		/// <summary>
		/// Registers <paramref name="texture"/> for <paramref name="path"/>.
		/// </summary>
		public static void Register(string path, Texture texture)
		{
			if (!string.IsNullOrEmpty(path))
				s_textures[path] = texture;
		}

		internal static Texture? Resolve(string? path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			StylePreloads.Note(PreloadKind.Texture, path);

			// A miss is cached as null too: the warning has already been logged and re-loading a
			// missing asset every restyle would cost more than the entry.
			if (s_textures.TryGetValue(path!, out var cached))
				return cached;

			using var marker = UiMarkers.LoadTexture.Auto();

			var texture = Resources.Load<Texture>(path!);

			// Sprite importers are the common case for UI art, and a sprite's texture is the right
			// thing to paint as long as it is not packed into an atlas.
			if (texture == null)
			{
				var sprite = Resources.Load<Sprite>(path!);
				if (sprite != null) texture = sprite.texture;
			}

			if (texture == null)
			{
				Debug.LogWarning($"[ReactiveUI] No texture at Resources/{path} for background-image.");
			}

			s_textures[path!] = texture;

			return texture;
		}

		internal static void Clear()
		{
			s_textures.Clear();
		}
	}
}

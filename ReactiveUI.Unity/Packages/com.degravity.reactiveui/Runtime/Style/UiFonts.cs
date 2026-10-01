using System;
using System.Collections.Generic;
using TMPro;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Maps a CSS <c>font-family</c> and <c>font-weight</c> onto a TextMeshPro asset.
	/// </summary>
	/// <remarks>
	/// Stylesheets name fonts as strings, so something has to hold the mapping. Populate it from
	/// <c>@font-face</c> rules, or register directly from C# when a font comes from somewhere
	/// Resources cannot reach.
	/// <para>
	/// A family may hold one face per weight, because a TMP asset is a baked atlas of one face and
	/// there is no synthetic bolding worth having. Registering without a weight files the face under
	/// 400. A family whose asset carries its own TMP weight table needs only that one registration —
	/// the text host hands the weight to TextMeshPro too, which swaps the typeface from the table.
	/// </para>
	/// </remarks>
	// Only ever repopulated by a sheet rebuild, which entering Play mode does not trigger.
	[NoAutoStaticsCleanup]
	public static partial class UiFonts
	{
		public const int NormalWeight = 400;

		private static readonly Dictionary<string, List<(int Weight, TMP_FontAsset Font)>> s_families =
			new(StringComparer.OrdinalIgnoreCase);

		// Resolution happens per restyle rather than once per sheet, so an unregistered family would
		// otherwise warn every frame it is styled.
		private static readonly HashSet<(string Family, int Weight)> s_warned = new();

		public static void Register(string family, TMP_FontAsset font, int weight = NormalWeight)
		{
			if (string.IsNullOrEmpty(family) || font == null)
				return;

			if (!s_families.TryGetValue(family, out var faces))
			{
				faces = new List<(int, TMP_FontAsset)>(1);
				s_families[family] = faces;
			}

			for (var i = 0; i < faces.Count; i++)
			{
				if (faces[i].Weight != weight)
					continue;

				faces[i] = (weight, font);

				return;
			}

			faces.Add((weight, font));
		}

		/// <summary>
		/// Resolves a family at a weight, warning once rather than silently rendering in the wrong
		/// face. A family with no face at that exact weight falls back to its nearest one, so a sheet
		/// that never mentions <c>font-weight</c> keeps working against a single registered face.
		/// </summary>
		public static TMP_FontAsset? Resolve(string? family, int weight = NormalWeight)
		{
			if (string.IsNullOrEmpty(family))
				return null;

			if (s_families.TryGetValue(family!, out var faces) && faces.Count > 0)
			{
				var best = faces[0];

				for (var i = 1; i < faces.Count; i++)
				{
					if (Mathf.Abs(faces[i].Weight - weight) < Mathf.Abs(best.Weight - weight))
						best = faces[i];
				}

				return best.Font;
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
		public static bool HasWeightFace(TMP_FontAsset? font, int weight)
		{
			// TMP indexes the table by hundreds — Thin at 1 through Black at 9 — and 400 is the
			// base face rather than a row in it.
			if (font == null || weight == NormalWeight)
				return false;

			var index = weight / 100;
			var table = font.fontWeightTable;

			return index >= 1 && table != null && index < table.Length && table[index].regularTypeface != null;
		}

		internal static void Clear()
		{
			s_families.Clear();
			s_warned.Clear();
		}
	}

	/// <summary>
	/// Maps a <c>background-image: resource("…")</c> path onto a texture.
	/// </summary>
	/// <remarks>
	/// Lives beside <see cref="UiFonts"/> because it is the same job — a CSS name resolved to a
	/// Unity asset — and because a style is applied far more often than a sheet is parsed, so the
	/// lookup has to be cached rather than hitting <c>Resources.Load</c> per restyle.
	/// </remarks>
	public static class UiTextures
	{
		[NoAutoStaticsCleanup]
		private static readonly Dictionary<string, Texture?> s_textures = new(StringComparer.Ordinal);

		public static void Register(string path, Texture texture)
		{
			if (!string.IsNullOrEmpty(path))
				s_textures[path] = texture;
		}

		public static Texture? Resolve(string? path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			// A miss is cached as null too: the warning has already been logged and re-loading a
			// missing asset every restyle would cost more than the entry.
			if (s_textures.TryGetValue(path!, out var cached))
				return cached;

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

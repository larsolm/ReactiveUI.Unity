using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Loads compiled stylesheets. This is the source a built player uses, and the editor's catalog
	/// goes through the same linking step.
	/// </summary>
	/// <remarks>
	/// Sheets arrive compiled — the editor builds each <c>.css</c> once, when it is imported — so loading
	/// is a read, not a parse. What is left is the part that depends on the whole set rather than on any
	/// one file: registering fonts, and ranking cascade layers, whose order is decided by every sheet
	/// that names them.
	/// </remarks>
	public sealed class StyleSheetLibrary : IStyleSheetSource
	{
		private readonly List<StyleSheet> _sheets = new();

		public event Action? Changed;

		IReadOnlyList<StyleSheet> IStyleSheetSource.Sheets => _sheets;

		/// <summary>
		/// Loads the given sheets, replacing anything loaded before. The order given is the cascade's
		/// document order.
		/// </summary>
		public void Load(IReadOnlyList<CompiledStyleSheet> sources)
		{
			var sheets = new List<StyleSheet?>(sources.Count);

			foreach (var source in sources)
				sheets.Add(TryLoad(source));

			Set(sources, sheets);
		}

		/// <summary>Reads one compiled sheet, or logs why it cannot be read.</summary>
		internal static StyleSheet? TryLoad(CompiledStyleSheet? source)
		{
			if (source == null || !source.HasData)
				return null;

			try
			{
				return source.Load();
			}
			catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
			{
				Debug.LogWarning($"[ReactiveUI] {ex.Message}");

				return null;
			}
		}

		/// <summary>
		/// Installs already-loaded sheets, parallel to <paramref name="sources"/>; a null sheet is one that
		/// failed to load and is skipped.
		/// </summary>
		internal void Set(IReadOnlyList<CompiledStyleSheet> sources, IReadOnlyList<StyleSheet?> sheets)
		{
			_sheets.Clear();
			UiFonts.Clear();
			UiTextures.Clear();

			var diagnostics = new List<string>();
			var loadedSources = new List<CompiledStyleSheet>(sources.Count);

			for (var i = 0; i < sheets.Count; i++)
			{
				if (sheets[i] is not { } sheet)
					continue;

				_sheets.Add(sheet);
				loadedSources.Add(sources[i]);
			}

			// Fonts are registered across every sheet before anything is styled, so a face declared in
			// one file can be used from another regardless of order.
			foreach (var sheet in _sheets)
				RegisterFonts(sheet, diagnostics);

			CascadeLayers.Rank(loadedSources, _sheets, diagnostics);

			foreach (var diagnostic in diagnostics)
				Debug.LogWarning($"[ReactiveUI] {diagnostic}");

			Changed?.Invoke();
		}

		private static void RegisterFonts(StyleSheet sheet, List<string> diagnostics)
		{
			foreach (var face in sheet.FontFaces)
			{
				var asset = Resources.Load<TMP_FontAsset>(face.ResourcePath);

				if (asset == null)
				{
					diagnostics.Add($"{sheet.Name}: no font asset at Resources/{face.ResourcePath}.");

					continue;
				}

				UiFonts.Register(face.Family, asset, face.Weight);
			}
		}
	}
}

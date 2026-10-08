using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Loads compiled stylesheets. This is the source a built player uses, and the editor's catalog
	/// goes through the same linking step.
	/// </summary>
	/// <remarks>
	/// Linking reads only each sheet's metadata: it ranks cascade layers and declares fonts. An eager
	/// sheet is read when an engine takes the library; any other is read the first time a node carries
	/// one of its classes, and font assets load the first time a style resolves them.
	/// </remarks>
	internal sealed class StyleSheetLibrary : IStyleSheetSource
	{
		private CompiledStyleSheet[] _sources = Array.Empty<CompiledStyleSheet>();
		private StyleSheet?[] _sheets = Array.Empty<StyleSheet?>();
		private bool[] _read = Array.Empty<bool>();
		private int[][] _layerRanks = Array.Empty<int[]>();
		private readonly Dictionary<int, List<int>> _lazyByClass = new();
		private readonly Dictionary<string, int> _slotsByPath = new(StringComparer.Ordinal);

		public event Action? Changed;

		public int Count => _sources.Length;

		/// <summary>The number of sheets read so far.</summary>
		internal int ReadCount
		{
			get
			{
				var count = 0;

				foreach (var sheet in _sheets)
				{
					if (sheet is not null)
						count++;
				}

				return count;
			}
		}

		public StyleSheet? Get(int slot)
		{
			if (!_read[slot])
			{
				_read[slot] = true;

				using (UiMarkers.ReadSheet.Auto())
					Install(slot, TryLoad(_sources[slot]));
			}

			return _sheets[slot];
		}

		public bool IsEager(int slot) => _sources[slot].Eager;

		public IReadOnlyList<int>? LazySlotsFor(int classId) => _lazyByClass.TryGetValue(classId, out var slots) ? slots : null;

		public string PathOf(int slot) => _sources[slot].SourcePath;

		public int SlotOf(string path) => _slotsByPath.TryGetValue(path, out var slot) ? slot : -1;

		/// <summary>
		/// Loads the given sheets, replacing anything loaded before. The order given is the cascade's
		/// document order.
		/// </summary>
		public void Load(IReadOnlyList<CompiledStyleSheet> sources) => Set(sources, null);

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
		/// Installs <paramref name="sources"/>, with <paramref name="sheets"/> already read in parallel
		/// to them, or null to read each on demand. A null sheet is one that failed to load.
		/// </summary>
		internal void Set(IReadOnlyList<CompiledStyleSheet> sources, IReadOnlyList<StyleSheet?>? sheets)
		{
			var kept = new List<CompiledStyleSheet>(sources.Count);
			var keptSheets = new List<StyleSheet?>(sources.Count);

			for (var i = 0; i < sources.Count; i++)
			{
				if (sources[i] == null)
					continue;

				kept.Add(sources[i]);
				keptSheets.Add(sheets?[i]);
			}

			_sources = kept.ToArray();
			_sheets = new StyleSheet?[_sources.Length];
			_read = new bool[_sources.Length];
			_lazyByClass.Clear();
			_slotsByPath.Clear();
			UiFonts.Clear();
			UiTextures.Clear();

			var diagnostics = new List<string>();

			_layerRanks = CascadeLayers.Rank(_sources, diagnostics);

			for (var slot = 0; slot < _sources.Length; slot++)
			{
				var source = _sources[slot];

				_slotsByPath[source.SourcePath] = slot;

				// Declared across every sheet before anything is styled, so a face declared in one file
				// can be used from another regardless of order or of which sheets are read.
				foreach (var font in source.Fonts)
					UiFonts.Declare(font.Family, font.ResourcePath, font.Weight, source.SourcePath);

				if (!source.Eager)
				{
					foreach (var css in source.Classes)
					{
						var id = ClassName.Intern(css)._id;

						if (!_lazyByClass.TryGetValue(id, out var slots))
						{
							slots = new List<int>(1);
							_lazyByClass[id] = slots;
						}

						slots.Add(slot);
					}
				}

				if (sheets is not null)
				{
					_read[slot] = true;
					Install(slot, keptSheets[slot]);
				}
			}

			foreach (var diagnostic in diagnostics)
				Debug.LogWarning($"[ReactiveUI] {diagnostic}");

			Changed?.Invoke();
		}

		private void Install(int slot, StyleSheet? sheet)
		{
			if (sheet is null)
				return;

			sheet.LayerRanks = _layerRanks[slot];
			_sheets[slot] = sheet;
		}
	}
}

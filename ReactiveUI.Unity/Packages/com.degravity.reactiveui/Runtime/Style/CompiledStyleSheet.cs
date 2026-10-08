using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// The imported asset for a <c>.css</c> file.
	/// </summary>
	public sealed class CompiledStyleSheet : ScriptableObject
	{
		/// <summary>One <c>@import</c>, resolved to the asset it names.</summary>
		[Serializable]
		internal struct Import
		{
			public string Path;

			/// <summary>The layer the import places the sheet in, or empty for none.</summary>
			/// <remarks>An anonymous <c>layer</c> is given a name unique to the import, so it is never empty when set.</remarks>
			public string Layer;

			public Import(string path, string layer)
			{
				Path = path;
				Layer = layer;
			}
		}

		/// <summary>One <c>@font-face</c>, readable without loading the sheet.</summary>
		[Serializable]
		internal struct Font
		{
			public string Family;
			public string ResourcePath;
			public int Weight;

			public Font(string family, string resourcePath, int weight)
			{
				Family = family;
				ResourcePath = resourcePath;
				Weight = weight;
			}
		}

		[SerializeField, HideInInspector]
		private byte[] _data = Array.Empty<byte>();

		[SerializeField]
		private bool _eager = true;

		[SerializeField]
		private string[] _layerNames = Array.Empty<string>();

		[SerializeField]
		private Font[] _fonts = Array.Empty<Font>();

		[SerializeField]
		private string _sourcePath = string.Empty;

		[SerializeField]
		private string[] _classes = Array.Empty<string>();

		[SerializeField]
		private string[] _unscopedClasses = Array.Empty<string>();

		[SerializeField]
		private Import[] _imports = Array.Empty<Import>();

		[SerializeField]
		private string[] _diagnostics = Array.Empty<string>();

		/// <summary>The asset path the sheet was compiled from.</summary>
		internal string SourcePath => _sourcePath;

		/// <summary>Every class a selector in the sheet names, sorted.</summary>
		internal IReadOnlyList<string> Classes => _classes;

		/// <summary>
		/// Classes the sheet styles with nothing narrowing them — a rule that is a single class, outside
		/// any <c>@scope</c> — in the order the sheet first does so.
		/// </summary>
		internal IReadOnlyList<string> UnscopedClasses => _unscopedClasses;

		/// <summary>What the compiler had to say about the file.</summary>
		internal IReadOnlyList<string> Diagnostics => _diagnostics;

		internal IReadOnlyList<Import> Imports => _imports;

		/// <summary>
		/// Whether the sheet is loaded up front. A sheet that is not is loaded once a node carries one
		/// of its <see cref="Classes"/>.
		/// </summary>
		internal bool Eager => _eager;

		/// <summary>The sheet's <c>@layer</c> names, in the order it first mentions them.</summary>
		internal IReadOnlyList<string> LayerNames => _layerNames;

		/// <summary>The sheet's <c>@font-face</c> rules.</summary>
		internal IReadOnlyList<Font> Fonts => _fonts;

		/// <summary>Whether the file compiled; one that did not loads as an empty sheet.</summary>
		internal bool HasData => _data.Length > 0;

		internal void Set(
			string sourcePath,
			byte[] data,
			string[] classes,
			string[] unscopedClasses,
			Import[] imports,
			string[] diagnostics,
			bool eager,
			string[] layerNames,
			Font[] fonts)
		{
			_sourcePath = sourcePath;
			_data = data;
			_classes = classes;
			_unscopedClasses = unscopedClasses;
			_imports = imports;
			_diagnostics = diagnostics;
			_eager = eager;
			_layerNames = layerNames;
			_fonts = fonts;
		}

		/// <summary>Reads the compiled sheet back.</summary>
		internal StyleSheet Load() => StyleSheetSerializer.Read(_data, string.IsNullOrEmpty(_sourcePath) ? name : _sourcePath);
	}
}

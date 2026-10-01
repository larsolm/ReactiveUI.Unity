using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// A <c>.css</c> file, compiled when it was imported.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is what a stylesheet asset is. The editor compiles each file once, when it changes, and
	/// both the editor and a player load the result — so there is one path from CSS to the cascade,
	/// and a player carries neither the CSS library nor the time it takes to run.
	/// </para>
	/// <para>
	/// Besides the compiled sheet it records what the editor needs to know about the file without
	/// loading it: the class names it uses, for the generated <c>ClassName</c> constants, and the
	/// sheets it imports, which decide the cascade order.
	/// </para>
	/// </remarks>
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

		[SerializeField, HideInInspector]
		private byte[] _data = Array.Empty<byte>();

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
		public string SourcePath => _sourcePath;

		/// <summary>Every class a selector in the sheet names, sorted.</summary>
		public IReadOnlyList<string> Classes => _classes;

		/// <summary>
		/// Classes the sheet styles with nothing narrowing them — a rule that is a single class, outside
		/// any <c>@scope</c> — in the order the sheet first does so.
		/// </summary>
		public IReadOnlyList<string> UnscopedClasses => _unscopedClasses;

		/// <summary>What the compiler had to say about the file.</summary>
		public IReadOnlyList<string> Diagnostics => _diagnostics;

		internal IReadOnlyList<Import> Imports => _imports;

		/// <summary>Whether the file compiled; one that did not loads as an empty sheet.</summary>
		internal bool HasData => _data.Length > 0;

		internal void Set(
			string sourcePath,
			byte[] data,
			string[] classes,
			string[] unscopedClasses,
			Import[] imports,
			string[] diagnostics)
		{
			_sourcePath = sourcePath;
			_data = data;
			_classes = classes;
			_unscopedClasses = unscopedClasses;
			_imports = imports;
			_diagnostics = diagnostics;
		}

		/// <summary>Reads the compiled sheet back.</summary>
		internal StyleSheet Load() => StyleSheetSerializer.Read(_data, string.IsNullOrEmpty(_sourcePath) ? name : _sourcePath);
	}
}

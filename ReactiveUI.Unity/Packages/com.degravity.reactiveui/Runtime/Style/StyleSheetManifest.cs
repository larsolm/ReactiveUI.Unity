using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// The generated list of stylesheets included in a build.
	/// </summary>
	public sealed class StyleSheetManifest : ScriptableObject
	{
		/// <summary>
		/// Where <see cref="UiRoot"/> looks for the manifest, relative to a <c>Resources</c> folder.
		/// </summary>
		internal const string ResourcePath = "ReactiveUI/StyleSheets";

		[Tooltip("Regenerated automatically from every .css file in the project. Editing this by hand "
			+ "is pointless — the next stylesheet import overwrites it.")]
		[SerializeField]
		private CompiledStyleSheet[] _sheets = Array.Empty<CompiledStyleSheet>();

		internal IReadOnlyList<CompiledStyleSheet> Sheets => _sheets;

		internal void SetSheets(CompiledStyleSheet[] sheets) => _sheets = sheets;
	}
}

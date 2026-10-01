using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// The list of stylesheets a build loads, regenerated from the project's <c>.css</c> files.
	/// </summary>
	/// <remarks>
	/// <para>
	/// It does two jobs, and the second is the one that is easy to miss: it tells <see cref="UiRoot"/>
	/// what to load, and — because a compiled sheet nothing references is stripped from the player — it
	/// is what gets the sheets into the build at all.
	/// </para>
	/// <para>
	/// Order is the cascade's document order, written here already resolved — imported sheets before
	/// the sheets importing them, ordinal asset path otherwise — so that a build resolves two equally
	/// specific rules exactly the way the editor did.
	/// </para>
	/// </remarks>
	public sealed class StyleSheetManifest : ScriptableObject
	{
		/// <summary>
		/// Where <see cref="UiRoot"/> looks for the manifest, relative to a <c>Resources</c> folder.
		/// </summary>
		public const string ResourcePath = "ReactiveUI/StyleSheets";

		[Tooltip("Regenerated automatically from every .css file in the project. Editing this by hand "
			+ "is pointless — the next stylesheet import overwrites it.")]
		[SerializeField]
		private CompiledStyleSheet[] _sheets = Array.Empty<CompiledStyleSheet>();

		public IReadOnlyList<CompiledStyleSheet> Sheets => _sheets;

		internal void SetSheets(CompiledStyleSheet[] sheets) => _sheets = sheets;
	}
}

using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>
	/// Supplies the stylesheets a runtime uses, and tells it when they change.
	/// </summary>
	/// <remarks>
	/// The runtime never parses CSS: every <c>.css</c> is compiled when the editor imports it, and both
	/// the editor and a player load the result. In the editor the source reloads a sheet whenever it is
	/// reimported, which is what makes editing one restyle a running game without a script recompile.
	/// </remarks>
	public interface IStyleSheetSource
	{
		/// <summary>
		/// Raised when the sheets have changed and the runtime should re-apply them.
		/// </summary>
		event Action? Changed;

		internal IReadOnlyList<StyleSheet> Sheets { get; }
	}

	/// <summary>
	/// The active source. Set once by whichever layer knows how to produce sheets.
	/// </summary>
	// The editor catalog claims the source from [InitializeOnLoadMethod], which does not re-run on
	// entering Play mode.
	[NoAutoStaticsCleanup]
	public static partial class StyleSheets
	{
		public static bool HasSource => s_source is not null;

		internal static IReadOnlyList<StyleSheet> Current => s_source?.Sheets ?? Array.Empty<StyleSheet>();

		public static event Action Changed = null!;
		private static IStyleSheetSource? s_source = null;

		public static void SetSource(IStyleSheetSource source)
		{
			if (s_source is not null)
				s_source.Changed -= OnChanged;

			s_source = source;
			s_source.Changed += OnChanged;

			OnChanged();
		}

		private static void OnChanged()
		{
			Changed?.Invoke();
		}
	}
}

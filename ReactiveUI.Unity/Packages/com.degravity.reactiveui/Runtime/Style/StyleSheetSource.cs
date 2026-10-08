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
	internal interface IStyleSheetSource
	{
		/// <summary>
		/// Raised when the sheets have changed and the runtime should re-apply them.
		/// </summary>
		event Action? Changed;

		/// <summary>The number of sheet slots, in cascade order.</summary>
		int Count { get; }

		/// <summary>The slot's sheet, loading it on first request; null if it failed to load.</summary>
		StyleSheet? Get(int slot);

		/// <summary>Whether the slot must be active before anything is matched.</summary>
		bool IsEager(int slot);

		/// <summary>The deferred slots whose sheets name the class, or null for none.</summary>
		IReadOnlyList<int>? LazySlotsFor(int classId);

		/// <summary>The asset path the slot's sheet was compiled from.</summary>
		string PathOf(int slot);

		/// <summary>The slot of the sheet compiled from <paramref name="path"/>, or -1 for none.</summary>
		int SlotOf(string path);
	}

	/// <summary>
	/// The active source. Set once by whichever layer knows how to produce sheets.
	/// </summary>
	// The editor catalog claims the source from [InitializeOnLoadMethod], which does not re-run on
	// entering Play mode.
	[NoAutoStaticsCleanup]
	internal static partial class StyleSheets
	{
		public static bool HasSource => s_source is not null;

		internal static IStyleSheetSource? Source => s_source;

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

using System;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>
	/// Storage for the inline styles declared this pass, one slot per node that asks for one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Paged rather than a single growable array, and this is load-bearing. <c>node.Style[Css.Left] =
	/// value</c> only compiles against a <c>ref</c>-returning property — assigning through an indexer
	/// on a returned <em>value</em> is CS1612 — and a <c>ref</c> into an array that later resizes
	/// points at the abandoned copy, so the write would land nowhere and the node would silently
	/// render unstyled. Pages are appended and never moved, so a ref handed out here stays valid for
	/// the life of the pass.
	/// </para>
	/// <para>
	/// Slots are allocated on first use rather than one per node: an inline style is ~200 bytes and
	/// most nodes never carry one.
	/// </para>
	/// </remarks>
	[NoAutoStaticsCleanup]
	internal static partial class InlineArena
	{
		private const int PageSize = 128;

		private static InlineStyle[]?[] s_pages = new InlineStyle[]?[8];
		private static int s_slotCount;

		/// <summary>
		/// Allocates this node's slot if it has none, and returns it.
		/// </summary>
		internal static int Slot(int node)
		{
			var existing = ElementPool.InlineSlotOf(node);

			if (existing >= 0)
				return existing;

			var slot = s_slotCount++;
			var page = slot / PageSize;

			if (page >= s_pages.Length)
				Array.Resize(ref s_pages, s_pages.Length * 2);

			s_pages[page] ??= new InlineStyle[PageSize];

			ElementPool.SetInlineSlot(node, slot);

			return slot;
		}

		internal static ref InlineStyle At(int slot) => ref s_pages[slot / PageSize]![slot % PageSize];

		internal static void Reset()
		{
			for (var page = 0; page < s_pages.Length; page++)
			{
				var entries = s_pages[page];

				if (entries is null)
					break;

				// Cleared, not just rewound: an entry's StyleValue can hold a reference.
				Array.Clear(entries, 0, PageSize);
			}

			s_slotCount = 0;
		}
	}
}

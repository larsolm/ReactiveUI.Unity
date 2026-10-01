using System.Collections;

namespace ReactiveUI
{
	/// <summary>
	/// A box. The default container.
	/// </summary>
	public readonly struct View : IElement
	{
		public Element Handle { get; }

		/// <summary>An unclassed View.</summary>
		/// <remarks>
		/// Declared explicitly, and this is load-bearing: for a struct, <c>new View()</c> binds to
		/// the implicit parameterless constructor rather than to the one whose arguments are all
		/// optional. Without this it would zero-initialise instead, yielding a handle to no element
		/// at all — and the node, along with every child added to it, would silently not render.
		/// </remarks>
		public View()
			: this(default)
		{
		}

		public View(ClassSet className = default, ElementRef? elementRef = null)
		{
			Handle = Element.Host(HostKind.View, className, elementRef, -1);
		}

		/// <inheritdoc cref="InlineStyle"/>
		public ref InlineStyle Style => ref InlineArena.At(InlineArena.Slot(Handle._node));

		public static implicit operator Element(View self) => self.Handle;
		public static Element? operator &(bool value, View self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(View self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(View));
	}
}

using System.Collections;

namespace ReactiveUI
{
	/// <summary>
	/// Which way a <see cref="Scroll"/> scrolls.
	/// </summary>
	public enum ScrollAxis
	{
		Vertical,
		Horizontal,
		Both,
	}

	/// <summary>
	/// <see cref="Scroll"/>'s props.
	/// </summary>
	public readonly record struct ScrollProps(ScrollAxis Axis = ScrollAxis.Vertical);

	/// <summary>
	/// A clipped, scrollable viewport.
	/// </summary>
	public readonly struct Scroll : IElement
	{
		public Element Handle { get; }

		/// <summary>An unclassed Scroll.</summary>
		/// <remarks>
		/// Declared explicitly, and this is load-bearing: for a struct, <c>new Scroll()</c> binds to
		/// the implicit parameterless constructor rather than to the one whose arguments are all
		/// optional. Without this it would zero-initialise instead, yielding a handle to no element
		/// at all — and the node, along with every child added to it, would silently not render.
		/// </remarks>
		public Scroll()
			: this(default)
		{
		}

		public Scroll(ClassSet className = default, ScrollProps? props = null, ElementRef? elementRef = null)
		{
			Handle = Element.Host(HostKind.Scroll, className, elementRef, PropsPool<ScrollProps>.Add(props ?? new ScrollProps()));
		}

		/// <inheritdoc cref="InlineStyle"/>
		public ref InlineStyle Style => ref InlineArena.At(InlineArena.Slot(Handle._node));

		public static implicit operator Element(Scroll self) => self.Handle;
		public static Element? operator &(bool value, Scroll self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(Scroll self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Scroll));
	}
}

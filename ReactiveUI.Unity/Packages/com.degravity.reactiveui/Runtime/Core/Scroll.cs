using System.Collections;

namespace ReactiveUI
{
	/// <summary>
	/// The directions a <see cref="Scroll"/> scrolls in.
	/// </summary>
	public enum ScrollAxis
	{
		Vertical,
		Horizontal,
		Both,
	}

	/// <summary>
	/// Props for <see cref="Scroll"/>.
	/// </summary>
	/// <param name="Axis">The directions the content scrolls in.</param>
	public readonly record struct ScrollProps(ScrollAxis Axis = ScrollAxis.Vertical);

	/// <summary>
	/// A container that clips its children and scrolls them.
	/// </summary>
	public readonly struct Scroll : IElement
	{
		/// <inheritdoc/>
		public Element Handle { get; }

		/// <summary>
		/// Creates an element with no classes.
		/// </summary>
		public Scroll()
			: this(default)
		{
		}

		/// <summary>
		/// Creates an element with the given classes, props, and ref.
		/// </summary>
		public Scroll(ClassSet className = default, ScrollProps? props = null, ElementRef? elementRef = null)
		{
			Handle = Element.Host(HostKind.Scroll, className, elementRef, PropsPool<ScrollProps>.Add(props ?? new ScrollProps()));
		}

		/// <summary>
		/// The element's inline style.
		/// </summary>
		public ref InlineStyle Style => ref InlineArena.At(InlineArena.Slot(Handle._node));

		public static implicit operator Element(Scroll self) => self.Handle;
		public static Element? operator &(bool value, Scroll self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(Scroll self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Scroll));
	}
}

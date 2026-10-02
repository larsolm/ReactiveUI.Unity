using System.Collections;

namespace ReactiveUI
{
	/// <summary>
	/// A general-purpose container element.
	/// </summary>
	public readonly struct View : IElement
	{
		/// <inheritdoc/>
		public Element Handle { get; }

		/// <summary>
		/// Creates an element with no classes.
		/// </summary>
		public View()
			: this(default)
		{
		}

		/// <summary>
		/// Creates an element with the given classes and ref.
		/// </summary>
		public View(ClassSet className = default, ElementRef? elementRef = null)
		{
			Handle = Element.Host(HostKind.View, className, elementRef, -1);
		}

		/// <summary>
		/// The element's inline style.
		/// </summary>
		public ref InlineStyle Style => ref InlineArena.At(InlineArena.Slot(Handle._node));

		public static implicit operator Element(View self) => self.Handle;
		public static Element? operator &(bool value, View self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(View self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(View));
	}
}

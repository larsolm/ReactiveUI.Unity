using System.Collections;

namespace ReactiveUI
{
	/// <summary>
	/// Props for <see cref="Text"/>.
	/// </summary>
	/// <param name="Content">The text to display.</param>
	public readonly record struct TextProps(string Content = "");

	/// <summary>
	/// An element that displays text.
	/// </summary>
	public readonly struct Text : IElement
	{
		/// <inheritdoc/>
		public Element Handle { get; }

		/// <summary>
		/// Creates an element with no classes.
		/// </summary>
		public Text()
			: this(default)
		{
		}

		/// <summary>
		/// Creates an element with the given classes, props, and ref.
		/// </summary>
		public Text(ClassSet className = default, TextProps? props = null, ElementRef? elementRef = null)
		{
			Handle = Element.Host(HostKind.Text, className, elementRef, PropsPool<TextProps>.Add(props ?? new TextProps()));
		}

		/// <summary>
		/// The element's inline style.
		/// </summary>
		public ref InlineStyle Style => ref InlineArena.At(InlineArena.Slot(Handle._node));

		public static implicit operator Element(Text self) => self.Handle;
		public static Element? operator &(bool value, Text self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(Text self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Text));
	}
}

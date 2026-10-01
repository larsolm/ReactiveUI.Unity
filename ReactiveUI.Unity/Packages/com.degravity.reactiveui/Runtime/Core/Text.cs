using System.Collections;

namespace ReactiveUI
{
	public readonly record struct TextProps(string Content = "");

	/// <summary>
	/// A text run.
	/// </summary>
	public readonly struct Text : IElement
	{
		public Element Handle { get; }

		/// <summary>An unclassed Text.</summary>
		/// <remarks>
		/// Declared explicitly, and this is load-bearing: for a struct, <c>new Text()</c> binds to
		/// the implicit parameterless constructor rather than to the one whose arguments are all
		/// optional. Without this it would zero-initialise instead, yielding a handle to no element
		/// at all — and the node, along with every child added to it, would silently not render.
		/// </remarks>
		public Text()
			: this(default)
		{
		}

		public Text(ClassSet className = default, TextProps? props = null, ElementRef? elementRef = null)
		{
			Handle = Element.Host(HostKind.Text, className, elementRef, PropsPool<TextProps>.Add(props ?? new TextProps()));
		}

		/// <inheritdoc cref="InlineStyle"/>
		public ref InlineStyle Style => ref InlineArena.At(InlineArena.Slot(Handle._node));

		public static implicit operator Element(Text self) => self.Handle;
		public static Element? operator &(bool value, Text self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(Text self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Text));
	}
}

using System.Collections;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// How an <see cref="Image"/> draws its sprite.
	/// </summary>
	public enum ImageMode
	{
		/// <summary>Stretches the whole sprite to fill the element.</summary>
		Simple,

		/// <summary>Draws the sprite as a nine-slice using its borders.</summary>
		Sliced,

		/// <summary>Draws the sprite using its mesh, as imported from an SVG.</summary>
		Svg,
	}

	/// <summary>
	/// Props for <see cref="Image"/>.
	/// </summary>
	/// <param name="Sprite">The sprite to draw.</param>
	/// <param name="Mode">How the sprite is drawn.</param>
	public readonly record struct ImageProps(Sprite? Sprite = null, ImageMode Mode = ImageMode.Simple);

	/// <summary>
	/// An element that draws a sprite.
	/// </summary>
	public readonly struct Image : IElement
	{
		/// <inheritdoc/>
		public Element Handle { get; }

		/// <summary>
		/// Creates an element with no classes.
		/// </summary>
		public Image()
			: this(default)
		{
		}

		/// <summary>
		/// Creates an element with the given classes, props, and ref.
		/// </summary>
		public Image(ClassSet className = default, ImageProps? props = null, ElementRef? elementRef = null)
		{
			Handle = Element.Host(HostKind.Image, className, elementRef, PropsPool<ImageProps>.Add(props ?? new ImageProps()));
		}

		/// <summary>
		/// The element's inline style.
		/// </summary>
		public ref InlineStyle Style => ref InlineArena.At(InlineArena.Slot(Handle._node));

		public static implicit operator Element(Image self) => self.Handle;
		public static Element? operator &(bool value, Image self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(Image self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Image));
	}
}

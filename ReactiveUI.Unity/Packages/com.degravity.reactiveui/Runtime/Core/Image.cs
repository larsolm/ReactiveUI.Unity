using System.Collections;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// How an <see cref="Image"/> stretches its sprite.
	/// </summary>
	public enum ImageMode
	{
		Simple,
		Sliced,
		Svg,
	}

	/// <summary>
	/// <see cref="Image"/>'s props.
	/// </summary>
	public readonly record struct ImageProps(Sprite? Sprite = null, ImageMode Mode = ImageMode.Simple);

	/// <summary>
	/// A sprite or SVG.
	/// </summary>
	public readonly struct Image : IElement
	{
		public Element Handle { get; }

		/// <summary>An unclassed Image.</summary>
		/// <remarks>
		/// Declared explicitly, and this is load-bearing: for a struct, <c>new Image()</c> binds to
		/// the implicit parameterless constructor rather than to the one whose arguments are all
		/// optional. Without this it would zero-initialise instead, yielding a handle to no element
		/// at all — and the node, along with every child added to it, would silently not render.
		/// </remarks>
		public Image()
			: this(default)
		{
		}

		public Image(ClassSet className = default, ImageProps? props = null, ElementRef? elementRef = null)
		{
			Handle = Element.Host(HostKind.Image, className, elementRef, PropsPool<ImageProps>.Add(props ?? new ImageProps()));
		}

		/// <inheritdoc cref="InlineStyle"/>
		public ref InlineStyle Style => ref InlineArena.At(InlineArena.Slot(Handle._node));

		public static implicit operator Element(Image self) => self.Handle;
		public static Element? operator &(bool value, Image self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(Image self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Image));
	}
}

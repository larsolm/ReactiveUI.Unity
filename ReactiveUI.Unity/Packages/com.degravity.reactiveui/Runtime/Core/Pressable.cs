using System;
using System.Collections;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Props for <see cref="Pressable"/>.
	/// </summary>
	/// <param name="OnClick">Invoked when the element is clicked or submitted while focused.</param>
	/// <param name="OnClickAt">Invoked on click with the pointer position in the element's local space.</param>
	/// <param name="OnPressDown">Invoked when a press begins.</param>
	/// <param name="OnPressUp">Invoked when a press ends.</param>
	/// <param name="OnHoverEnter">Invoked when the pointer enters the element.</param>
	/// <param name="OnHoverExit">Invoked when the pointer leaves the element.</param>
	/// <param name="Disabled">Whether the element ignores input and cannot be focused.</param>
	/// <param name="Unfocusable">Whether focus navigation skips the element.</param>
	/// <param name="OnMove">Invoked with the navigation direction while the element is focused; return true to consume it.
	/// </param>
	public readonly record struct PressableProps(
		Action? OnClick = null,
		Action<Vector2>? OnClickAt = null,
		Action? OnPressDown = null,
		Action? OnPressUp = null,
		Action? OnHoverEnter = null,
		Action? OnHoverExit = null,
		bool Disabled = false,
		bool Unfocusable = false,
		Func<Vector2, bool>? OnMove = null
	);

	/// <summary>
	/// A container that responds to pointer and navigation input.
	/// </summary>
	public readonly struct Pressable : IElement
	{
		/// <inheritdoc/>
		public Element Handle { get; }

		/// <summary>
		/// Creates an element with no classes.
		/// </summary>
		public Pressable()
			: this(default)
		{
		}

		/// <summary>
		/// Creates an element with the given classes, props, and ref.
		/// </summary>
		public Pressable(ClassSet className = default, PressableProps? props = null, ElementRef? elementRef = null)
		{
			Handle = Element.Host(
				HostKind.Pressable,
				className,
				elementRef,
				PropsPool<PressableProps>.Add(props ?? new PressableProps()));
		}

		/// <summary>
		/// The element's inline style.
		/// </summary>
		public ref InlineStyle Style => ref InlineArena.At(InlineArena.Slot(Handle._node));

		public static implicit operator Element(Pressable self) => self.Handle;
		public static Element? operator &(bool value, Pressable self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(Pressable self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Pressable));
	}
}

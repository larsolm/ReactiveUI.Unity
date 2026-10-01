using System;
using System.Collections;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// <see cref="Pressable"/>'s props.
	/// </summary>
	/// <param name="Unfocusable">
	/// Keeps navigation from landing here. A part of a larger control — a segment, a slider's track —
	/// stays clickable without becoming a stop of its own. Phrased negatively so that zeroed props, which
	/// a record struct's parameterless constructor produces, mean the ordinary focusable case.
	/// </param>
	/// <param name="OnMove">
	/// Offered each navigation direction while this node holds focus; returning true consumes it, so a
	/// focused slider or option row can take left and right instead of losing focus to them.
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
	/// A box that responds to pointer and focus input.
	/// </summary>
	public readonly struct Pressable : IElement
	{
		public Element Handle { get; }

		/// <summary>An unclassed Pressable.</summary>
		/// <remarks>
		/// Declared explicitly, and this is load-bearing: for a struct, <c>new Pressable()</c> binds to
		/// the implicit parameterless constructor rather than to the one whose arguments are all
		/// optional. Without this it would zero-initialise instead, yielding a handle to no element
		/// at all — and the node, along with every child added to it, would silently not render.
		/// </remarks>
		public Pressable()
			: this(default)
		{
		}

		public Pressable(ClassSet className = default, PressableProps? props = null, ElementRef? elementRef = null)
		{
			Handle = Element.Host(
				HostKind.Pressable,
				className,
				elementRef,
				PropsPool<PressableProps>.Add(props ?? new PressableProps()));
		}

		/// <inheritdoc cref="InlineStyle"/>
		public ref InlineStyle Style => ref InlineArena.At(InlineArena.Slot(Handle._node));

		public static implicit operator Element(Pressable self) => self.Handle;
		public static Element? operator &(bool value, Pressable self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(Pressable self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Pressable));
	}
}

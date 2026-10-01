using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Tracks which node holds focus, and moves focus between the nodes that can take it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Focus is a state bit like any other, so a stylesheet describes what focus looks like and
	/// nothing here needs to know. <c>:focus</c> follows focus always; <c>:focus-visible</c> is
	/// additionally gated on the player using a gamepad or keyboard, which is the distinction that
	/// stops a mouse click from drawing a navigation ring.
	/// </para>
	/// <para>
	/// Scopes exist for modals. A dialog pushes a scope, and navigation stops escaping to the
	/// screen behind it — without that, a stick flick moves the highlight somewhere the player
	/// cannot see.
	/// </para>
	/// </remarks>
	public sealed class FocusManager
	{
		/// <summary>
		/// Whether anything currently holds focus.
		/// </summary>
		public bool HasFocus => Focused is not null;

		internal HostInstance? Focused { get; private set; }

		private readonly List<HostInstance> _focusable = new();
		private readonly List<HostInstance> _scopes = new();

		public FocusManager()
		{
			InputModalityTracker.Changed += OnModalityChanged;
		}

		internal void Register(HostInstance host)
		{
			if (!_focusable.Contains(host))
				_focusable.Add(host);
		}

		internal void Unregister(HostInstance host)
		{
			_focusable.Remove(host);
			_scopes.Remove(host);

			if (ReferenceEquals(Focused, host))
				Focused = null;
		}

		/// <summary>
		/// Moves focus to the node behind a handle.
		/// </summary>
		public void Focus(ElementRef? target)
		{
			Focus(target?._host);
		}

		/// <summary>
		/// Clears focus entirely.
		/// </summary>
		public void Blur()
		{
			Focus((HostInstance?)null);
		}

		/// <summary>
		/// Whether the node behind a handle currently holds focus.
		/// </summary>
		public bool IsFocused(ElementRef? target)
		{
			return target?._host is not null && ReferenceEquals(Focused, target._host);
		}

		/// <summary>
		/// Activates whatever holds focus, as pressing it would.
		/// </summary>
		public void Submit()
		{
			if (Focused is not PressableHost pressable || !CanFocus(pressable))
				return;

			pressable._onClick?.Invoke();
		}

		/// <summary>
		/// Confines navigation to a subtree, for a modal or a menu.
		/// </summary>
		public void PushScope(ElementRef scope)
		{
			if (scope._host is not null)
				PushScope(scope._host);
		}

		public void PopScope(ElementRef scope)
		{
			if (scope._host is not null)
				PopScope(scope._host);
		}

		/// <remarks>
		/// Focus left behind outside the new scope is dropped, so the next navigation input lands
		/// inside the scope instead of trying to travel out of a subtree it can no longer reach.
		/// </remarks>
		internal void PushScope(HostInstance scope)
		{
			_scopes.Add(scope);

			if (Focused is not null && !InActiveScope(Focused))
				Focus((HostInstance?)null);
		}

		internal void PopScope(HostInstance scope)
		{
			var index = _scopes.LastIndexOf(scope);
			if (index >= 0)
				_scopes.RemoveAt(index);
		}

		/// <summary>
		/// Moves focus in a direction, choosing the nearest candidate that actually lies that way.
		/// </summary>
		/// <remarks>
		/// Distance is weighted so that alignment with the direction of travel counts for more than
		/// raw proximity. Picking purely by distance feels wrong in practice: a control slightly
		/// nearer but well off-axis steals the focus from the one the player was clearly heading
		/// towards.
		/// </remarks>
		public bool Move(Vector2 direction)
		{
			if (Focused is null || !CanFocus(Focused))
				return FocusFirst();

			if (Focused is PressableHost { _onMove: { } consume } && consume(direction))
				return true;

			var origin = Centre(Focused);
			HostInstance? best = null;
			var bestScore = float.MaxValue;

			for (var i = 0; i < _focusable.Count; i++)
			{
				var candidate = _focusable[i];
				if (ReferenceEquals(candidate, Focused) || !CanFocus(candidate)) continue;

				var delta = Centre(candidate) - origin;
				var along = Vector2.Dot(delta, direction);

				// Anything level with or behind the origin is not in this direction.
				if (along <= 1f)
					continue;

				var across = Mathf.Abs(delta.x * direction.y - delta.y * direction.x);
				var score = along + across * 2f;

				if (score >= bestScore)
					continue;

				bestScore = score;
				best = candidate;
			}

			if (best is null)
				return false;

			InputModalityTracker.NoteNavigation();
			Focus(best);

			return true;
		}

		internal void Focus(HostInstance? host)
		{
			if (ReferenceEquals(Focused, host))
				return;

			if (Focused is not null)
			{
				Focused.SetState(UiStates.s_focus, false);
				Focused.SetState(UiStates.s_focusVisible, false);
			}

			Focused = host;

			if (Focused is null)
				return;

			Focused.SetState(UiStates.s_focus, true);
			Focused.SetState(UiStates.s_focusVisible, InputModalityTracker.Current == InputModality.Navigation);

			RevealInScroll(Focused);
		}

		private bool FocusFirst()
		{
			for (var i = 0; i < _focusable.Count; i++)
			{
				if (!CanFocus(_focusable[i])) continue;

				InputModalityTracker.NoteNavigation();
				Focus(_focusable[i]);

				return true;
			}

			return false;
		}

		private bool CanFocus(HostInstance host)
		{
			return host is not PressableHost { _disabled: true } and not PressableHost { _focusable: false }
				&& !IsExiting(host)
				&& InActiveScope(host);
		}

		/// <summary>
		/// Whether a node is inside a subtree playing its exit animation.
		/// </summary>
		/// <remarks>
		/// An exiting subtree is still mounted, so its pressables are still registered — but it is on
		/// its way out, and navigation landing in a dialog that is closing is a focus the player loses.
		/// </remarks>
		private static bool IsExiting(HostInstance host)
		{
			for (Instance? current = host; current is not null; current = current._parent)
			{
				if (current is HostInstance ancestor && (ancestor._state & UiStates.s_exit.Mask) != 0)
					return true;
			}

			return false;
		}

		/// <summary>
		/// Scrolls every scroll view enclosing a node until the node is inside it.
		/// </summary>
		/// <remarks>
		/// Walked outward so a list nested in a scrolling page first brings the item into its own
		/// viewport, then brings itself into the page's.
		/// </remarks>
		private static void RevealInScroll(HostInstance host)
		{
			for (var current = host._parent; current is not null; current = current._parent)
			{
				if (current is ScrollHost scroll)
					scroll.Reveal(host._rectTransform);
			}
		}

		private bool InActiveScope(HostInstance host)
		{
			if (_scopes.Count == 0) return true;

			var scope = _scopes[_scopes.Count - 1];

			for (Instance? current = host; current is not null; current = current._parent)
			{
				if (ReferenceEquals(current, scope)) return true;
			}

			return false;
		}

		private static Vector2 Centre(HostInstance host)
		{
			var rect = host._rectTransform;

			return rect.TransformPoint(rect.rect.center);
		}

		private void OnModalityChanged(InputModality modality)
		{
			Focused?.SetState(UiStates.s_focusVisible, modality == InputModality.Navigation);
		}

		internal void Dispose()
		{
			InputModalityTracker.Changed -= OnModalityChanged;
		}
	}
}

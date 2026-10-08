using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Tracks and moves focus between focusable elements.
	/// </summary>
	/// <remarks>
	/// The focused element matches <c>:focus</c>, and also <c>:focus-visible</c> while the
	/// <see cref="InputModality"/> is <see cref="InputModality.Navigation"/>. While navigating, something
	/// is always focused if anything can be: the latest default in the active scope, else the first
	/// focusable element in tree order.
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
		private readonly List<HostInstance> _defaults = new();

		internal FocusManager()
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
			_defaults.Remove(host);

			if (ReferenceEquals(Focused, host))
				Focused = null;
		}

		internal void AddDefault(HostInstance host)
		{
			_defaults.Add(host);
		}

		internal void RemoveDefault(HostInstance host)
		{
			var index = _defaults.LastIndexOf(host);
			if (index >= 0)
				_defaults.RemoveAt(index);
		}

		/// <summary>
		/// Moves focus to the default when navigating and the focused element has gone, been disabled or left the scope.
		/// </summary>
		/// <remarks>
		/// Run once a frame after effects, so a screen that replaces the focused one hands focus to its
		/// own default rather than leaving the player with nothing highlighted until they press again.
		/// </remarks>
		internal void Maintain()
		{
			if (InputModalityTracker.Current != InputModality.Navigation)
				return;

			if (Focused is not null && CanFocus(Focused))
				return;

			FocusDefault();
		}

		/// <summary>
		/// Focuses the element <paramref name="target"/> is attached to, or clears focus when it is null.
		/// </summary>
		public void Focus(ElementRef? target)
		{
			Focus(target?._host);
		}

		/// <summary>
		/// Clears focus.
		/// </summary>
		public void Blur()
		{
			Focus((HostInstance?)null);
		}

		/// <summary>
		/// Whether the element <paramref name="target"/> is attached to has focus.
		/// </summary>
		public bool IsFocused(ElementRef? target)
		{
			return target?._host is not null && ReferenceEquals(Focused, target._host);
		}

		/// <summary>
		/// Clicks the focused element.
		/// </summary>
		public void Submit()
		{
			if (Focused is not PressableHost pressable || !CanFocus(pressable))
				return;

			pressable._onClick?.Invoke();
		}

		/// <summary>
		/// Confines focus navigation to the element <paramref name="scope"/> is attached to and its descendants.
		/// </summary>
		/// <remarks>
		/// Clears focus if it is outside the new scope.
		/// </remarks>
		public void PushScope(ElementRef scope)
		{
			if (scope._host is not null)
				PushScope(scope._host);
		}

		/// <summary>
		/// Removes a scope added by <see cref="PushScope(ElementRef)"/>.
		/// </summary>
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
		/// Moves focus to the nearest focusable element in <paramref name="direction"/>.
		/// </summary>
		/// <returns>Whether focus moved or the move was consumed.</returns>
		/// <remarks>
		/// When nothing is focused, focuses the default instead.
		/// </remarks>
		public bool Move(Vector2 direction)
		{
			if (Focused is null || !CanFocus(Focused))
			{
				InputModalityTracker.NoteNavigation();

				return FocusDefault();
			}

			if (Focused is PressableHost { _onMove: { } consume } && consume(direction))
				return true;

			var space = CanvasSpace(Focused);
			var origin = Centre(Focused, space);
			HostInstance? best = null;
			var bestScore = float.MaxValue;

			for (var i = 0; i < _focusable.Count; i++)
			{
				var candidate = _focusable[i];
				if (ReferenceEquals(candidate, Focused) || !CanFocus(candidate)) continue;

				var delta = Centre(candidate, space) - origin;
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

		private bool FocusDefault()
		{
			for (var i = _defaults.Count - 1; i >= 0; i--)
			{
				if (!CanFocus(_defaults[i])) continue;

				Focus(_defaults[i]);

				return true;
			}

			HostInstance? first = null;

			for (var i = 0; i < _focusable.Count; i++)
			{
				var candidate = _focusable[i];

				if (CanFocus(candidate) && (first is null || Precedes(candidate, first)))
					first = candidate;
			}

			if (first is null)
				return false;

			Focus(first);

			return true;
		}

		/// <summary>
		/// Whether <paramref name="first"/> comes before <paramref name="second"/> in tree order.
		/// </summary>
		/// <remarks>
		/// Registration order is mount order, which puts anything inserted later — a conditional item
		/// in the middle of a list — at the end.
		/// </remarks>
		private static bool Precedes(Instance first, Instance second)
		{
			var a = first;
			var b = second;
			var depthA = Depth(a);
			var depthB = Depth(b);

			for (; depthA > depthB; depthA--) a = a._parent!;
			for (; depthB > depthA; depthB--) b = b._parent!;

			// One is an ancestor of the other, and an ancestor comes first.
			if (ReferenceEquals(a, b))
				return Depth(first) < Depth(second);

			while (!ReferenceEquals(a._parent, b._parent))
			{
				a = a._parent!;
				b = b._parent!;
			}

			var siblings = a._parent?._children;

			return siblings is not null && siblings.IndexOf(a) < siblings.IndexOf(b);
		}

		private static int Depth(Instance instance)
		{
			var depth = 0;

			for (var current = instance._parent; current is not null; current = current._parent)
				depth++;

			return depth;
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

		/// <summary>
		/// The root canvas a node draws on, whose local space is measured in UI pixels.
		/// </summary>
		/// <remarks>
		/// World space is scaled by the canvas: on a camera or world canvas one unit can be dozens of
		/// pixels, so a pixel threshold there throws away neighbours that sit right next to each other.
		/// </remarks>
		private static Transform? CanvasSpace(HostInstance host)
		{
			var canvas = host._rectTransform.GetComponentInParent<Canvas>();

			return canvas != null ? canvas.rootCanvas.transform : null;
		}

		private static Vector2 Centre(HostInstance host, Transform? space)
		{
			var rect = host._rectTransform;
			var world = rect.TransformPoint(rect.rect.center);

			return space != null ? space.InverseTransformPoint(world) : world;
		}

		private void OnModalityChanged(InputModality modality)
		{
			if (modality == InputModality.Navigation && (Focused is null || !CanFocus(Focused)))
				FocusDefault();

			Focused?.SetState(UiStates.s_focusVisible, modality == InputModality.Navigation);
		}

		internal void Dispose()
		{
			InputModalityTracker.Changed -= OnModalityChanged;
		}
	}
}

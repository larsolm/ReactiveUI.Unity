using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace ReactiveUI
{
	/// <summary>
	/// Input System actions bound by mounted components, newest binding first.
	/// </summary>
	/// <remarks>
	/// The action-shaped twin of <see cref="HotkeyRegistry"/>, with the same last-registered-wins rule:
	/// a modal binding Cancel shadows the screen beneath it, and unmounting the modal hands Cancel back.
	/// Each action fires at most one binding per frame. Actions are polled rather than subscribed to,
	/// so an action that is disabled simply never fires — enabling and disabling maps stays the game's
	/// business.
	/// </remarks>
	internal sealed class InputActionRegistry
	{
		private readonly struct Binding
		{
			internal readonly InputAction _action;
			internal readonly Action _listener;
			internal readonly object _owner;

			internal Binding(InputAction action, Action listener, object owner)
			{
				_action = action;
				_listener = listener;
				_owner = owner;
			}
		}

		private readonly List<Binding> _bindings = new();
		private readonly List<InputAction> _dispatched = new();

		internal bool IsEmpty => _bindings.Count == 0;

		internal void Register(InputAction action, Action listener, object owner)
		{
			_bindings.Add(new Binding(action, listener, owner));
		}

		internal void UnregisterAll(object owner)
		{
			for (var i = _bindings.Count - 1; i >= 0; i--)
			{
				if (ReferenceEquals(_bindings[i]._owner, owner))
					_bindings.RemoveAt(i);
			}
		}

		internal void Dispatch()
		{
			if (_bindings.Count == 0)
				return;

			_dispatched.Clear();

			for (var i = _bindings.Count - 1; i >= 0; i--)
			{
				// A listener can unmount bindings, which shortens the list under the loop.
				if (i >= _bindings.Count)
					continue;

				var binding = _bindings[i];

				if (_dispatched.Contains(binding._action) || !binding._action.WasPerformedThisFrame())
					continue;

				_dispatched.Add(binding._action);
				NoteModality(binding._action);
				binding._listener();
			}
		}

		/// <remarks>
		/// Before the listener runs, so whatever it mounts — a new tab under a resting pointer — is
		/// styled for navigation rather than lighting up as hovered.
		/// </remarks>
		private static void NoteModality(InputAction action)
		{
			if (action.activeControl?.device is { } and not Pointer)
				InputModalityTracker.NoteNavigation();
		}

		internal void Clear()
		{
			_bindings.Clear();
		}
	}
}

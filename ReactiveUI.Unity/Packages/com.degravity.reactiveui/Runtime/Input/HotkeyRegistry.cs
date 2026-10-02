using System;
using System.Collections.Generic;
using InputKey = UnityEngine.InputSystem.Key;

namespace ReactiveUI
{
	/// <summary>
	/// Keyboard shortcuts, newest binding first.
	/// </summary>
	/// <remarks>
	/// Last registered wins, which is what makes modals behave: a dialog binding Escape shadows
	/// whatever the screen beneath it bound, and unbinding on unmount restores it without either
	/// side knowing about the other.
	/// </remarks>
	internal sealed class HotkeyRegistry
	{
		private readonly struct Binding
		{
			internal readonly InputKey _key;
			internal readonly Action _action;
			internal readonly object _owner;

			internal Binding(InputKey key, Action action, object owner)
			{
				_key = key;
				_action = action;
				_owner = owner;
			}
		}

		private readonly List<Binding> _bindings = new();

		/// <summary>Whether anything is bound at all, so the driver can skip polling the keyboard.</summary>
		internal bool IsEmpty => _bindings.Count == 0;

		internal void Register(InputKey key, Action action, object owner)
		{
			_bindings.Add(new Binding(key, action, owner));
		}

		internal void UnregisterAll(object owner)
		{
			for (var i = _bindings.Count - 1; i >= 0; i--)
			{
				if (ReferenceEquals(_bindings[i]._owner, owner))
					_bindings.RemoveAt(i);
			}
		}

		internal bool Dispatch(InputKey key)
		{
			for (var i = _bindings.Count - 1; i >= 0; i--)
			{
				if (_bindings[i]._key != key)
					continue;

				_bindings[i]._action();

				return true;
			}

			return false;
		}

		internal void Clear()
		{
			_bindings.Clear();
		}
	}
}

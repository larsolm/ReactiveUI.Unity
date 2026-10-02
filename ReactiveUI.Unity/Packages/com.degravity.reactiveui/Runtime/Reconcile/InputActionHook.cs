using System;
using UnityEngine.InputSystem;

namespace ReactiveUI
{
	/// <summary>
	/// Holds an Input System action binding for as long as the component is mounted.
	/// </summary>
	/// <remarks>
	/// Registered once per action, like <see cref="HotkeyHook{TState}"/>, so a re-render cannot move this
	/// binding back to the head of the queue and take the action from a modal above it. Handing it a
	/// different action does re-register, since that is a new binding rather than a refresh.
	/// </remarks>
	internal sealed class InputActionHook<TState> : Hook
	{
		private readonly HookStore _store;
		private readonly Action _listener;

		private InputAction? _action;
		private Action<TState>? _callback;
		private TState _state = default!;

		public InputActionHook(HookStore store)
		{
			_store = store;
			_listener = () => _callback?.Invoke(_state);
		}

		public void Bind(InputAction? action, TState state, Action<TState> callback)
		{
			_callback = callback;
			_state = state;

			if (ReferenceEquals(_action, action))
				return;

			_store.Actions?.UnregisterAll(this);
			_action = action;

			if (_action is not null)
				_store.Actions?.Register(_action, _listener, this);
		}

		public override void Dispose()
		{
			if (_action is null)
				return;

			_action = null;
			_store.Actions?.UnregisterAll(this);
		}
	}

	internal sealed class InputActionHook : Hook
	{
		private readonly HookStore _store;
		private readonly Action _listener;

		private InputAction? _action;
		private Action? _callback;

		public InputActionHook(HookStore store)
		{
			_store = store;
			_listener = () => _callback?.Invoke();
		}

		public void Bind(InputAction? action, Action callback)
		{
			_callback = callback;

			if (ReferenceEquals(_action, action))
				return;

			_store.Actions?.UnregisterAll(this);
			_action = action;

			if (_action is not null)
				_store.Actions?.Register(_action, _listener, this);
		}

		public override void Dispose()
		{
			if (_action is null)
				return;

			_action = null;
			_store.Actions?.UnregisterAll(this);
		}
	}
}

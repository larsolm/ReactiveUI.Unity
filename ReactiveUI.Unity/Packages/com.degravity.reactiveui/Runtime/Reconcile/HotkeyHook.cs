using System;

namespace ReactiveUI
{
	/// <summary>
	/// Holds a keyboard shortcut for as long as the component is mounted.
	/// </summary>
	/// <remarks>
	/// The registration is made once and never renewed, because hotkeys are last-binding-wins and
	/// re-registering would move this one back to the head of the queue — a screen that re-rendered
	/// would take the key back from the modal above it. What is registered is a stable listener, so
	/// the state it dispatches to can still be refreshed every render without disturbing that order.
	/// </remarks>
	internal sealed class HotkeyHook<TState> : Hook
	{
		private readonly HookStore _store;
		private readonly Action _listener;

		private Action<TState>? _callback;
		private TState _state = default!;
		private bool _bound;

		public HotkeyHook(HookStore store)
		{
			_store = store;
			_listener = () => _callback?.Invoke(_state);
		}

		public void Bind(UnityEngine.InputSystem.Key key, TState state, Action<TState> callback)
		{
			_callback = callback;
			_state = state;

			if (_bound)
				return;

			_bound = true;
			_store.Hotkeys?.Register(key, _listener, this);
		}

		public override void Dispose()
		{
			if (!_bound)
				return;

			_bound = false;
			_store.Hotkeys?.UnregisterAll(this);
		}
	}
}

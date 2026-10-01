using System;

namespace ReactiveUI
{
	/// <summary>
	/// Holds the state a handler acts on, behind one delegate of its own that never changes identity.
	/// </summary>
	/// <remarks>
	/// <see cref="Invoke"/> is allocated once, at mount, and reads the fields when it runs. There are
	/// no deps: the call always sees the state the last render stored.
	/// </remarks>
	internal sealed class CallbackHook<TState> : Hook
	{
		public readonly Action Invoke;

		private Action<TState>? _callback;
		private TState _state = default!;

		public CallbackHook()
		{
			Invoke = () => _callback?.Invoke(_state);
		}

		public Action Bind(Action<TState>? callback, in TState state)
		{
			_callback = callback;
			_state = state;

			return Invoke;
		}
	}

	/// <summary>
	/// <see cref="CallbackHook{TState}"/> for a handler that is also passed an argument by whatever
	/// raises it.
	/// </summary>
	internal sealed class CallbackHook<TState, TArg> : Hook
	{
		public readonly Action<TArg> Invoke;

		private Action<TState, TArg>? _callback;
		private TState _state = default!;

		public CallbackHook()
		{
			Invoke = argument => _callback?.Invoke(_state, argument);
		}

		public Action<TArg> Bind(Action<TState, TArg>? callback, in TState state)
		{
			_callback = callback;
			_state = state;

			return Invoke;
		}
	}
}

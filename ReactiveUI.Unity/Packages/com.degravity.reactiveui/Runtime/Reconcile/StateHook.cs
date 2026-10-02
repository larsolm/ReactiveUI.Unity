using System;
using System.Collections.Generic;

namespace ReactiveUI
{
	/// <summary>
	/// A component state value and its setters, returned by <see cref="Ui.UseState{T}"/>.
	/// </summary>
	public readonly struct State<T> : IEquatable<State<T>>
	{
		/// <summary>
		/// The current value.
		/// </summary>
		public T Value => _hook.Value;

		private readonly StateHook<T> _hook;

		internal State(StateHook<T> hook)
		{
			_hook = hook;
		}

		/// <summary>
		/// Sets the value, re-rendering the component if it changed.
		/// </summary>
		public void Set(T value)
		{
			_hook.Set(value);
		}

		/// <summary>
		/// Sets the value to the result of <paramref name="update"/> applied to the current value.
		/// </summary>
		public void Update<TState>(TState state, Func<T, TState, T> update)
		{
			_hook.Set(update(_hook.Value, state));
		}

		public bool Equals(State<T> other) => EqualityComparer<T>.Default.Equals(Value, other.Value);

		/// <summary>
		/// Returns the state's current value.
		/// </summary>
		public static implicit operator T(State<T> state) => state.Value;
	}

	internal sealed class StateHook<T> : Hook
	{
		public T Value;

		/// Allocated once at mount, so a component can hand it out every render for free.
		public readonly Action<T> Setter;

		private readonly HookStore _store;

		public StateHook(HookStore store, T initial)
		{
			_store = store;
			Value = initial;
			Setter = Set;
		}

		public void Set(T value)
		{
			if (EqualityComparer<T>.Default.Equals(Value, value))
				return;

			Value = value;
			_store.Scheduler.MarkDirty(_store.Instance);
		}
	}
}

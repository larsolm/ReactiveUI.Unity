using System;
using System.Collections.Generic;

namespace ReactiveUI
{
	/// <summary>
	/// A piece of component state plus the setters that change it.
	/// </summary>
	/// <remarks>
	/// The setters are bound to the hook, not to the element that read it, so a handler captured in
	/// one render keeps working after later renders have replaced that element.
	/// </remarks>
	public readonly struct State<T> : IEquatable<State<T>>
	{
		public T Value => _hook.Value;

		private readonly StateHook<T> _hook;

		internal State(StateHook<T> hook)
		{
			_hook = hook;
		}

		/// <summary>
		/// Sets the state to the specified value.
		/// </summary>
		/// <param name="value">The new value to set the state to.</param>
		public void Set(T value)
		{
			_hook.Set(value);
		}

		/// <summary>
		/// <see cref="Set"/> as a delegate, for handing to a component that wants one.
		/// </summary>
		/// <remarks>
		/// Built once when the hook is created. Passing the method group instead —
		/// <c>OnChange = value.Set</c> — allocates a delegate <em>and</em> boxes this struct to be its
		/// target, on every render, and the fresh reference defeats the memoisation of whatever it is
		/// handed to.
		/// </remarks>
		public Action<T> Setter => _hook.Setter;

		/// <summary>
		/// Updates from the current value — correct even when several updates batch in one frame.
		/// </summary>
		/// <remarks>
		/// Write <paramref name="update"/> as a <c>static</c> lambda and reach everything it needs
		/// through <paramref name="state"/>; capturing a local instead allocates a closure per call.
		/// <code>
		/// count.Update(step, static (current, amount) => current + amount);
		/// </code>
		/// </remarks>
		public void Update<TState>(TState state, Func<T, TState, T> update)
		{
			_hook.Set(update(_hook.Value, state));
		}

		public bool Equals(State<T> other) => EqualityComparer<T>.Default.Equals(Value, other.Value);

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

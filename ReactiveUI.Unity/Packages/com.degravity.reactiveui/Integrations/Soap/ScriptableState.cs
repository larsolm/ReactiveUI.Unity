using System;
using System.Collections.Generic;
using Obvious.Soap;

namespace ReactiveUI.Soap
{
	/// <summary>
	/// A Soap variable's value and its setters, returned by <see cref="SoapHooks.UseScriptableState{T}"/>.
	/// </summary>
	public readonly struct ScriptableState<T> : IEquatable<ScriptableState<T>>
	{
		/// <summary>
		/// The current value.
		/// </summary>
		public T Value => _variable.Value;

		private readonly ScriptableVariable<T> _variable;

		internal ScriptableState(ScriptableVariable<T> variable)
		{
			_variable = variable;
		}

		/// <summary>
		/// Sets the variable's value, re-rendering every component bound to it if it changed.
		/// </summary>
		public void Set(T value)
		{
			_variable.Value = value;
		}

		/// <summary>
		/// Sets the value to the result of <paramref name="update"/> applied to the current value.
		/// </summary>
		public void Update<TState>(TState state, Func<T, TState, T> update)
		{
			_variable.Value = update(_variable.Value, state);
		}

		public bool Equals(ScriptableState<T> other) => EqualityComparer<T>.Default.Equals(Value, other.Value);

		/// <summary>
		/// Returns the variable's current value.
		/// </summary>
		public static implicit operator T(ScriptableState<T> state) => state.Value;
	}
}

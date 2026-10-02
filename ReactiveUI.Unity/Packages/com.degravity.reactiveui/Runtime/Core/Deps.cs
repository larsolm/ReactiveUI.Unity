using System;

namespace ReactiveUI
{
	/// <summary>
	/// A hook dependency that compares a reference by identity.
	/// </summary>
	public readonly struct Deps<T> : IEquatable<Deps<T>>
		where T : class
	{
		/// <summary>
		/// The wrapped reference.
		/// </summary>
		public readonly T? Value;

		/// <summary>
		/// Wraps <paramref name="value"/>.
		/// </summary>
		public Deps(T? value)
		{
			Value = value;
		}

		public bool Equals(Deps<T> other) => ReferenceEquals(Value, other.Value);

		public override bool Equals(object? obj) => obj is Deps<T> other && Equals(other);

		public override int GetHashCode() => Value is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Value);
	}

	/// <summary>
	/// Factory methods for <see cref="Deps{T}"/>.
	/// </summary>
	public static class Deps
	{
		/// <summary>
		/// Wraps <paramref name="value"/> as an identity-compared hook dependency.
		/// </summary>
		public static Deps<T> Of<T>(T? value)
			where T : class
		{
			return new Deps<T>(value);
		}
	}

	/// <summary>
	/// Thrown when a component reads a context that no ancestor provides.
	/// </summary>
	public sealed class MissingContextException : Exception
	{
		internal MissingContextException(string message) : base(message)
		{
		}
	}
}

using System;

namespace ReactiveUI
{
	/// <summary>
	/// Wraps a reference so it can be a hook dependency, compared by identity.
	/// </summary>
	/// <remarks>
	/// Deps must be <see cref="IEquatable{T}"/> so they compare without boxing. An asset or a definition
	/// declares no equality of its own; this gives it identity equality and composes into a tuple like
	/// any other dep: <c>(index, Deps.Of(relic))</c>.
	/// </remarks>
	public readonly struct Deps<T> : IEquatable<Deps<T>>
		where T : class
	{
		public readonly T? Value;

		public Deps(T? value)
		{
			Value = value;
		}

		public bool Equals(Deps<T> other) => ReferenceEquals(Value, other.Value);

		public override bool Equals(object? obj) => obj is Deps<T> other && Equals(other);

		public override int GetHashCode() => Value is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Value);
	}

	public static class Deps
	{
		/// <inheritdoc cref="Deps{T}"/>
		public static Deps<T> Of<T>(T? value)
			where T : class
		{
			return new Deps<T>(value);
		}
	}

	/// <summary>
	/// Thrown when a component reads a context that nothing above it provides.
	/// </summary>
	public sealed class MissingContextException : Exception
	{
		internal MissingContextException(string message) : base(message)
		{
		}
	}
}

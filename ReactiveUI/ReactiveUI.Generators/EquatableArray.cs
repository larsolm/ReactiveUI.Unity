using System;
using System.Collections;
using System.Collections.Generic;

namespace ReactiveUI.Generators
{
	/// <summary>
	/// An immutable array compared by its contents.
	/// </summary>
	/// <remarks>
	/// An incremental generator only skips work when a stage's output equals the last one, and a plain
	/// array compares by reference — so every model holding a list would count as changed on every
	/// keystroke and regenerate everything downstream of it.
	/// </remarks>
	internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
		where T : IEquatable<T>
	{
		public static readonly EquatableArray<T> Empty = new(Array.Empty<T>());

		private readonly T[]? _items;

		public EquatableArray(T[] items)
		{
			_items = items;
		}

		public int Count => _items?.Length ?? 0;

		public T this[int index] => _items![index];

		public bool Equals(EquatableArray<T> other)
		{
			var left = _items ?? Array.Empty<T>();
			var right = other._items ?? Array.Empty<T>();

			if (left.Length != right.Length)
				return false;

			for (var i = 0; i < left.Length; i++)
			{
				if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
					return false;
			}

			return true;
		}

		public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

		public override int GetHashCode()
		{
			var hash = 17;

			foreach (var item in _items ?? Array.Empty<T>())
				hash = unchecked((hash * 31) + (item?.GetHashCode() ?? 0));

			return hash;
		}

		public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}

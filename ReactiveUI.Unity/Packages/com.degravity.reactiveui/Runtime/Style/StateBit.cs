using System;

namespace ReactiveUI
{
	/// <summary>
	/// One bit in a node's state word.
	/// </summary>
	public readonly struct StateBit : IEquatable<StateBit>
	{
		public static StateBit None => new(-1);

		public ulong Mask => _index < 0 ? 0UL : 1UL << _index;
		public bool IsValid => _index >= 0;

		internal readonly int _index;

		internal StateBit(int index)
		{
			_index = index;
		}

		public bool Equals(StateBit other)
		{
			return _index == other._index;
		}

		public override bool Equals(object? obj)
		{
			return obj is StateBit other && Equals(other);
		}

		public override int GetHashCode()
		{
			return _index;
		}
	}
}

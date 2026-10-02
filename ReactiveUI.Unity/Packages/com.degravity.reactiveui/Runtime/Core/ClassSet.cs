using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>
	/// An interned CSS class name.
	/// </summary>
	public readonly struct ClassName : IEquatable<ClassName>
	{
		/// <summary>
		/// Whether this refers to a non-empty name.
		/// </summary>
		public bool IsValid => _id != 0;

		/// <summary>
		/// The class name text.
		/// </summary>
		public string Name => ClassTable.NameOf(_id);

		internal readonly int _id;

		internal ClassName(int id)
		{
			_id = id;
		}

		/// <summary>
		/// Returns the <see cref="ClassName"/> for <paramref name="name"/>. Used by generated code.
		/// </summary>
		[EditorBrowsable(EditorBrowsableState.Never)]
		public static ClassName Intern(string name) => new(ClassTable.Intern(name));

		public override bool Equals(object? obj) => obj is ClassName other && Equals(other);
		public override int GetHashCode() => _id;
		public override string ToString() => Name;
		public bool Equals(ClassName other) => _id == other._id;

		/// <summary>
		/// Combines two class names into a set.
		/// </summary>
		public static ClassSet operator |(ClassName a, ClassName b) => new ClassSet(a) | b;

		/// <summary>
		/// Returns a set containing <paramref name="a"/> when <paramref name="b"/> is true, otherwise an empty set.
		/// </summary>
		public static ClassSet operator &(ClassName a, bool b) => b ? new ClassSet(a) : default;

		/// <inheritdoc cref="op_BitwiseAnd(ClassName, bool)"/>
		public static ClassSet operator &(bool b, ClassName a) => b ? new ClassSet(a) : default;
	}

	/// <summary>
	/// A set of CSS class names applied to an element.
	/// </summary>
	public struct ClassSet : IEquatable<ClassSet>
	{
		private const int InlineCapacity = 8;

		/// <summary>
		/// The number of class names in the set.
		/// </summary>
		public int Count { get; private set; }

		private int _a;
		private int _b;
		private int _c;
		private int _d;
		private int _e;
		private int _f;
		private int _g;
		private int _h;
		private int[]? _overflow;

		/// <summary>
		/// Creates a set containing <paramref name="name"/>.
		/// </summary>
		public ClassSet(ClassName name) : this()
		{
			if (name.IsValid)
			{
				_a = name._id;
				Count = 1;
			}
		}

		internal void Add(int id)
		{
			if (id == 0)
				return;

			for (var i = 0; i < Count; i++)
			{
				if (this[i] == id)
					return;
			}

			switch (Count)
			{
				case 0:
					_a = id;
					break;
				case 1:
					_b = id;
					break;
				case 2:
					_c = id;
					break;
				case 3:
					_d = id;
					break;
				case 4:
					_e = id;
					break;
				case 5:
					_f = id;
					break;
				case 6:
					_g = id;
					break;
				case 7:
					_h = id;
					break;
				default:
					// Rare enough that growing one slot at a time is cheaper than over-allocating.
					var spill = Count - InlineCapacity;

					if (_overflow is null)
						_overflow = new int[4];
					else if (spill == _overflow.Length)
						Array.Resize(ref _overflow, spill * 2);

					_overflow[spill] = id;

					break;
			}

			Count++;
		}

		/// <summary>
		/// Whether the set contains <paramref name="name"/>.
		/// </summary>
		public readonly bool Contains(ClassName name)
		{
			for (var i = 0; i < Count; i++)
			{
				if (this[i] == name._id)
					return true;
			}

			return false;
		}

		public readonly bool Equals(ClassSet other)
		{
			if (Count != other.Count)
				return false;

			// Order is insertion order, and both sides are built the same way at the same call
			// site, so an ordered compare is correct and avoids a set-difference.
			for (var i = 0; i < Count; i++)
			{
				if (this[i] != other[i])
					return false;
			}

			return true;
		}

		public override readonly bool Equals(object? obj) => obj is ClassSet other && Equals(other);

		public override readonly int GetHashCode()
		{
			var hash = 17;

			for (var i = 0; i < Count; i++)
				hash = hash * 31 + this[i];

			return hash;
		}

		public override readonly string ToString()
		{
			if (Count == 0)
				return string.Empty;

			if (Count == 1)
				return ClassTable.NameOf(_a);

			var builder = new StringBuilder();

			for (var i = 0; i < Count; i++)
			{
				if (i > 0)
					builder.Append('.');

				builder.Append(ClassTable.NameOf(this[i]));
			}

			return builder.ToString();
		}

		internal readonly int this[int index] => index switch
		{
			0 => _a,
			1 => _b,
			2 => _c,
			3 => _d,
			4 => _e,
			5 => _f,
			6 => _g,
			7 => _h,
			_ => _overflow![index - InlineCapacity],
		};

		/// <summary>
		/// Converts a single class name to a set.
		/// </summary>
		public static implicit operator ClassSet(ClassName name) => new(name);

		/// <summary>
		/// Returns the union of two sets.
		/// </summary>
		public static ClassSet operator |(ClassSet a, ClassSet b)
		{
			for (var i = 0; i < b.Count; i++)
				a.Add(b[i]);

			return a;
		}

		/// <summary>
		/// Returns the set with <paramref name="b"/> added.
		/// </summary>
		public static ClassSet operator |(ClassSet a, ClassName b)
		{
			a.Add(b._id);
			return a;
		}
	}

	// Ids are cached in static readonly ClassName fields that never re-initialise.
	[NoAutoStaticsCleanup]
	internal static partial class ClassTable
	{
		private static readonly Dictionary<string, int> s_ids = new(StringComparer.Ordinal);
		private static readonly List<string> s_names = new() { string.Empty };

		public static int Intern(string? name)
		{
			if (string.IsNullOrEmpty(name))
				return 0;

			if (s_ids.TryGetValue(name, out var id))
				return id;

			id = s_names.Count;
			s_names.Add(name);
			s_ids[name] = id;

			return id;
		}

		public static string NameOf(int id)
		{
			return id > 0 && id < s_names.Count ? s_names[id] : string.Empty;
		}
	}
}

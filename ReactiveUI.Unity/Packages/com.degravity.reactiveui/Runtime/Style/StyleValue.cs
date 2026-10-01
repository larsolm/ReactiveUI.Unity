using System;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// How a <see cref="StyleLength"/> resolves against its container.
	/// </summary>
	public enum LengthUnit : byte
	{
		Points,
		Percent,
		Rem,
		Auto,
	}

	/// <summary>
	/// A CSS length: a number plus the unit it is measured in.
	/// </summary>
	public readonly struct StyleLength : IEquatable<StyleLength>
	{
		public readonly float Value;
		public readonly LengthUnit Unit;

		public StyleLength(float value, LengthUnit unit = LengthUnit.Points)
		{
			Value = value;
			Unit = unit;
		}

		public static StyleLength Auto => new(0f, LengthUnit.Auto);

		public static implicit operator StyleLength(float points) => new(points);
		public static implicit operator StyleLength(int points) => new(points);

		public bool Equals(StyleLength other)
		{
			return Value.Equals(other.Value) && Unit == other.Unit;
		}

		public override bool Equals(object? obj)
		{
			return obj is StyleLength other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Value, (int)Unit);
		}

		public override string ToString()
		{
			return Unit == LengthUnit.Auto ? "auto" : $"{Value}{Suffix}";
		}

		private string Suffix => Unit switch
		{
			LengthUnit.Percent => "%",
			LengthUnit.Rem => "rem",
			LengthUnit.Auto => "",
			_ => "px",
		};
	}

	/// <summary>
	/// What kind of value a <see cref="StyleValue"/> is carrying.
	/// </summary>
	public enum StyleValueKind : byte
	{
		None,
		Length,
		Color,
		Number,
		Keyword,
		Reference,

		/// <summary>
		/// A <c>var(--name)</c> that the cascade resolves per node against the custom properties
		/// in scope. Unresolvable references drop the declaration, as CSS does.
		/// </summary>
		VarReference,
	}

	/// <summary>
	/// One resolved property value, in a single fixed-size shape so a computed style is a flat
	/// array rather than a class with a field per property. Four floats cover a colour or a
	/// length; <see cref="Reference"/> carries the handful of values that are genuinely objects
	/// (fonts, sprites, shadow lists).
	/// </summary>
	public readonly struct StyleValue : IEquatable<StyleValue>
	{
		public readonly float A;
		public readonly float B;
		public readonly float C;
		public readonly float D;
		public readonly int Tag;
		public readonly object? Reference;

		private StyleValue(float a, float b, float c, float d, int tag, object? reference)
		{
			A = a;
			B = b;
			C = c;
			D = d;
			Tag = tag;
			Reference = reference;
		}

		public StyleValueKind Kind => (StyleValueKind)(Tag & 0xFF);

		public static StyleValue OfLength(StyleLength length)
		{
			return new(length.Value, 0f, 0f, 0f, (int)StyleValueKind.Length | ((int)length.Unit << 8), null);
		}

		public static StyleValue OfColor(Color color)
		{
			return new(color.r, color.g, color.b, color.a, (int)StyleValueKind.Color, null);
		}

		public static StyleValue OfNumber(float value)
		{
			return new(value, 0f, 0f, 0f, (int)StyleValueKind.Number, null);
		}

		public static StyleValue OfKeyword(int keyword)
		{
			return new(0f, 0f, 0f, 0f, (int)StyleValueKind.Keyword | (keyword << 8), null);
		}

		public static StyleValue OfReference(object? reference)
		{
			return new(0f, 0f, 0f, 0f, (int)StyleValueKind.Reference, reference);
		}

		internal static StyleValue OfVar(int nameId)
		{
			return new(0f, 0f, 0f, 0f, (int)StyleValueKind.VarReference | (nameId << 8), null);
		}

		internal int VarNameId => Tag >> 8;

		// Custom properties are set through a single string indexer, which can only carry one
		// value type — so the conversions live here instead of as indexer overloads.
		public static implicit operator StyleValue(Color color) => OfColor(color);
		public static implicit operator StyleValue(StyleLength length) => OfLength(length);

		public StyleLength AsLength()
		{
			return new(A, (LengthUnit)((Tag >> 8) & 0xFF));
		}

		public Color AsColor()
		{
			return new(A, B, C, D);
		}

		public float AsNumber()
		{
			return A;
		}

		public int AsKeyword()
		{
			return Tag >> 8;
		}

		public bool Equals(StyleValue other)
		{
			return A.Equals(other.A)
				&& B.Equals(other.B)
				&& C.Equals(other.C)
				&& D.Equals(other.D)
				&& Tag == other.Tag
				&& ReferenceEquals(Reference, other.Reference);
		}

		public override bool Equals(object? obj)
		{
			return obj is StyleValue other && Equals(other);
		}

		public override int GetHashCode()
		{
			var hash = HashCode.Combine(A, B, C, D, Tag);

			return Reference is null ? hash : hash * 31 + Reference.GetHashCode();
		}
	}
}

using System;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// The unit of a <see cref="StyleLength"/>.
	/// </summary>
	public enum LengthUnit : byte
	{
		/// <summary>Pixels.</summary>
		Points,

		/// <summary>A percentage of the containing element's size.</summary>
		Percent,

		/// <summary>A multiple of the root's pixels-per-rem.</summary>
		Rem,

		/// <summary>Sized automatically by layout.</summary>
		Auto,
	}

	/// <summary>
	/// A CSS length value and its unit.
	/// </summary>
	public readonly struct StyleLength : IEquatable<StyleLength>
	{
		/// <summary>
		/// The <c>auto</c> length.
		/// </summary>
		public static StyleLength Auto => new(0f, LengthUnit.Auto);

		/// <summary>
		/// The numeric value.
		/// </summary>
		public readonly float Value;

		/// <summary>
		/// The unit <see cref="Value"/> is measured in.
		/// </summary>
		public readonly LengthUnit Unit;

		/// <summary>
		/// Creates a length in points (pixels).
		/// </summary>
		public static StyleLength Points(float value) => new(value, LengthUnit.Points);

		/// <summary>
		/// Creates a length in percent relative to the containing element's size.
		/// </summary>
		public static StyleLength Percent(float value) => new(value, LengthUnit.Percent);

		/// <summary>
		/// Creates a length in rem units.
		/// </summary>
		public static StyleLength Rem(float value) => new(value, LengthUnit.Rem);

		/// <summary>
		/// Creates a length of <paramref name="value"/> in <paramref name="unit"/>.
		/// </summary>
		public StyleLength(float value, LengthUnit unit = LengthUnit.Points)
		{
			Value = value;
			Unit = unit;
		}

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

		/// <summary>
		/// Converts a pixel value to a length.
		/// </summary>
		public static implicit operator StyleLength(float points) => new(points);

		/// <summary>
		/// Converts a pixel value to a length.
		/// </summary>
		public static implicit operator StyleLength(int points) => new(points);
	}

	/// <summary>
	/// What kind of value a <see cref="StyleValue"/> is carrying.
	/// </summary>
	internal enum StyleValueKind : byte
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
	/// A style property value: a length, color, or number.
	/// </summary>
	public readonly struct StyleValue : IEquatable<StyleValue>
	{
		internal readonly float A;
		internal readonly float B;
		internal readonly float C;
		internal readonly float D;
		internal readonly int Tag;
		internal readonly object? Reference;

		private StyleValue(float a, float b, float c, float d, int tag, object? reference)
		{
			A = a;
			B = b;
			C = c;
			D = d;
			Tag = tag;
			Reference = reference;
		}

		internal StyleValueKind Kind => (StyleValueKind)(Tag & 0xFF);

		/// <summary>
		/// Creates a length value.
		/// </summary>
		public static StyleValue OfLength(StyleLength length)
		{
			return new(length.Value, 0f, 0f, 0f, (int)StyleValueKind.Length | ((int)length.Unit << 8), null);
		}

		/// <summary>
		/// Creates a color value.
		/// </summary>
		public static StyleValue OfColor(Color color)
		{
			return new(color.r, color.g, color.b, color.a, (int)StyleValueKind.Color, null);
		}

		/// <summary>
		/// Creates a numeric value.
		/// </summary>
		public static StyleValue OfNumber(float value)
		{
			return new(value, 0f, 0f, 0f, (int)StyleValueKind.Number, null);
		}

		internal static StyleValue OfKeyword(int keyword)
		{
			return new(0f, 0f, 0f, 0f, (int)StyleValueKind.Keyword | (keyword << 8), null);
		}

		internal static StyleValue OfReference(object? reference)
		{
			return new(0f, 0f, 0f, 0f, (int)StyleValueKind.Reference, reference);
		}

		internal static StyleValue OfVar(int nameId)
		{
			return new(0f, 0f, 0f, 0f, (int)StyleValueKind.VarReference | (nameId << 8), null);
		}

		internal int VarNameId => Tag >> 8;

		/// <summary>
		/// Converts a color to a style value.
		/// </summary>
		public static implicit operator StyleValue(Color color) => OfColor(color);

		/// <summary>
		/// Converts a length to a style value.
		/// </summary>
		public static implicit operator StyleValue(StyleLength length) => OfLength(length);

		internal StyleLength AsLength()
		{
			return new(A, (LengthUnit)((Tag >> 8) & 0xFF));
		}

		internal Color AsColor()
		{
			return new(A, B, C, D);
		}

		internal float AsNumber()
		{
			return A;
		}

		internal int AsKeyword()
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

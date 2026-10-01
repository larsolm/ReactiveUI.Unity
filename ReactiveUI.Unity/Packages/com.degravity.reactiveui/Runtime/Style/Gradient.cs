using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	public enum GradientKind
	{
		/// <summary>
		/// A directional gradient along an angle (0° = upward, 90° = rightward).
		/// </summary>
		Linear,

		/// <summary>
		/// An approximated radial gradient from the centre outward.
		/// </summary>
		Radial,
	}

	/// <summary>
	/// A colour stop at a normalized (0..1) position along a gradient.
	/// </summary>
	public readonly struct GradientStop : IEquatable<GradientStop>
	{
		public Color Color => _ink._value;

		public readonly float Position;

		internal readonly VarColor _ink;

		public GradientStop(Color color, float position) : this(new VarColor(color), position)
		{
		}

		internal GradientStop(VarColor ink, float position)
		{
			_ink = ink;
			Position = Mathf.Clamp01(position);
		}

		public bool Equals(GradientStop other)
		{
			return _ink.Equals(other._ink) && Position.Equals(other.Position);
		}

		public override bool Equals(object? obj)
		{
			return obj is GradientStop other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(_ink, Position);
		}
	}

	/// <summary>
	/// A background gradient painted into the (rounded) fill. Linear gradients are exact;
	/// radial gradients are approximated per-vertex (best used on large, softly-tessellated
	/// backgrounds). Referenced from a computed style as a background-image value.
	/// </summary>
	public sealed class Gradient : IEquatable<Gradient>, IVarDependent
	{
		bool IVarDependent.HasVars => _hasVars;

		public readonly GradientKind Kind;

		public readonly float Angle;

		public readonly IReadOnlyList<GradientStop> Stops;

		private readonly bool _hasVars;

		public Gradient(GradientKind kind, float angle, params GradientStop[] stops)
		{
			Kind = kind;
			Angle = angle;
			Stops = stops;

			for (var i = 0; i < stops.Length && !_hasVars; i++)
				_hasVars = stops[i]._ink.IsVar;
		}


		object? IVarDependent.Substitute(IReadOnlyDictionary<int, StyleValue> scope)
		{
			var resolved = new GradientStop[Stops.Count];

			for (var i = 0; i < Stops.Count; i++)
			{
				var stop = Stops[i];

				if (!stop._ink.TryResolve(scope, out var color)) return null;

				resolved[i] = new GradientStop(color, stop.Position);
			}

			return new Gradient(Kind, Angle, resolved);
		}

		/// <summary>
		/// A two-stop linear gradient at the given angle (degrees).
		/// </summary>
		public static Gradient Linear(float angleDegrees, Color from, Color to)
		{
			return new(GradientKind.Linear, angleDegrees, new GradientStop(from, 0f), new GradientStop(to, 1f));
		}

		/// <summary>
		/// A two-stop radial gradient from the centre outward.
		/// </summary>
		public static Gradient Radial(Color center, Color edge)
		{
			return new(GradientKind.Radial, 0f, new GradientStop(center, 0f), new GradientStop(edge, 1f));
		}

		/// <summary>
		/// Samples the stop colours at <paramref name="t"/> (0..1).
		/// </summary>
		public Color Sample(float t)
		{
			var stops = Stops;
			if (stops.Count == 0)
				return Color.clear;

			if (stops.Count == 1 || t <= stops[0].Position)
				return stops[0].Color;

			for (var i = 1; i < stops.Count; i++)
			{
				var b = stops[i];
				if (t > b.Position)
					continue;

				var a = stops[i - 1];
				var span = b.Position - a.Position;
				var k = span <= Mathf.Epsilon ? 0f : (t - a.Position) / span;
				return Color.Lerp(a.Color, b.Color, k);
			}

			return stops[stops.Count - 1].Color;
		}

		public bool Equals(Gradient? other)
		{
			if (other is null || Kind != other.Kind || !Angle.Equals(other.Angle) || Stops.Count != other.Stops.Count)
				return false;

			for (var i = 0; i < Stops.Count; i++)
			{
				if (!Stops[i].Equals(other.Stops[i]))
					return false;
			}

			return true;
		}

		public override bool Equals(object? obj)
		{
			return Equals(obj as Gradient);
		}

		public override int GetHashCode()
		{
			var hash = HashCode.Combine(Kind, Angle, Stops.Count);

			foreach (var stop in Stops)
			{
				hash = HashCode.Combine(hash, stop);
			}

			return hash;
		}
	}
}

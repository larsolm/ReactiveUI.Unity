using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Every timing function the framework evaluates: the named <see cref="Easing"/> members, plus the
	/// <c>cubic-bezier()</c> and <c>steps()</c> curves a sheet declares.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A declared curve is interned here and its id rides in the same keyword slot a named easing
	/// uses, above <see cref="CustomBase"/>, so a computed style still stores an easing as one int and
	/// every reader casts it to <see cref="Easing"/> exactly as before.
	/// </para>
	/// <para>
	/// The table is never cleared. Ids are baked into built sheets, which outlive a play session when
	/// domain reload is off; a curve that is no longer used costs five floats.
	/// </para>
	/// </remarks>
	internal static class EasingCurves
	{
		internal const int CustomBase = 64;

		private enum CurveKind : byte
		{
			Bezier,
			Steps,
		}

		private enum StepPosition : byte
		{
			End,
			Start,
			None,
			Both,
		}

		private readonly struct Curve : IEquatable<Curve>
		{
			public readonly CurveKind Kind;
			public readonly float X1;
			public readonly float Y1;
			public readonly float X2;
			public readonly float Y2;
			public readonly int Steps;
			public readonly StepPosition Position;

			public Curve(float x1, float y1, float x2, float y2)
			{
				Kind = CurveKind.Bezier;
				X1 = x1;
				Y1 = y1;
				X2 = x2;
				Y2 = y2;
				Steps = 0;
				Position = StepPosition.End;
			}

			public Curve(int steps, StepPosition position)
			{
				Kind = CurveKind.Steps;
				X1 = Y1 = X2 = Y2 = 0f;
				Steps = steps;
				Position = position;
			}

			public bool Equals(Curve other)
			{
				return Kind == other.Kind
					&& X1.Equals(other.X1) && Y1.Equals(other.Y1) && X2.Equals(other.X2) && Y2.Equals(other.Y2)
					&& Steps == other.Steps && Position == other.Position;
			}

			public override bool Equals(object? obj) => obj is Curve other && Equals(other);

			public override int GetHashCode() => HashCode.Combine(Kind, X1, Y1, X2, Y2, Steps, Position);
		}

		[NoAutoStaticsCleanup]
		private static readonly List<Curve> s_curves = new();

		[NoAutoStaticsCleanup]
		private static readonly Dictionary<Curve, int> s_ids = new();

		/// <summary>
		/// Reads a timing function written as a function (or a step keyword) and returns its id.
		/// </summary>
		internal static bool TryParse(string text, out int id)
		{
			id = -1;

			var trimmed = text.Trim();

			if (trimmed.Equals("step-start", StringComparison.OrdinalIgnoreCase))
				return Intern(new Curve(1, StepPosition.Start), out id);

			if (trimmed.Equals("step-end", StringComparison.OrdinalIgnoreCase))
				return Intern(new Curve(1, StepPosition.End), out id);

			if (ValueParser.Inner(trimmed, "cubic-bezier") is { } bezier)
			{
				var args = bezier.Split(',');

				if (args.Length != 4
					|| !TryNumber(args[0], out var x1) || !TryNumber(args[1], out var y1)
					|| !TryNumber(args[2], out var x2) || !TryNumber(args[3], out var y2))
				{
					return false;
				}

				// CSS requires both x coordinates in [0, 1]; outside it the curve is not a function
				// of time and has no single value to return.
				if (x1 < 0f || x1 > 1f || x2 < 0f || x2 > 1f)
					return false;

				return Intern(new Curve(x1, y1, x2, y2), out id);
			}

			if (ValueParser.Inner(trimmed, "steps") is { } steps)
			{
				var args = steps.Split(',');

				if (args.Length is < 1 or > 2
					|| !int.TryParse(args[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
				{
					return false;
				}

				var position = StepPosition.End;

				if (args.Length == 2)
				{
					switch (args[1].Trim().ToLowerInvariant())
					{
						case "end":
						case "jump-end":
							position = StepPosition.End;
							break;
						case "start":
						case "jump-start":
							position = StepPosition.Start;
							break;
						case "jump-none":
							position = StepPosition.None;
							break;
						case "jump-both":
							position = StepPosition.Both;
							break;
						default:
							return false;
					}
				}

				if (count < 1 || (position == StepPosition.None && count < 2))
					return false;

				return Intern(new Curve(count, position), out id);
			}

			return false;
		}

		/// <summary>
		/// Maps linear progress through a timing function.
		/// </summary>
		/// <remarks>
		/// The named curves are the standard definitions — the same ones PrimeTween implements under
		/// those names — so every easing, named or declared, is evaluated in one place.
		/// </remarks>
		internal static float Evaluate(Easing easing, float t)
		{
			var id = (int)easing;

			if (id >= CustomBase)
			{
				var index = id - CustomBase;

				return index < s_curves.Count ? Evaluate(s_curves[index], t) : t;
			}

			return easing switch
			{
				Easing.Linear => t,
				Easing.InQuad => t * t,
				Easing.OutQuad => t * (2f - t),
				Easing.OutCubic => 1f - (1f - t) * (1f - t) * (1f - t),
				Easing.OutBack => OutBack(t),
				_ => t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t,
			};
		}

		private static bool Intern(Curve curve, out int id)
		{
			if (!s_ids.TryGetValue(curve, out var index))
			{
				index = s_curves.Count;
				s_curves.Add(curve);
				s_ids[curve] = index;
			}

			id = CustomBase + index;

			return true;
		}

		private static float Evaluate(in Curve curve, float t)
		{
			return curve.Kind == CurveKind.Steps ? Steps(curve, t) : Bezier(curve, t);
		}

		/// <summary>
		/// CSS step semantics: how many jumps the run makes, and whether the first and last happen at
		/// its edges.
		/// </summary>
		private static float Steps(in Curve curve, float t)
		{
			var n = curve.Steps;

			if (t >= 1f)
				return 1f;

			var step = Mathf.Floor(Mathf.Max(t, 0f) * n);

			return curve.Position switch
			{
				StepPosition.Start => (step + 1f) / n,
				StepPosition.None => Mathf.Min(step, n - 1) / (n - 1),
				StepPosition.Both => (step + 1f) / (n + 1),
				_ => step / n,
			};
		}

		/// <summary>
		/// Solves the curve's x for the given time, then returns its y — Newton's method first, with a
		/// bisection fallback where the slope is too flat for Newton to converge.
		/// </summary>
		private static float Bezier(in Curve curve, float x)
		{
			if (x <= 0f)
				return 0f;

			if (x >= 1f)
				return 1f;

			var cx = 3f * curve.X1;
			var bx = 3f * (curve.X2 - curve.X1) - cx;
			var ax = 1f - cx - bx;
			var cy = 3f * curve.Y1;
			var by = 3f * (curve.Y2 - curve.Y1) - cy;
			var ay = 1f - cy - by;

			var u = x;

			for (var i = 0; i < 8; i++)
			{
				var error = ((ax * u + bx) * u + cx) * u - x;

				if (Mathf.Abs(error) < 1e-5f)
					return ((ay * u + by) * u + cy) * u;

				var slope = (3f * ax * u + 2f * bx) * u + cx;

				if (Mathf.Abs(slope) < 1e-6f)
					break;

				u -= error / slope;
			}

			var low = 0f;
			var high = 1f;
			u = x;

			for (var i = 0; i < 32; i++)
			{
				var value = ((ax * u + bx) * u + cx) * u;

				if (Mathf.Abs(value - x) < 1e-5f)
					break;

				if (value < x)
					low = u;
				else
					high = u;

				u = (low + high) * 0.5f;
			}

			return ((ay * u + by) * u + cy) * u;
		}

		private static float OutBack(float t)
		{
			const float c1 = 1.70158f;
			const float c3 = c1 + 1f;
			var p = t - 1f;

			return 1f + c3 * p * p * p + c1 * p * p;
		}

		private static bool TryNumber(string text, out float value)
		{
			return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}
	}
}

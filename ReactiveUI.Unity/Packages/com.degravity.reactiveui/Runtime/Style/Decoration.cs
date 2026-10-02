using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	internal interface IVarDependent
	{
		bool HasVars { get; }

		object? Substitute(IReadOnlyDictionary<int, StyleValue> scope);
	}

	/// <summary>
	/// A colour that is either literal or read from a custom property, optionally with its alpha
	/// replaced — <c>rgba(var(--accent), .2)</c>.
	/// </summary>
	/// <remarks>
	/// The alpha override is what lets a theme define each colour once: every translucent wash of it
	/// is written against the one variable instead of being a token of its own per opacity.
	/// </remarks>
	internal readonly struct VarColor : IEquatable<VarColor>
	{
		private const float KeepAlpha = -1f;

		internal bool IsVar => _varId != 0;

		internal readonly Color _value;
		internal readonly int _varId;
		internal readonly float _alpha;

		internal VarColor(Color value)
		{
			_value = value;
			_varId = 0;
			_alpha = KeepAlpha;
		}

		internal VarColor(int varId, float alpha = KeepAlpha)
		{
			_value = Color.clear;
			_varId = varId;
			_alpha = alpha;
		}

		internal bool TryResolve(IReadOnlyDictionary<int, StyleValue> scope, out Color color)
		{
			if (_varId == 0)
			{
				color = _value;
				return true;
			}

			if (!scope.TryGetValue(_varId, out var value))
			{
				color = Color.clear;
				return false;
			}

			if (value.Kind == StyleValueKind.Color)
			{
				color = value.AsColor();
			}
			else if (value.Reference is VarColorValue derived && derived._ink._varId != _varId && derived._ink.TryResolve(scope, out var resolved))
			{
				// A token built from another — `--scrim: rgba(var(--shade), .5)` — read inside a gradient,
				// shadow or another rgba(var()).
				color = resolved;
			}
			else
			{
				color = Color.clear;
				return false;
			}

			if (_alpha >= 0f)
				color.a = _alpha;

			return true;
		}

		public bool Equals(VarColor other)
		{
			return _varId == other._varId && _value.Equals(other._value) && _alpha.Equals(other._alpha);
		}

		public override bool Equals(object? obj)
		{
			return obj is VarColor other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(_value, _varId, _alpha);
		}
	}

	/// <summary>
	/// A colour property's value that reads a custom property through <c>rgba(var(--x), a)</c>.
	/// </summary>
	/// <remarks>
	/// A plain <c>var(--x)</c> is a reference the cascade swaps wholesale; this wraps one because the
	/// alpha has to be applied after the swap.
	/// </remarks>
	internal sealed class VarColorValue : IVarDependent, IEquatable<VarColorValue>
	{
		internal readonly VarColor _ink;

		internal VarColorValue(VarColor ink)
		{
			_ink = ink;
		}

		public bool HasVars => true;

		public object? Substitute(IReadOnlyDictionary<int, StyleValue> scope)
		{
			return _ink.TryResolve(scope, out var color) ? color : null;
		}

		public bool Equals(VarColorValue? other) => other is not null && _ink.Equals(other._ink);

		public override bool Equals(object? obj) => obj is VarColorValue other && Equals(other);

		public override int GetHashCode() => _ink.GetHashCode();
	}

	/// <summary>
	/// The CSS <c>border-style</c> keywords.
	/// </summary>
	/// <remarks>
	/// A border with no <c>border-style</c> is <see cref="Solid"/>, unlike CSS where it is <c>none</c>.
	/// </remarks>
	public enum BorderStyle : byte
	{
		Solid = 0,
		None,
		Hidden,
		Dashed,
		Dotted,
		Double,
		Groove,
		Ridge,
		Inset,
		Outset,
	}

	/// <summary>
	/// The four border edges as they will be painted — a width, a colour and a style each, in CSS
	/// order.
	/// </summary>
	/// <remarks>
	/// Yoga has always reserved the four widths separately, so a per-side width already moved the
	/// content; until 2026-08-16 only the top edge's width and colour reached the painter, which drew
	/// one uniform stroke. Carrying all four here is what lets the two agree.
	/// </remarks>
	internal readonly struct BorderPaint : IEquatable<BorderPaint>
	{
		public readonly float Top;
		public readonly float Right;
		public readonly float Bottom;
		public readonly float Left;

		public readonly Color TopColor;
		public readonly Color RightColor;
		public readonly Color BottomColor;
		public readonly Color LeftColor;

		public readonly BorderStyle TopStyle;
		public readonly BorderStyle RightStyle;
		public readonly BorderStyle BottomStyle;
		public readonly BorderStyle LeftStyle;

		public BorderPaint(
			float top, float right, float bottom, float left,
			Color topColor, Color rightColor, Color bottomColor, Color leftColor)
			: this(top, right, bottom, left, topColor, rightColor, bottomColor, leftColor,
				BorderStyle.Solid, BorderStyle.Solid, BorderStyle.Solid, BorderStyle.Solid)
		{
		}

		public BorderPaint(
			float top, float right, float bottom, float left,
			Color topColor, Color rightColor, Color bottomColor, Color leftColor,
			BorderStyle topStyle, BorderStyle rightStyle, BorderStyle bottomStyle, BorderStyle leftStyle)
		{
			Top = top;
			Right = right;
			Bottom = bottom;
			Left = left;
			TopColor = topColor;
			RightColor = rightColor;
			BottomColor = bottomColor;
			LeftColor = leftColor;
			TopStyle = topStyle;
			RightStyle = rightStyle;
			BottomStyle = bottomStyle;
			LeftStyle = leftStyle;
		}

		/// <summary>
		/// The same widths and styles with different colours, for the four colour channels to drive.
		/// </summary>
		public BorderPaint WithColors(Color top, Color right, Color bottom, Color left) =>
			new(Top, Right, Bottom, Left, top, right, bottom, left,
				TopStyle, RightStyle, BottomStyle, LeftStyle);

		/// <summary>Whether any edge would actually put ink on the screen.</summary>
		public bool Any =>
			Draws(0) || Draws(1) || Draws(2) || Draws(3);

		public float WidthOf(int side) => side switch
		{
			0 => Top,
			1 => Right,
			2 => Bottom,
			_ => Left,
		};

		public Color ColorOf(int side) => side switch
		{
			0 => TopColor,
			1 => RightColor,
			2 => BottomColor,
			_ => LeftColor,
		};

		public BorderStyle StyleOf(int side) => side switch
		{
			0 => TopStyle,
			1 => RightStyle,
			2 => BottomStyle,
			_ => LeftStyle,
		};

		/// <summary>Whether one edge has a width, a visible colour and a style that paints.</summary>
		public bool Draws(int side) =>
			WidthOf(side) > 0f
			&& ColorOf(side).a > 0f
			&& StyleOf(side) is not (BorderStyle.None or BorderStyle.Hidden);

		public bool Equals(BorderPaint other) =>
			Top.Equals(other.Top) && Right.Equals(other.Right)
			&& Bottom.Equals(other.Bottom) && Left.Equals(other.Left)
			&& TopColor == other.TopColor && RightColor == other.RightColor
			&& BottomColor == other.BottomColor && LeftColor == other.LeftColor
			&& TopStyle == other.TopStyle && RightStyle == other.RightStyle
			&& BottomStyle == other.BottomStyle && LeftStyle == other.LeftStyle;

		public override bool Equals(object? obj) => obj is BorderPaint other && Equals(other);

		public override int GetHashCode() =>
			HashCode.Combine(
				HashCode.Combine(Top, Right, Bottom, Left),
				HashCode.Combine(TopStyle, RightStyle, BottomStyle, LeftStyle),
				TopColor, RightColor, BottomColor, LeftColor);
	}

	/// <summary>
	/// One <c>box-shadow</c> entry, in the units it was authored in.
	/// </summary>
	/// <remarks>
	/// Lengths stay unresolved so a shadow written in <c>rem</c> scales with the rest of the UI when
	/// the root size changes, rather than being frozen at whatever it meant when the sheet was built.
	/// </remarks>
	internal readonly struct Shadow : IEquatable<Shadow>
	{
		public Color Color => _ink._value;

		public readonly StyleLength OffsetX;
		public readonly StyleLength OffsetY;
		public readonly StyleLength Blur;
		public readonly StyleLength Spread;

		internal readonly VarColor _ink;

		public Shadow(StyleLength offsetX, StyleLength offsetY, StyleLength blur, StyleLength spread, Color color)
			: this(offsetX, offsetY, blur, spread, new VarColor(color))
		{
		}

		internal Shadow(StyleLength offsetX, StyleLength offsetY, StyleLength blur, StyleLength spread, VarColor ink)
		{
			OffsetX = offsetX;
			OffsetY = offsetY;
			Blur = blur;
			Spread = spread;
			_ink = ink;
		}

		public bool Equals(Shadow other)
		{
			return OffsetX.Equals(other.OffsetX)
				&& OffsetY.Equals(other.OffsetY)
				&& Blur.Equals(other.Blur)
				&& Spread.Equals(other.Spread)
				&& _ink.Equals(other._ink);
		}

		public override bool Equals(object? obj)
		{
			return obj is Shadow other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(OffsetX, OffsetY, Blur, Spread, _ink);
		}
	}

	/// <summary>
	/// The comma-separated list one <c>box-shadow</c> declaration produces, first entry on top.
	/// </summary>
	internal sealed class ShadowList : IEquatable<ShadowList>, IVarDependent
	{
		public int Count => _shadows.Length;

		bool IVarDependent.HasVars => _hasVars;

		private readonly Shadow[] _shadows;
		private readonly bool _hasVars;

		public ShadowList(params Shadow[] shadows)
		{
			_shadows = shadows;

			for (var i = 0; i < shadows.Length && !_hasVars; i++)
				_hasVars = shadows[i]._ink.IsVar;
		}

		public Shadow this[int index] => _shadows[index];

		object? IVarDependent.Substitute(IReadOnlyDictionary<int, StyleValue> scope)
		{
			var resolved = new Shadow[_shadows.Length];

			for (var i = 0; i < _shadows.Length; i++)
			{
				var shadow = _shadows[i];

				// An unresolvable reference drops the whole declaration, as CSS does — a shadow with
				// one silently-black entry would be worse than no shadow.
				if (!shadow._ink.TryResolve(scope, out var color)) return null;

				resolved[i] = new Shadow(shadow.OffsetX, shadow.OffsetY, shadow.Blur, shadow.Spread, color);
			}

			return new ShadowList(resolved);
		}

		public bool Equals(ShadowList? other)
		{
			if (other is null || other._shadows.Length != _shadows.Length) return false;

			for (var i = 0; i < _shadows.Length; i++)
			{
				if (!_shadows[i].Equals(other._shadows[i])) return false;
			}

			return true;
		}

		public override bool Equals(object? obj) => Equals(obj as ShadowList);

		public override int GetHashCode()
		{
			var hash = _shadows.Length;

			for (var i = 0; i < _shadows.Length; i++) hash = HashCode.Combine(hash, _shadows[i]);

			return hash;
		}

		/// <summary>Resolves every entry to pixels, flipping Y into Unity's upward-positive space.</summary>
		internal void Resolve(List<ResolvedShadow> into, in StyleContext ctx)
		{
			into.Clear();

			for (var i = 0; i < _shadows.Length; i++)
			{
				var shadow = _shadows[i];

				into.Add(new ResolvedShadow(
					new Vector2(ctx.Resolve(shadow.OffsetX), -ctx.Resolve(shadow.OffsetY)),
					ctx.Resolve(shadow.Blur),
					ctx.Resolve(shadow.Spread),
					shadow._ink._value));
			}
		}
	}

	/// <summary>
	/// A repeating pattern painted over the fill — a two-tone checker (<c>-rui-checker: 50px rgba(…)</c>)
	/// or, given a line width, a hairline grid (<c>-rui-grid: 48px 1px rgba(…)</c>).
	/// </summary>
	/// <remarks>
	/// Vendor-prefixed because it is not CSS. The alternative spellings, a
	/// <c>repeating-conic-gradient</c> or a tiled <c>linear-gradient</c> with <c>background-size</c>,
	/// would mean building a gradient engine able to express something the mesh painter already draws
	/// in one pass. Both names set the same property, so the later declaration wins.
	/// </remarks>
	internal sealed class Checker : IEquatable<Checker>, IVarDependent
	{
		public readonly StyleLength CellSize;

		/// <summary>Zero paints the checker; anything wider paints grid lines of that width.</summary>
		public readonly StyleLength LineWidth;

		internal readonly VarColor Ink;

		public Color Color => Ink._value;

		bool IVarDependent.HasVars => Ink.IsVar;

		public Checker(StyleLength cellSize, Color color) : this(cellSize, default, new VarColor(color))
		{
		}

		public Checker(StyleLength cellSize, StyleLength lineWidth, Color color) : this(cellSize, lineWidth, new VarColor(color))
		{
		}

		internal Checker(StyleLength cellSize, StyleLength lineWidth, VarColor ink)
		{
			CellSize = cellSize;
			LineWidth = lineWidth;
			Ink = ink;
		}

		object? IVarDependent.Substitute(IReadOnlyDictionary<int, StyleValue> scope)
		{
			return Ink.TryResolve(scope, out var color) ? new Checker(CellSize, LineWidth, color) : null;
		}

		public bool Equals(Checker? other)
		{
			return other is not null
				&& CellSize.Equals(other.CellSize)
				&& LineWidth.Equals(other.LineWidth)
				&& Ink.Equals(other.Ink);
		}

		public override bool Equals(object? obj)
		{
			return Equals(obj as Checker);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(CellSize, LineWidth, Ink);
		}
	}
}

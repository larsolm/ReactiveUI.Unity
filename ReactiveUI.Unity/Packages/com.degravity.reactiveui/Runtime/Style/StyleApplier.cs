using ReactiveUI.Yoga;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Context every length resolves against.
	/// </summary>
	internal readonly struct StyleContext
	{
		/// <summary>
		/// Pixels per <c>rem</c> — the knob the whole UI scales by.
		/// </summary>
		public readonly float RemSize;

		public StyleContext(float remSize)
		{
			RemSize = remSize;
		}

		public float Resolve(StyleLength length) => length.Unit switch
		{
			LengthUnit.Rem => length.Value * RemSize,
			_ => length.Value,
		};

		public YogaValue ToYoga(StyleLength length) => length.Unit switch
		{
			LengthUnit.Auto => YogaValue.Auto(),
			LengthUnit.Percent => YogaValue.Percent(length.Value),
			LengthUnit.Rem => YogaValue.Point(length.Value * RemSize),
			_ => YogaValue.Point(length.Value),
		};
	}

	internal static class StyleApplier
	{
		internal static void ApplyLayout(YogaNode node, ComputedStyle style, in StyleContext ctx)
		{
			node.Display = (YogaDisplay)style.Keyword(PropId.Display, (int)YogaDisplay.Flex);
			node.PositionType = (YogaPositionType)style.Keyword(PropId.Position, (int)YogaPositionType.Relative);
			node.FlexDirection = (YogaFlexDirection)style.Keyword(PropId.FlexDirection, (int)YogaFlexDirection.Column);
			node.Wrap = (YogaWrap)style.Keyword(PropId.FlexWrap, (int)YogaWrap.NoWrap);
			node.JustifyContent = (YogaJustify)style.Keyword(PropId.JustifyContent, (int)YogaJustify.FlexStart);
			node.AlignItems = (YogaAlign)style.Keyword(PropId.AlignItems, (int)YogaAlign.Stretch);
			node.AlignSelf = (YogaAlign)style.Keyword(PropId.AlignSelf, (int)YogaAlign.Auto);
			node.AlignContent = (YogaAlign)style.Keyword(PropId.AlignContent, (int)YogaAlign.FlexStart);
			node.Overflow = (YogaOverflow)style.Keyword(PropId.Overflow, (int)YogaOverflow.Visible);

			node.FlexGrow = style.Number(PropId.FlexGrow, 0f);
			node.FlexShrink = style.Number(PropId.FlexShrink, 1f);
			node.FlexBasis = Value(style, PropId.FlexBasis, ctx, YogaValue.Auto());
			node.AspectRatio = style.Number(PropId.AspectRatio, float.NaN);

			node.Width = Value(style, PropId.Width, ctx, YogaValue.Auto());
			node.Height = Value(style, PropId.Height, ctx, YogaValue.Auto());
			node.MinWidth = Value(style, PropId.MinWidth, ctx, YogaValue.Undefined());
			node.MinHeight = Value(style, PropId.MinHeight, ctx, YogaValue.Undefined());
			node.MaxWidth = Value(style, PropId.MaxWidth, ctx, YogaValue.Undefined());
			node.MaxHeight = Value(style, PropId.MaxHeight, ctx, YogaValue.Undefined());

			node.Left = Value(style, PropId.Left, ctx, YogaValue.Undefined());
			node.Right = Value(style, PropId.Right, ctx, YogaValue.Undefined());
			node.Top = Value(style, PropId.Top, ctx, YogaValue.Undefined());
			node.Bottom = Value(style, PropId.Bottom, ctx, YogaValue.Undefined());

			// Clear the grouped edges first so a per-side value cannot be overridden by a stale
			// "all" or "horizontal" value left behind by a previous style.
			node.Padding = YogaValue.Undefined();
			node.PaddingHorizontal = YogaValue.Undefined();
			node.PaddingVertical = YogaValue.Undefined();
			node.PaddingLeft = Value(style, PropId.PaddingLeft, ctx, YogaValue.Point(0f));
			node.PaddingRight = Value(style, PropId.PaddingRight, ctx, YogaValue.Point(0f));
			node.PaddingTop = Value(style, PropId.PaddingTop, ctx, YogaValue.Point(0f));
			node.PaddingBottom = Value(style, PropId.PaddingBottom, ctx, YogaValue.Point(0f));

			node.Margin = YogaValue.Undefined();
			node.MarginHorizontal = YogaValue.Undefined();
			node.MarginVertical = YogaValue.Undefined();
			node.MarginLeft = Value(style, PropId.MarginLeft, ctx, YogaValue.Point(0f));
			node.MarginRight = Value(style, PropId.MarginRight, ctx, YogaValue.Point(0f));
			node.MarginTop = Value(style, PropId.MarginTop, ctx, YogaValue.Point(0f));
			node.MarginBottom = Value(style, PropId.MarginBottom, ctx, YogaValue.Point(0f));

			node.RowGap = Value(style, PropId.RowGap, ctx, YogaValue.Undefined());
			node.ColumnGap = Value(style, PropId.ColumnGap, ctx, YogaValue.Undefined());

			// The border box is what a stroke is painted inside, so Yoga has to reserve it — but only
			// for an edge that is actually drawn. CSS computes the width of a `none` or `hidden` edge
			// to zero, which is what makes `border-style: none` give the content its space back
			// rather than leaving a transparent gutter behind.
			node.BorderLeftWidth = Width(style, PropId.BorderLeftWidth, PropId.BorderLeftStyle, ctx);
			node.BorderRightWidth = Width(style, PropId.BorderRightWidth, PropId.BorderRightStyle, ctx);
			node.BorderTopWidth = Width(style, PropId.BorderTopWidth, PropId.BorderTopStyle, ctx);
			node.BorderBottomWidth = Width(style, PropId.BorderBottomWidth, PropId.BorderBottomStyle, ctx);
		}

		/// <remarks>
		/// A sheet writes a length, so a <c>rem</c> border scales with the rest of the UI; a width set
		/// from code through <see cref="InlineStyle.SetBorder"/> is a bare number of pixels.
		/// </remarks>
		private static float Width(ComputedStyle style, PropId width, PropId edgeStyle, in StyleContext ctx)
		{
			if (Style(style, edgeStyle) is BorderStyle.None or BorderStyle.Hidden || !style.TryGet(width, out var value))
				return 0f;

			return value.Kind == StyleValueKind.Length ? ctx.Resolve(value.AsLength()) : value.AsNumber();
		}

		private static BorderStyle Style(ComputedStyle style, PropId edgeStyle) =>
			(BorderStyle)style.Keyword(edgeStyle, (int)BorderStyle.Solid);

		private static YogaValue Value(ComputedStyle style, PropId id, in StyleContext ctx, YogaValue fallback)
		{
			return style.TryGet(id, out var value) ? ctx.ToYoga(value.AsLength()) : fallback;
		}

		internal static BorderPaint Borders(ComputedStyle style, in StyleContext ctx)
		{
			var transparent = new Color(0f, 0f, 0f, 0f);

			return new BorderPaint(
				Width(style, PropId.BorderTopWidth, PropId.BorderTopStyle, ctx),
				Width(style, PropId.BorderRightWidth, PropId.BorderRightStyle, ctx),
				Width(style, PropId.BorderBottomWidth, PropId.BorderBottomStyle, ctx),
				Width(style, PropId.BorderLeftWidth, PropId.BorderLeftStyle, ctx),
				style.Color(PropId.BorderTopColor, transparent),
				style.Color(PropId.BorderRightColor, transparent),
				style.Color(PropId.BorderBottomColor, transparent),
				style.Color(PropId.BorderLeftColor, transparent),
				Style(style, PropId.BorderTopStyle),
				Style(style, PropId.BorderRightStyle),
				Style(style, PropId.BorderBottomStyle),
				Style(style, PropId.BorderLeftStyle));
		}

		internal static Vector4 Corners(ComputedStyle style, in StyleContext ctx)
		{
			return new(
				ctx.Resolve(style.Length(PropId.BorderTopLeftRadius)),
				ctx.Resolve(style.Length(PropId.BorderTopRightRadius)),
				ctx.Resolve(style.Length(PropId.BorderBottomRightRadius)),
				ctx.Resolve(style.Length(PropId.BorderBottomLeftRadius)));
		}
	}
}

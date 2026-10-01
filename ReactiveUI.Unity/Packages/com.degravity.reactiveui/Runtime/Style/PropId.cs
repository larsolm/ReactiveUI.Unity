namespace ReactiveUI
{
	/// <summary>
	/// Every style property the framework understands. A computed style is a sorted array keyed
	/// on this, so adding a property costs one entry here plus one line in the property registry —
	/// not a field on a god object plus a merge, a comparer and a hash.
	/// </summary>
	public enum PropId : ushort
	{
		None = 0,

		// ---- Layout (written to Yoga) ----
		Display,
		Position,
		Left,
		Right,
		Top,
		Bottom,
		FlexDirection,
		FlexWrap,
		JustifyContent,
		AlignItems,
		AlignSelf,
		AlignContent,
		FlexGrow,
		FlexShrink,
		FlexBasis,
		Width,
		Height,
		MinWidth,
		MinHeight,
		MaxWidth,
		MaxHeight,
		AspectRatio,
		Overflow,
		PaddingLeft,
		PaddingRight,
		PaddingTop,
		PaddingBottom,
		MarginLeft,
		MarginRight,
		MarginTop,
		MarginBottom,
		RowGap,
		ColumnGap,

		// ---- Visual ----
		BackgroundColor,
		BackgroundImage,
		BackgroundGradient,
		Opacity,
		BorderLeftWidth,
		BorderRightWidth,
		BorderTopWidth,
		BorderBottomWidth,
		BorderLeftStyle,
		BorderRightStyle,
		BorderTopStyle,
		BorderBottomStyle,
		BorderLeftColor,
		BorderRightColor,
		BorderTopColor,
		BorderBottomColor,
		BorderTopLeftRadius,
		BorderTopRightRadius,
		BorderBottomRightRadius,
		BorderBottomLeftRadius,
		BoxShadow,
		Checker,
		Visibility,

		// ---- Transform ----
		TranslateX,
		TranslateY,
		Scale,
		Rotation,
		TransformOriginX,
		TransformOriginY,

		// ---- Text (inherited) ----
		Color,
		FontFamily,
		FontSize,
		FontWeight,
		TextAlign,
		VerticalAlign,
		WhiteSpace,
		LineHeight,
		LetterSpacing,
		WordSpacing,
		TextTransform,

		// ---- Transitions ----
		TransitionProperty,
		TransitionDuration,
		TransitionTimingFunction,
		TransitionDelay,

		// ---- Animations ----
		AnimationName,
		AnimationDuration,
		AnimationTimingFunction,
		AnimationDelay,
		AnimationIterationCount,
		AnimationDirection,
		AnimationFillMode,
		AnimationPlayState,
	}

	/// <summary>
	/// A property whose value is a <see cref="StyleLength"/>.
	/// </summary>
	public readonly struct LengthProp
	{
		internal readonly PropId _id;

		internal LengthProp(PropId id)
		{
			_id = id;
		}
	}

	/// <summary>
	/// A property whose value is a colour.
	/// </summary>
	public readonly struct ColorProp
	{
		internal readonly PropId _id;

		internal ColorProp(PropId id)
		{
			_id = id;
		}
	}

	/// <summary>
	/// A property whose value is a bare number.
	/// </summary>
	public readonly struct FloatProp
	{
		internal readonly PropId _id;
		internal FloatProp(PropId id)
		{
			_id = id;
		}
	}

	/// <summary>
	/// Typed handles for the properties that are worth setting from C# — gameplay-driven values
	/// that cannot live in a stylesheet. Anything static belongs in CSS instead.
	/// </summary>
	public static class Css
	{
		public static readonly LengthProp Left = new(PropId.Left);
		public static readonly LengthProp Right = new(PropId.Right);
		public static readonly LengthProp Top = new(PropId.Top);
		public static readonly LengthProp Bottom = new(PropId.Bottom);
		public static readonly LengthProp Width = new(PropId.Width);
		public static readonly LengthProp Height = new(PropId.Height);
		public static readonly LengthProp MinHeight = new(PropId.MinHeight);
		public static readonly LengthProp FontSize = new(PropId.FontSize);
		public static readonly LengthProp TranslateX = new(PropId.TranslateX);
		public static readonly LengthProp TranslateY = new(PropId.TranslateY);
		public static readonly ColorProp BackgroundColor = new(PropId.BackgroundColor);
		public static readonly ColorProp Color = new(PropId.Color);
		public static readonly ColorProp BorderColor = new(PropId.BorderTopColor);
		public static readonly FloatProp Opacity = new(PropId.Opacity);
		public static readonly FloatProp FlexGrow = new(PropId.FlexGrow);
		public static readonly FloatProp Rotation = new(PropId.Rotation);
		public static readonly FloatProp Scale = new(PropId.Scale);
	}
}

namespace ReactiveUI
{
	/// <summary>
	/// Every style property the framework understands.
	/// </summary>
	internal enum PropId : ushort
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
		ScaleX,
		ScaleY,
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
	/// A style property that takes a <see cref="StyleLength"/>.
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
	/// A style property that takes a color.
	/// </summary>
	public readonly struct ColorProp
	{
		internal readonly PropId _id;

		/// Set for a shorthand handle whose <see cref="_id"/> is the top side of a four-sided group.
		internal readonly bool _allSides;

		internal ColorProp(PropId id, bool allSides = false)
		{
			_id = id;
			_allSides = allSides;
		}
	}

	/// <summary>
	/// A style property that takes a number.
	/// </summary>
	public readonly struct FloatProp
	{
		internal readonly PropId _id;

		/// Set for the uniform scale handle, whose <see cref="_id"/> is the X axis of the pair.
		internal readonly bool _bothAxes;

		internal FloatProp(PropId id, bool bothAxes = false)
		{
			_id = id;
			_bothAxes = bothAxes;
		}
	}

	/// <summary>
	/// Style properties that can be set through <see cref="InlineStyle"/>.
	/// </summary>
	public static class Css
	{
		/// <summary>The <c>left</c> property.</summary>
		public static readonly LengthProp Left = new(PropId.Left);

		/// <summary>The <c>right</c> property.</summary>
		public static readonly LengthProp Right = new(PropId.Right);

		/// <summary>The <c>top</c> property.</summary>
		public static readonly LengthProp Top = new(PropId.Top);

		/// <summary>The <c>bottom</c> property.</summary>
		public static readonly LengthProp Bottom = new(PropId.Bottom);

		/// <summary>The <c>width</c> property.</summary>
		public static readonly LengthProp Width = new(PropId.Width);

		/// <summary>The <c>height</c> property.</summary>
		public static readonly LengthProp Height = new(PropId.Height);

		/// <summary>The <c>min-height</c> property.</summary>
		public static readonly LengthProp MinHeight = new(PropId.MinHeight);

		/// <summary>The <c>font-size</c> property.</summary>
		public static readonly LengthProp FontSize = new(PropId.FontSize);

		/// <summary>The horizontal translation of <c>transform</c>.</summary>
		public static readonly LengthProp TranslateX = new(PropId.TranslateX);

		/// <summary>The vertical translation of <c>transform</c>.</summary>
		public static readonly LengthProp TranslateY = new(PropId.TranslateY);

		/// <summary>The <c>background-color</c> property.</summary>
		public static readonly ColorProp BackgroundColor = new(PropId.BackgroundColor);

		/// <summary>The <c>color</c> property.</summary>
		public static readonly ColorProp Color = new(PropId.Color);

		/// <summary>The <c>border-color</c> property, applied to all four sides.</summary>
		public static readonly ColorProp BorderColor = new(PropId.BorderTopColor, allSides: true);

		/// <summary>The <c>opacity</c> property.</summary>
		public static readonly FloatProp Opacity = new(PropId.Opacity);

		/// <summary>The <c>flex-grow</c> property.</summary>
		public static readonly FloatProp FlexGrow = new(PropId.FlexGrow);

		/// <summary>The rotation of <c>transform</c>, in degrees.</summary>
		public static readonly FloatProp Rotation = new(PropId.Rotation);

		/// <summary>The uniform scale of <c>transform</c>, applied to both axes.</summary>
		public static readonly FloatProp Scale = new(PropId.ScaleX, bothAxes: true);

		/// <summary>The horizontal scale of <c>transform</c>.</summary>
		public static readonly FloatProp ScaleX = new(PropId.ScaleX);

		/// <summary>The vertical scale of <c>transform</c>.</summary>
		public static readonly FloatProp ScaleY = new(PropId.ScaleY);
	}
}

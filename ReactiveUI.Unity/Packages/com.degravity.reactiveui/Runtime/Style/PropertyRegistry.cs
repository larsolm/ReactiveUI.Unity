using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	internal enum ValueSyntax : byte
	{
		Length,
		Number,
		Color,
		Keyword,
		FontReference,
		ImageReference,
		Transform,
		Time,
		PropertyName,
		Shadow,
		Checker,
		LetterSpacing,
		AnimationName,
		Count,
	}

	internal readonly struct PropertyInfo
	{
		internal readonly PropId _id;
		internal readonly ValueSyntax _syntax;
		internal readonly bool _inherited;
		internal readonly KeywordSet _keywords;

		internal PropertyInfo(PropId id, ValueSyntax syntax, bool inherited = false, KeywordSet keywords = KeywordSet.None)
		{
			_id = id;
			_syntax = syntax;
			_inherited = inherited;
			_keywords = keywords;
		}
	}

	internal enum KeywordSet : byte
	{
		None,
		Display,
		Position,
		FlexDirection,
		FlexWrap,
		JustifyContent,
		AlignItems,
		AlignSelf,
		AlignContent,
		Overflow,
		TextAlign,
		VerticalAlign,
		WhiteSpace,
		FontWeight,
		TextTransform,
		Visibility,
		BorderStyle,
		Easing,
		AnimationDirection,
		AnimationFillMode,
		AnimationPlayState,
	}

	[NoAutoStaticsCleanup]
	internal static class PropertyRegistry
	{
		private static readonly Dictionary<string, PropertyInfo> s_byName = new(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<PropId, PropertyInfo> s_byId = new();
		private static readonly HashSet<PropId> s_inherited = new();

		static PropertyRegistry()
		{
			// ---- Layout ----
			Add("display", PropId.Display, ValueSyntax.Keyword, keywords: KeywordSet.Display);
			Add("position", PropId.Position, ValueSyntax.Keyword, keywords: KeywordSet.Position);
			Add("left", PropId.Left, ValueSyntax.Length);
			Add("right", PropId.Right, ValueSyntax.Length);
			Add("top", PropId.Top, ValueSyntax.Length);
			Add("bottom", PropId.Bottom, ValueSyntax.Length);
			Add("flex-direction", PropId.FlexDirection, ValueSyntax.Keyword, keywords: KeywordSet.FlexDirection);
			Add("flex-wrap", PropId.FlexWrap, ValueSyntax.Keyword, keywords: KeywordSet.FlexWrap);
			Add("justify-content", PropId.JustifyContent, ValueSyntax.Keyword, keywords: KeywordSet.JustifyContent);
			Add("align-items", PropId.AlignItems, ValueSyntax.Keyword, keywords: KeywordSet.AlignItems);
			Add("align-self", PropId.AlignSelf, ValueSyntax.Keyword, keywords: KeywordSet.AlignSelf);
			Add("align-content", PropId.AlignContent, ValueSyntax.Keyword, keywords: KeywordSet.AlignContent);
			Add("flex-grow", PropId.FlexGrow, ValueSyntax.Number);
			Add("flex-shrink", PropId.FlexShrink, ValueSyntax.Number);
			Add("flex-basis", PropId.FlexBasis, ValueSyntax.Length);
			Add("width", PropId.Width, ValueSyntax.Length);
			Add("height", PropId.Height, ValueSyntax.Length);
			Add("min-width", PropId.MinWidth, ValueSyntax.Length);
			Add("min-height", PropId.MinHeight, ValueSyntax.Length);
			Add("max-width", PropId.MaxWidth, ValueSyntax.Length);
			Add("max-height", PropId.MaxHeight, ValueSyntax.Length);
			Add("aspect-ratio", PropId.AspectRatio, ValueSyntax.Number);
			Add("overflow", PropId.Overflow, ValueSyntax.Keyword, keywords: KeywordSet.Overflow);
			Add("padding-left", PropId.PaddingLeft, ValueSyntax.Length);
			Add("padding-right", PropId.PaddingRight, ValueSyntax.Length);
			Add("padding-top", PropId.PaddingTop, ValueSyntax.Length);
			Add("padding-bottom", PropId.PaddingBottom, ValueSyntax.Length);
			Add("margin-left", PropId.MarginLeft, ValueSyntax.Length);
			Add("margin-right", PropId.MarginRight, ValueSyntax.Length);
			Add("margin-top", PropId.MarginTop, ValueSyntax.Length);
			Add("margin-bottom", PropId.MarginBottom, ValueSyntax.Length);
			Add("row-gap", PropId.RowGap, ValueSyntax.Length);
			Add("column-gap", PropId.ColumnGap, ValueSyntax.Length);

			// ---- Visual ----
			Add("background-color", PropId.BackgroundColor, ValueSyntax.Color);
			Add("background-image", PropId.BackgroundImage, ValueSyntax.ImageReference);
			Add("opacity", PropId.Opacity, ValueSyntax.Number);
			Add("border-left-width", PropId.BorderLeftWidth, ValueSyntax.Length);
			Add("border-right-width", PropId.BorderRightWidth, ValueSyntax.Length);
			Add("border-top-width", PropId.BorderTopWidth, ValueSyntax.Length);
			Add("border-bottom-width", PropId.BorderBottomWidth, ValueSyntax.Length);
			Add("border-left-style", PropId.BorderLeftStyle, ValueSyntax.Keyword, keywords: KeywordSet.BorderStyle);
			Add("border-right-style", PropId.BorderRightStyle, ValueSyntax.Keyword, keywords: KeywordSet.BorderStyle);
			Add("border-top-style", PropId.BorderTopStyle, ValueSyntax.Keyword, keywords: KeywordSet.BorderStyle);
			Add("border-bottom-style", PropId.BorderBottomStyle, ValueSyntax.Keyword, keywords: KeywordSet.BorderStyle);
			Add("border-left-color", PropId.BorderLeftColor, ValueSyntax.Color);
			Add("border-right-color", PropId.BorderRightColor, ValueSyntax.Color);
			Add("border-top-color", PropId.BorderTopColor, ValueSyntax.Color);
			Add("border-bottom-color", PropId.BorderBottomColor, ValueSyntax.Color);
			Add("border-top-left-radius", PropId.BorderTopLeftRadius, ValueSyntax.Length);
			Add("border-top-right-radius", PropId.BorderTopRightRadius, ValueSyntax.Length);
			Add("border-bottom-right-radius", PropId.BorderBottomRightRadius, ValueSyntax.Length);
			Add("border-bottom-left-radius", PropId.BorderBottomLeftRadius, ValueSyntax.Length);
			Add("box-shadow", PropId.BoxShadow, ValueSyntax.Shadow);

			// Registered only so `transition-property: border-color` resolves, the way `transform`
			// is below: the shorthand itself is expanded into the four sides before the registry is
			// consulted, and BorderTopColor stands for the whole group.
			Add("border-color", PropId.BorderTopColor, ValueSyntax.Color);

			// Inherited, as CSS has it. The one divergence: hiding is spelled as a zero alpha on a
			// CanvasGroup, which multiplies down the subtree, so a descendant cannot win itself back
			// with `visibility: visible`.
			Add("visibility", PropId.Visibility, ValueSyntax.Keyword, inherited: true, keywords: KeywordSet.Visibility);

			// The chunky 3D edge under every button and tile, the accent glow under a selected card
			// and the ring around a live piece are all box-shadows, so this is load-bearing rather
			// than decorative. `-rui-checker` is the one extension: a repeating two-tone grid the
			// mesh painter draws in a single pass. `-rui-grid` is its hairline form, an alias whose
			// extra line width is what tells the painter to draw lines instead of cells.
			Add("-rui-checker", PropId.Checker, ValueSyntax.Checker);
			Add("-rui-grid", PropId.Checker, ValueSyntax.Checker);

			// ---- Transform ----
			// Registered only so `transition-property: transform` resolves; the value itself is a
			// shorthand over four channels and is expanded before the registry is consulted.
			// PropId.TranslateX stands for the whole group, which TransitionFor understands.
			Add("transform", PropId.TranslateX, ValueSyntax.Transform);

			// ---- Text. Inherited, which is the ergonomic win a cascade buys over a style object:
			// a font set on a screen no longer has to be repeated on every text run beneath it. ----
			Add("color", PropId.Color, ValueSyntax.Color, inherited: true);
			Add("font-family", PropId.FontFamily, ValueSyntax.FontReference, inherited: true);
			Add("font-size", PropId.FontSize, ValueSyntax.Length, inherited: true);
			Add("font-weight", PropId.FontWeight, ValueSyntax.Keyword, inherited: true, keywords: KeywordSet.FontWeight);
			Add("text-align", PropId.TextAlign, ValueSyntax.Keyword, inherited: true, keywords: KeywordSet.TextAlign);

			// Nothing like the CSS property of the same name, which positions an inline box against
			// its line. There are no inline boxes here: a text run is a block that TextMeshPro aligns
			// inside, so this is TMP's vertical alignment under a name authors already reach for.
			Add("vertical-align", PropId.VerticalAlign, ValueSyntax.Keyword, inherited: true, keywords: KeywordSet.VerticalAlign);
			Add("white-space", PropId.WhiteSpace, ValueSyntax.Keyword, inherited: true, keywords: KeywordSet.WhiteSpace);
			Add("line-height", PropId.LineHeight, ValueSyntax.Number, inherited: true);
			Add("letter-spacing", PropId.LetterSpacing, ValueSyntax.LetterSpacing, inherited: true);
			Add("word-spacing", PropId.WordSpacing, ValueSyntax.LetterSpacing, inherited: true);
			Add("text-transform", PropId.TextTransform, ValueSyntax.Keyword, inherited: true, keywords: KeywordSet.TextTransform);

			// ---- Transitions ----
			Add("transition-property", PropId.TransitionProperty, ValueSyntax.PropertyName);
			Add("transition-duration", PropId.TransitionDuration, ValueSyntax.Time);
			Add("transition-delay", PropId.TransitionDelay, ValueSyntax.Time);
			Add("transition-timing-function", PropId.TransitionTimingFunction, ValueSyntax.Keyword, keywords: KeywordSet.Easing);

			// ---- Animations ----
			// The `animation` shorthand is expanded by the CSS library before the registry is
			// consulted, exactly as `transition` is, so only the longhands need registering.
			Add("animation-name", PropId.AnimationName, ValueSyntax.AnimationName);
			Add("animation-duration", PropId.AnimationDuration, ValueSyntax.Time);
			Add("animation-delay", PropId.AnimationDelay, ValueSyntax.Time);
			Add("animation-timing-function", PropId.AnimationTimingFunction, ValueSyntax.Keyword, keywords: KeywordSet.Easing);
			Add("animation-iteration-count", PropId.AnimationIterationCount, ValueSyntax.Count);
			Add("animation-direction", PropId.AnimationDirection, ValueSyntax.Keyword, keywords: KeywordSet.AnimationDirection);
			Add("animation-fill-mode", PropId.AnimationFillMode, ValueSyntax.Keyword, keywords: KeywordSet.AnimationFillMode);
			Add("animation-play-state", PropId.AnimationPlayState, ValueSyntax.Keyword, keywords: KeywordSet.AnimationPlayState);
		}

		private static void Add(string name, PropId id, ValueSyntax syntax, bool inherited = false, KeywordSet keywords = KeywordSet.None)
		{
			s_byName[name] = new PropertyInfo(id, syntax, inherited, keywords);
			// First registration wins: later names for the same id are aliases, like `transform`.
			s_byId.TryAdd(id, s_byName[name]);

			if (inherited)
				s_inherited.Add(id);
		}

		internal static bool TryGet(string name, out PropertyInfo info) => s_byName.TryGetValue(name, out info);

		internal static bool TryGet(PropId id, out PropertyInfo info) => s_byId.TryGetValue(id, out info);

		internal static bool IsInherited(PropId id) => s_inherited.Contains(id);

		internal static IReadOnlyCollection<PropId> InheritedProperties => s_inherited;
	}
}

using System.Collections.Generic;
using ReactiveUI.Yoga;
using TMPro;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Horizontal alignment of a text run.
	/// </summary>
	internal enum TextAlign
	{
		Left,
		Center,
		Right,
		Justified,
	}

	/// <summary>
	/// Where a text run sits inside its own box, straight from TextMeshPro's set. <c>Middle</c> is
	/// first because it is what the framework aligned to before the property existed.
	/// </summary>
	internal enum VerticalAlign
	{
		Middle,
		Top,
		Bottom,

		/// <summary>Aligns the first line's baseline to the top of the box.</summary>
		Baseline,

		/// <summary>Centres the rendered glyph geometry rather than the line metrics.</summary>
		Geometry,

		/// <summary>Aligns to the cap height rather than the ascender.</summary>
		Capline,
	}

	/// <summary>
	/// Whether a text run may wrap.
	/// </summary>
	internal enum WhiteSpace
	{
		Normal,
		NoWrap,
	}

	/// <summary>
	/// Casing applied to a text run on the way to the glyphs.
	/// </summary>
	internal enum TextTransform
	{
		None,
		Uppercase,
		Lowercase,
		Capitalize,
	}

	/// <summary>
	/// A text run, painted like any other box with the glyphs drawn on top.
	/// </summary>
	/// <remarks>
	/// Derives from <see cref="VisualHost"/> so a <c>Text</c> answers to <c>background-color</c>,
	/// <c>border</c>, <c>border-radius</c>, <c>box-shadow</c> and the rest, rather than to the text
	/// properties alone. The glyphs live on the child <see cref="VisualHost.BuildGraphicChild"/>
	/// makes, which is also what gives `padding` on a text run something to do.
	/// </remarks>
	internal sealed class TextHost : VisualHost
	{
		internal override HostKind Kind => HostKind.Text;

		private readonly Dictionary<long, YogaSize> _measureCache = new();

		private TextMeshProUGUI _tmp = null!;
		private string? _content;
		private TextTransform _transform;

		private int _metricsId;

		internal void Build()
		{
			_tmp = BuildGraphicChild("Glyphs").gameObject.AddComponent<TextMeshProUGUI>();
			_tmp.raycastTarget = false;
			_tmp.gameObject.AddComponent<GammaMaterialModifier>();
			_yoga.SetMeasureFunction(Measure);
		}

		internal override void ApplyProps(int node)
		{
			var content = ElementPool.Props<TextProps>(node).Content ?? string.Empty;
			if (string.Equals(content, _content)) return;

			_content = content;
			ApplyContent();
			InvalidateMeasure();
		}

		/// <summary>
		/// Pushes the content onto TMP with the casing <c>text-transform</c> asks for.
		/// </summary>
		/// <remarks>
		/// Upper and lower case are left to TMP's own font styles, which apply during layout and cost
		/// no allocation. <c>capitalize</c> has no TMP equivalent, so it is the one case that rewrites
		/// the string — and therefore the one that has to be re-applied when the style changes rather
		/// than only when the content does.
		/// </remarks>
		private void ApplyContent()
		{
			var content = _content ?? string.Empty;

			_tmp.text = _transform == TextTransform.Capitalize ? Capitalize(content) : content;
		}

		private static string Capitalize(string text)
		{
			var chars = text.ToCharArray();
			var atWordStart = true;

			for (var i = 0; i < chars.Length; i++)
			{
				if (char.IsWhiteSpace(chars[i]))
				{
					atWordStart = true;

					continue;
				}

				if (atWordStart) chars[i] = char.ToUpperInvariant(chars[i]);

				atWordStart = false;
			}

			return new string(chars);
		}

		internal override void ApplyStyle(ComputedStyle style, in StyleContext ctx)
		{
			base.ApplyStyle(style, ctx);

			// The family is resolved here rather than at parse time because the weight it pairs with
			// arrives through the cascade as its own declaration.
			var family = style.Reference<string>(PropId.FontFamily);
			var weight = style.Keyword(PropId.FontWeight, UiFonts.NormalWeight);
			var font = UiFonts.Resolve(family, weight);
			var fontSize = ctx.Resolve(style.Length(PropId.FontSize, new StyleLength(ctx.RemSize)));
			var lineHeight = style.Number(PropId.LineHeight, 1f);
			var letterSpacing = style.Number(PropId.LetterSpacing, 0f);
			var wordSpacing = style.Number(PropId.WordSpacing, 0f);
			var align = (TextAlign)style.Keyword(PropId.TextAlign, (int)TextAlign.Left);
			var verticalAlign = (VerticalAlign)style.Keyword(PropId.VerticalAlign, (int)VerticalAlign.Middle);
			var whiteSpace = (WhiteSpace)style.Keyword(PropId.WhiteSpace, (int)WhiteSpace.Normal);
			var transform = (TextTransform)style.Keyword(PropId.TextTransform, (int)TextTransform.None);

			if (transform != _transform)
			{
				_transform = transform;
				ApplyContent();
			}

			// Only the properties that change the glyph run's size may invalidate the measure
			// cache. Colour is deliberately excluded: with inherited text properties, a colour-only
			// change on an ancestor would otherwise blow every descendant's cache for nothing.
			// Vertical alignment stays out of the metrics key: it moves the run inside a box whose
			// size it does not change, so it can never invalidate a measurement.
			var metrics = Metrics(font, weight, fontSize, lineHeight, letterSpacing, wordSpacing, align, whiteSpace, transform);

			if (metrics != _metricsId)
			{
				_metricsId = metrics;
				InvalidateMeasure();
			}

			if (font != null) _tmp.font = font;

			// Handed to TextMeshPro only when the resolved asset actually carries that face in its
			// own weight table, because a family can spell its weights either way: as separate
			// `@font-face` registrations, or in one asset's table. When it is the table, TMP swaps
			// to an *alternative typeface* — a second font asset, and so a second material, which
			// costs a `TMP SubMeshUI` child GameObject on every text node and leaves the main mesh
			// empty. When it is `@font-face`, `UiFonts.Resolve` has already picked the right face
			// and asking TMP for the weight again would only make it look for another.
			//
			// Passing a weight an asset has no entry for is worse than useless: TMP's lookup for a
			// non-Regular weight returns null outright rather than falling through to the asset's
			// own glyphs, so every character then takes the missing-character path — and that path
			// caches its answer under the *Regular* key it retried with, so the composite key the
			// next lookup builds misses again, every character, every rebuild.
			_tmp.fontWeight = UiFonts.HasWeightFace(font, weight) ? (FontWeight)weight : FontWeight.Regular;
			_tmp.fontSize = fontSize;
			_tmp.characterSpacing = letterSpacing;
			_tmp.wordSpacing = wordSpacing;
			_tmp.lineSpacing = (lineHeight - 1f) * 100f;
			_tmp.fontStyle = Style(transform);

			// The two halves are set separately rather than through the combined `alignment`, which
			// would silently reset whichever half it was not asked about.
			_tmp.horizontalAlignment = Alignment(align);
			_tmp.verticalAlignment = Alignment(verticalAlign);
			_tmp.textWrappingMode = whiteSpace == WhiteSpace.NoWrap ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
		}

		protected override bool PaintsContent => true;

		protected override void PaintContentColor(Color color)
		{
			if (_tmp != null)
				_tmp.color = color;
		}

		private static FontStyles Style(TextTransform transform) => transform switch
		{
			TextTransform.Uppercase => FontStyles.UpperCase,
			TextTransform.Lowercase => FontStyles.LowerCase,
			_ => FontStyles.Normal,
		};

		private static int Metrics(
			TMP_FontAsset? font,
			int weight,
			float size,
			float lineHeight,
			float spacing,
			float wordSpacing,
			TextAlign align,
			WhiteSpace wrap,
			TextTransform transform)
		{
			// Reference identity rather than GetInstanceID, which Unity 6.6 deprecates; the key
			// only has to be stable within a session.
			var hash = font is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(font);

			// Weight belongs in the key on its own account, not just through the asset it picked: a
			// family whose faces live in one asset's weight table swaps typeface without swapping
			// `font`, so the two weights would otherwise share a measurement.
			hash = hash * 397 ^ weight;
			hash = hash * 397 ^ size.GetHashCode();
			hash = hash * 397 ^ lineHeight.GetHashCode();
			hash = hash * 397 ^ spacing.GetHashCode();
			hash = hash * 397 ^ wordSpacing.GetHashCode();
			hash = hash * 397 ^ (int)align;
			hash = hash * 397 ^ (int)wrap;
			hash = hash * 397 ^ (int)transform;

			return hash;
		}

		private void InvalidateMeasure()
		{
			_measureCache.Clear();
			_yoga.MarkDirty();
		}

		private YogaSize Measure(YogaNode node, float width, YogaMeasureMode widthMode, float height, YogaMeasureMode heightMode)
		{
			if (string.IsNullOrEmpty(_tmp.text) || _tmp.font == null) return MeasureOutput.Make(0f, 0f);

			var key = CacheKey(width, widthMode);
			if (_measureCache.TryGetValue(key, out var cached)) return cached;

			var constraint = widthMode == YogaMeasureMode.Undefined ? float.PositiveInfinity : width;
			var preferred = _tmp.GetPreferredValues(_tmp.text, constraint, float.PositiveInfinity);

			var measuredWidth = widthMode switch
			{
				YogaMeasureMode.Exactly => width,
				YogaMeasureMode.AtMost => Mathf.Min(Mathf.Ceil(preferred.x), width),
				_ => Mathf.Ceil(preferred.x),
			};

			var measuredHeight = heightMode == YogaMeasureMode.Exactly ? height : Mathf.Ceil(preferred.y);
			var size = MeasureOutput.Make(measuredWidth, measuredHeight);
			_measureCache[key] = size;

			return size;
		}

		private static long CacheKey(float width, YogaMeasureMode mode)
		{
			var rounded = mode == YogaMeasureMode.Undefined ? -1 : Mathf.RoundToInt(width);

			return ((long)mode << 32) ^ (uint)rounded;
		}

		private static HorizontalAlignmentOptions Alignment(TextAlign align) => align switch
		{
			TextAlign.Center => HorizontalAlignmentOptions.Center,
			TextAlign.Right => HorizontalAlignmentOptions.Right,
			TextAlign.Justified => HorizontalAlignmentOptions.Justified,
			_ => HorizontalAlignmentOptions.Left,
		};

		private static VerticalAlignmentOptions Alignment(VerticalAlign align) => align switch
		{
			VerticalAlign.Top => VerticalAlignmentOptions.Top,
			VerticalAlign.Bottom => VerticalAlignmentOptions.Bottom,
			VerticalAlign.Baseline => VerticalAlignmentOptions.Baseline,
			VerticalAlign.Geometry => VerticalAlignmentOptions.Geometry,
			VerticalAlign.Capline => VerticalAlignmentOptions.Capline,
			_ => VerticalAlignmentOptions.Middle,
		};

		internal override void ResetForPool()
		{
			base.ResetForPool();

			_content = null;
			_transform = TextTransform.None;
			_metricsId = 0;
			_measureCache.Clear();

			if (_tmp != null) _tmp.text = string.Empty;
		}
	}
}

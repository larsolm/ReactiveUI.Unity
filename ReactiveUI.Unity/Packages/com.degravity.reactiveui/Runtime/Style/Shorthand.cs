using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace ReactiveUI
{
	internal static class Shorthand
	{
		private static readonly PropertyInfo s_backgroundImage = new(PropId.BackgroundImage, ValueSyntax.ImageReference);

		internal static bool TryExpand(string name, string value, List<Declaration> into, List<string> diagnostics, string sourceName)
		{
			switch (name.ToLowerInvariant())
			{
				case "padding":
					return Box(value, into, PropId.PaddingTop, PropId.PaddingRight, PropId.PaddingBottom, PropId.PaddingLeft, diagnostics, sourceName, name);

				case "margin":
					return Box(value, into, PropId.MarginTop, PropId.MarginRight, PropId.MarginBottom, PropId.MarginLeft, diagnostics, sourceName, name);

				case "border-radius":
					return Box(value, into, PropId.BorderTopLeftRadius, PropId.BorderTopRightRadius, PropId.BorderBottomRightRadius, PropId.BorderBottomLeftRadius, diagnostics, sourceName, name);

				case "gap":
				{
					var parts = Split(value);
					if (parts.Count is 0 or > 2)
						return Fail(diagnostics, sourceName, name, value);

					if (!Length(parts[0], out var row))
						return Fail(diagnostics, sourceName, name, value);

					var column = row;
					if (parts.Count == 2 && !Length(parts[1], out column))
						return Fail(diagnostics, sourceName, name, value);

					into.Add(new Declaration(PropId.RowGap, row));
					into.Add(new Declaration(PropId.ColumnGap, column));

					return true;
				}

				case "border":
					return Border(value, into, diagnostics, sourceName, name, s_allSides);

				case "border-top":
					return Border(value, into, diagnostics, sourceName, name, s_top);

				case "border-right":
					return Border(value, into, diagnostics, sourceName, name, s_right);

				case "border-bottom":
					return Border(value, into, diagnostics, sourceName, name, s_bottom);

				case "border-left":
					return Border(value, into, diagnostics, sourceName, name, s_left);

				case "border-width":
					return Box(value, into, PropId.BorderTopWidth, PropId.BorderRightWidth, PropId.BorderBottomWidth, PropId.BorderLeftWidth, diagnostics, sourceName, name);

				case "border-color":
					return BorderColor(value, into, diagnostics, sourceName);

				case "border-style":
					return KeywordBox(value, into, PropId.BorderTopStyle, PropId.BorderRightStyle, PropId.BorderBottomStyle, PropId.BorderLeftStyle, KeywordSet.BorderStyle, diagnostics, sourceName, name);

				case "background":
					// Only the colour form is meaningful here; images have their own property.
					if (!ValueParser.TryParseColor(value.Trim(), out var background))
					{
						return Fail(diagnostics, sourceName, name, value);
					}

					into.Add(new Declaration(PropId.BackgroundColor, background));

					return true;

				case "background-image":
				{
					// One CSS property, two very different values: a gradient is painted into the
					// node's own mesh, a resource() becomes its texture. Splitting them here rather
					// than at paint time keeps the applier reading one property per concern.
					var text = value.Trim();

					if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}

					if (ValueParser.TryParseGradient(text, out var gradient))
					{
						into.Add(new Declaration(PropId.BackgroundGradient, gradient));

						return true;
					}

					if (!ValueParser.TryParse(text, s_backgroundImage, out var image))
					{
						return Fail(diagnostics, sourceName, name, value);
					}

					into.Add(new Declaration(PropId.BackgroundImage, image));

					return true;
				}

				case "flex":
					return Flex(value, into, diagnostics, sourceName);

				case "inset":
					return Box(value, into, PropId.Top, PropId.Right, PropId.Bottom, PropId.Left, diagnostics, sourceName, name);

				case "transform":
					return Transform(value, into, diagnostics, sourceName);

				case "transform-origin":
					return TransformOrigin(value, into, diagnostics, sourceName);

				case "transition":
					return Transition(value, into, diagnostics, sourceName);

				default:
					return false;
			}
		}

		/// <summary>
		/// Reads the <c>flex</c> shorthand, including the basis it is really about.
		/// </summary>
		/// <remarks>
		/// The basis is the whole reason the shorthand exists: `flex: 1` means `1 1 0`, so the items
		/// share the container rather than their own content widths. Writing only grow and shrink —
		/// which this did until 2026-08-16 — leaves the basis at `auto` and quietly turns every
		/// `flex: 1` into `flex: 1 1 auto`, which lays out differently the moment the contents differ
		/// in size.
		/// </remarks>
		private static bool Flex(string value, List<Declaration> into, List<string> diagnostics, string sourceName)
		{
			var text = value.Trim();

			// The keyword forms, spelled out because they are not simply "the numbers with defaults".
			if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
				return Flex(0f, 0f, StyleValue.OfLength(StyleLength.Auto), into);

			if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
				return Flex(1f, 1f, StyleValue.OfLength(StyleLength.Auto), into);

			var parts = Split(text);

			if (parts.Count is 0 or > 3)
				return Fail(diagnostics, sourceName, "flex", value);

			if (!float.TryParse(parts[0], out var grow))
				return Fail(diagnostics, sourceName, "flex", value);

			var shrink = 1f;

			// An omitted basis is `0`, not `auto` — that is the difference the remark above is about.
			var basis = StyleValue.OfLength(new StyleLength(0f));
			var next = 1;

			// A bare number in second place is the shrink factor; anything else is already the basis.
			if (next < parts.Count && float.TryParse(parts[next], out var parsedShrink))
			{
				shrink = parsedShrink;
				next++;
			}

			if (next < parts.Count && !Length(parts[next], out basis))
				return Fail(diagnostics, sourceName, "flex", value);

			return Flex(grow, shrink, basis, into);
		}

		private static bool Flex(float grow, float shrink, StyleValue basis, List<Declaration> into)
		{
			into.Add(new Declaration(PropId.FlexGrow, StyleValue.OfNumber(grow)));
			into.Add(new Declaration(PropId.FlexShrink, StyleValue.OfNumber(shrink)));
			into.Add(new Declaration(PropId.FlexBasis, basis));

			return true;
		}

		/// <summary>Applies the 1-to-4 value box rule: all / vertical horizontal / top h bottom / t r b l.</summary>
		private static bool Box(
			string value,
			List<Declaration> into,
			PropId top,
			PropId right,
			PropId bottom,
			PropId left,
			List<string> diagnostics,
			string sourceName,
			string name,
			bool asNumber = false)
		{
			var parts = Split(value);

			if (parts.Count is 0 or > 4)
				return Fail(diagnostics, sourceName, name, value);

			var values = new StyleValue[4];

			for (var i = 0; i < parts.Count; i++)
			{
				if (!(asNumber ? Number(parts[i], out values[i]) : Length(parts[i], out values[i])))
				{
					return Fail(diagnostics, sourceName, name, value);
				}
			}

			var (t, r, b, l) = parts.Count switch
			{
				1 => (values[0], values[0], values[0], values[0]),
				2 => (values[0], values[1], values[0], values[1]),
				3 => (values[0], values[1], values[2], values[1]),
				_ => (values[0], values[1], values[2], values[3]),
			};

			into.Add(new Declaration(top, t));
			into.Add(new Declaration(right, r));
			into.Add(new Declaration(bottom, b));
			into.Add(new Declaration(left, l));

			return true;
		}

		/// <summary>
		/// The 1-to-4 value form of <see cref="Box"/> for a keyword property — <c>border-style</c> is
		/// the only one, but it takes the same top/right/bottom/left shorthand every box property does.
		/// </summary>
		private static bool KeywordBox(
			string value,
			List<Declaration> into,
			PropId top,
			PropId right,
			PropId bottom,
			PropId left,
			KeywordSet set,
			List<string> diagnostics,
			string sourceName,
			string name)
		{
			var parts = Split(value);

			if (parts.Count is 0 or > 4)
				return Fail(diagnostics, sourceName, name, value);

			var values = new StyleValue[4];

			for (var i = 0; i < parts.Count; i++)
			{
				var keyword = Keywords.Resolve(set, parts[i]);

				if (keyword < 0)
					return Fail(diagnostics, sourceName, name, value);

				values[i] = StyleValue.OfKeyword(keyword);
			}

			var (t, r, b, l) = parts.Count switch
			{
				1 => (values[0], values[0], values[0], values[0]),
				2 => (values[0], values[1], values[0], values[1]),
				3 => (values[0], values[1], values[2], values[1]),
				_ => (values[0], values[1], values[2], values[3]),
			};

			into.Add(new Declaration(top, t));
			into.Add(new Declaration(right, r));
			into.Add(new Declaration(bottom, b));
			into.Add(new Declaration(left, l));

			return true;
		}

		/// <summary>
		/// Reads <c>transform</c> into its five channels. One that reads a custom property is deferred
		/// whole, as <c>transition</c> is, and parsed once the cascade has substituted it.
		/// </summary>
		private static bool Transform(
			string value, List<Declaration> into, List<string> diagnostics, string sourceName)
		{
			var text = value.Trim();

			if (text.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				var pending = StyleValue.OfReference(new PendingTransform(text));

				for (var i = 0; i < PendingTransform.Longhands.Length; i++)
					into.Add(new Declaration(PendingTransform.Longhands[i], pending));

				return true;
			}

			if (!TryParseTransform(text, out var x, out var y, out var scale, out var rotation))
				return Fail(diagnostics, sourceName, "transform", value);

			into.Add(new Declaration(PropId.TranslateX, StyleValue.OfLength(x)));
			into.Add(new Declaration(PropId.TranslateY, StyleValue.OfLength(y)));
			into.Add(new Declaration(PropId.ScaleX, StyleValue.OfNumber(scale.x)));
			into.Add(new Declaration(PropId.ScaleY, StyleValue.OfNumber(scale.y)));
			into.Add(new Declaration(PropId.Rotation, StyleValue.OfNumber(rotation)));

			return true;
		}

		/// <summary>Parses a <c>transform</c> function list with no <c>var()</c> left in it.</summary>
		internal static bool TryParseTransform(
			string text, out StyleLength x, out StyleLength y, out Vector2 scale, out float rotation)
		{
			x = default;
			y = default;
			scale = Vector2.one;
			rotation = 0f;

			if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
				return true;

			foreach (var function in ValueParser.SplitTop(text, ' '))
			{
				if (!TryTransformFunction(function, ref x, ref y, ref scale, ref rotation))
					return false;
			}

			return true;
		}

		/// <summary>
		/// Reads <c>transform-origin</c> into one length per axis, each measured from the left and
		/// the top. Keywords are turned into percentages here so nothing downstream has to know the
		/// words, and both axes are always written so a more specific rule replaces the pair rather
		/// than half of it.
		/// </summary>
		private static bool TransformOrigin(
			string value, List<Declaration> into, List<string> diagnostics, string sourceName)
		{
			var parts = Split(value);

			if (parts.Count is 0 or > 2)
				return Fail(diagnostics, sourceName, "transform-origin", value);

			var x = default(StyleValue);
			var y = default(StyleValue);
			var sawX = false;
			var sawY = false;

			// Lengths and `center` do not name an axis, so they are held back and then fill whichever
			// axes the keywords left free. That is what makes `top left` and `left top` mean the same
			// thing, and `bottom 20px` put the 20px on x where CSS says it goes.
			var free = new StyleValue[2];
			var freeCount = 0;

			foreach (var part in parts)
			{
				switch (part.ToLowerInvariant())
				{
					case "left" when !sawX:
						x = Percent(0f);
						sawX = true;

						continue;

					case "right" when !sawX:
						x = Percent(100f);
						sawX = true;

						continue;

					case "top" when !sawY:
						y = Percent(0f);
						sawY = true;

						continue;

					case "bottom" when !sawY:
						y = Percent(100f);
						sawY = true;

						continue;

					case "center":
						free[freeCount++] = Percent(50f);

						continue;

					default:
						if (!Length(part, out var length))
							return Fail(diagnostics, sourceName, "transform-origin", value);

						free[freeCount++] = length;

						continue;
				}
			}

			var next = 0;

			// An axis nobody named is centred, as it is in CSS.
			if (!sawX) x = next < freeCount ? free[next++] : Percent(50f);
			if (!sawY) y = next < freeCount ? free[next] : Percent(50f);

			into.Add(new Declaration(PropId.TransformOriginX, x));
			into.Add(new Declaration(PropId.TransformOriginY, y));

			return true;
		}

		/// <summary>
		/// Reads a <c>transition</c> the CSS library left whole, which it does for any shorthand that
		/// reads a custom property.
		/// </summary>
		/// <remarks>
		/// A <c>var()</c> in a shorthand cannot be assigned to a longhand until it is substituted —
		/// <c>transition: opacity var(--motion)</c> may be hiding a duration, an easing or both — so
		/// every longhand is declared here as the same deferred value, and each one reads its own
		/// slice once the cascade knows the scope. Before this the shorthand was reported as an
		/// unknown property and dropped, while the longhands took <c>var()</c> without complaint.
		/// </remarks>
		private static bool Transition(string value, List<Declaration> into, List<string> diagnostics, string sourceName)
		{
			var text = value.Trim();

			if (text.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				var pending = StyleValue.OfReference(new PendingTransition(text));

				for (var i = 0; i < PendingTransition.Longhands.Length; i++)
					into.Add(new Declaration(PendingTransition.Longhands[i], pending));

				return true;
			}

			// The CSS library expands a literal transition itself, so this is only a fallback.
			var any = false;

			for (var i = 0; i < PendingTransition.Longhands.Length; i++)
			{
				var id = PendingTransition.Longhands[i];

				if (!PendingTransition.TryRead(text, id, out var parsed, out var valid))
				{
					if (!valid)
						return Fail(diagnostics, sourceName, "transition", value);

					continue;
				}

				into.Add(new Declaration(id, parsed));
				any = true;
			}

			return any || Fail(diagnostics, sourceName, "transition", value);
		}

		private static StyleValue Percent(float value)
		{
			return StyleValue.OfLength(new StyleLength(value, LengthUnit.Percent));
		}

		private static bool TryTransformFunction(string function, ref StyleLength x, ref StyleLength y, ref Vector2 scale, ref float rotation)
		{
			if (Arguments(function, "translatex") is { } tx)
				return Offset(tx, ref x);

			if (Arguments(function, "translatey") is { } ty)
				return Offset(ty, ref y);

			if (Arguments(function, "scalex") is { } sx)
				return Bare(sx, ref scale.x);

			if (Arguments(function, "scaley") is { } sy)
				return Bare(sy, ref scale.y);

			if (Arguments(function, "scale") is { } s)
			{
				var factors = s.Split(',');
				if (factors.Length is 0 or > 2 || !Bare(factors[0], ref scale.x))
					return false;

				scale.y = scale.x;

				return factors.Length == 1 || Bare(factors[1], ref scale.y);
			}

			if (Arguments(function, "rotate") is { } r)
			{
				var degrees = r.EndsWith("deg", StringComparison.OrdinalIgnoreCase)
					? r.Substring(0, r.Length - 3)
					: r;

				if (!float.TryParse(degrees.Trim(), out rotation))
					return false;

				return true;
			}

			if (Arguments(function, "translate") is not { } pair)
				return false;

			var parts = pair.Split(',');
			if (parts.Length is 0 or > 2)
				return false;

			if (!Offset(parts[0], ref x))
				return false;

			return parts.Length == 1 || Offset(parts[1], ref y);
		}

		private static string? Arguments(string function, string name)
		{
			return ValueParser.Inner(function, name);
		}

		/// <remarks>
		/// Keeps the unit: a rem offset scales with the rem size, and a percentage is of the node's own
		/// box, as CSS measures a translation.
		/// </remarks>
		private static bool Offset(string text, ref StyleLength target)
		{
			if (!ValueParser.TryParseLength(text.Trim(), out var value))
				return false;

			target = value.AsLength();

			return target.Unit != LengthUnit.Auto;
		}

		private static bool Bare(string text, ref float target)
		{
			return float.TryParse(text.Trim(), out target);
		}

		private readonly struct BorderSide
		{
			public readonly PropId Width;
			public readonly PropId Style;
			public readonly PropId Color;

			public BorderSide(PropId width, PropId style, PropId color)
			{
				Width = width;
				Style = style;
				Color = color;
			}
		}

		[NoAutoStaticsCleanup]
		private static readonly BorderSide[] s_top = { new(PropId.BorderTopWidth, PropId.BorderTopStyle, PropId.BorderTopColor) };

		[NoAutoStaticsCleanup]
		private static readonly BorderSide[] s_right = { new(PropId.BorderRightWidth, PropId.BorderRightStyle, PropId.BorderRightColor) };

		[NoAutoStaticsCleanup]
		private static readonly BorderSide[] s_bottom = { new(PropId.BorderBottomWidth, PropId.BorderBottomStyle, PropId.BorderBottomColor) };

		[NoAutoStaticsCleanup]
		private static readonly BorderSide[] s_left = { new(PropId.BorderLeftWidth, PropId.BorderLeftStyle, PropId.BorderLeftColor) };

		[NoAutoStaticsCleanup]
		private static readonly BorderSide[] s_allSides = { s_top[0], s_right[0], s_bottom[0], s_left[0] };

		/// <summary>
		/// <c>border</c> and its four per-side forms: a width, a style and a colour in any order, applied
		/// to the given sides.
		/// </summary>
		private static bool Border(
			string value, List<Declaration> into, List<string> diagnostics, string sourceName, string name, BorderSide[] sides)
		{
			var parts = Split(value);
			var width = StyleValue.OfNumber(0f);
			var color = StyleValue.OfColor(UnityEngine.Color.clear);
			var style = StyleValue.OfKeyword((int)BorderStyle.Solid);
			var sawWidth = false;
			var sawColor = false;
			var sawStyle = false;

			foreach (var part in parts)
			{
				// The style keyword is tested first because it is the only component with a closed
				// vocabulary: `dotted` is neither a length nor a colour, but `none` would be read as
				// a colour name by a parser that saw it after the others.
				if (!sawStyle)
				{
					var keyword = Keywords.Resolve(KeywordSet.BorderStyle, part);

					if (keyword >= 0)
					{
						style = StyleValue.OfKeyword(keyword);
						sawStyle = true;

						continue;
					}
				}

				if (!sawWidth && Length(part, out width))
				{
					sawWidth = true;

					continue;
				}

				if (sawColor || !ColorOrVar(part, out color))
					continue;

				sawColor = true;
			}

			if (!sawWidth && !sawColor && !sawStyle)
				return Fail(diagnostics, sourceName, name, value);

			foreach (var side in sides)
			{
				if (sawWidth)
					into.Add(new Declaration(side.Width, width));

				// The shorthand resets every component it does not name, so a rule that omits the style
				// after one that set `dashed` goes back to a solid line rather than inheriting it.
				into.Add(new Declaration(side.Style, style));

				if (sawColor)
					into.Add(new Declaration(side.Color, color));
			}

			return true;
		}

		private static bool BorderColor(
			string value, List<Declaration> into, List<string> diagnostics, string sourceName)
		{
			if (!ColorOrVar(value.Trim(), out var color))
				return Fail(diagnostics, sourceName, "border-color", value);

			into.Add(new Declaration(PropId.BorderTopColor, color));
			into.Add(new Declaration(PropId.BorderRightColor, color));
			into.Add(new Declaration(PropId.BorderBottomColor, color));
			into.Add(new Declaration(PropId.BorderLeftColor, color));

			return true;
		}

		private static bool Length(string text, out StyleValue value)
		{
			return Var(text, out value)
				|| CalcValue(text, CalcOutput.Length, out value)
				|| ValueParser.TryParseLength(text, out value);
		}

		private static bool Number(string text, out StyleValue value)
		{
			if (Var(text, out value) || CalcValue(text, CalcOutput.Number, out value))
				return true;

			if (!ValueParser.TryParseLength(text, out var length))
				return false;

			value = StyleValue.OfNumber(length.AsLength().Value);

			return true;
		}

		/// <summary>
		/// A shorthand's parts each become a declaration of their own, so one written as a calc that
		/// reads a custom property can defer exactly as `padding: var(--gap)` already does.
		/// </summary>
		private static bool CalcValue(string text, CalcOutput output, out StyleValue value)
		{
			value = default;

			return Calc.IsCalc(text) && Calc.TryParse(text, output, allowVars: true, out value);
		}

		private static bool ColorOrVar(string text, out StyleValue value)
		{
			return Var(text, out value) || ValueParser.TryParseColor(text, out value);
		}

		private static bool Var(string text, out StyleValue value)
		{
			value = default;

			if (!text.StartsWith("var(", StringComparison.OrdinalIgnoreCase))
				return false;

			var name = ValueParser.Inner(text, "var");
			if (name is null)
				return false;

			var comma = name.IndexOf(',');
			if (comma >= 0)
				name = name.Substring(0, comma);

			value = StyleValue.OfVar(ClassTable.Intern(name.Trim()));

			return true;
		}

		private static List<string> Split(string value)
		{
			var parts = new List<string>(4);
			var depth = 0;
			var start = -1;

			for (var i = 0; i <= value.Length; i++)
			{
				var atEnd = i == value.Length;
				var c = atEnd ? ' ' : value[i];

				if (!atEnd)
				{
					if (c == '(')
						depth++;
					else if (c == ')')
						depth--;
				}

				if (depth == 0 && (atEnd || char.IsWhiteSpace(c)))
				{
					if (start >= 0)
					{
						parts.Add(value.Substring(start, i - start));
						start = -1;
					}

					continue;
				}

				if (start < 0)
					start = i;
			}

			return parts;
		}

		private static bool Fail(List<string> diagnostics, string sourceName, string name, string value)
		{
			diagnostics.Add($"{sourceName}: cannot parse '{name}: {value}'.");

			return true;
		}
	}

	/// <summary>
	/// A <c>transition</c> shorthand that reads a custom property, carried through the sheet as its
	/// text and split into longhands only once the cascade has substituted it.
	/// </summary>
	/// <remarks>
	/// One instance stands in for all four longhands, and each asks it for its own slice. A slice the
	/// substituted text does not name is left unset, which is what the CSS library does with the
	/// longhands a literal shorthand omits.
	/// </remarks>
	internal sealed class PendingTransition : PendingShorthand
	{
		internal static readonly PropId[] Longhands =
		{
			PropId.TransitionProperty,
			PropId.TransitionDuration,
			PropId.TransitionTimingFunction,
			PropId.TransitionDelay,
		};

		internal PendingTransition(string text) : base(text)
		{
		}

		protected override bool TryReadSubstituted(string text, PropId id, out StyleValue value) =>
			TryRead(text, id, out value, out _);

		/// <summary>
		/// Reads one longhand out of a single transition. <paramref name="valid"/> tells a shorthand
		/// that does not name this longhand apart from one that is malformed.
		/// </summary>
		/// <remarks>
		/// The first time is the duration and the second the delay, as in CSS. An easing keyword is
		/// tried before a property name because the grammar gives it priority. A list is rejected, as it
		/// is for the longhands: a rule set carries one transition.
		/// </remarks>
		internal static bool TryRead(string text, PropId id, out StyleValue value, out bool valid)
		{
			value = default;
			valid = false;

			var parts = ValueParser.SplitTop(text, ' ');

			if (parts.Count == 0
				|| !PropertyRegistry.TryGet(PropId.TransitionDuration, out var time)
				|| !PropertyRegistry.TryGet(PropId.TransitionProperty, out var property))
			{
				return false;
			}

			var seen = new HashSet<PropId>();
			var found = false;

			foreach (var part in parts)
			{
				PropId slot;

				if (ValueParser.TryParse(part, time, out var parsed))
				{
					slot = seen.Contains(PropId.TransitionDuration) ? PropId.TransitionDelay : PropId.TransitionDuration;
				}
				else if (Keywords.Resolve(KeywordSet.Easing, part) is var easing and >= 0)
				{
					slot = PropId.TransitionTimingFunction;
					parsed = StyleValue.OfKeyword(easing);
				}
				else if (ValueParser.TryParse(part, property, out parsed))
				{
					slot = PropId.TransitionProperty;
				}
				else
				{
					return false;
				}

				if (!seen.Add(slot))
					return false;

				if (slot != id)
					continue;

				value = parsed;
				found = true;
			}

			valid = true;

			return found;
		}

	}

	/// <summary>
	/// A <c>transform</c> that reads a custom property, carried as its text and split into the five
	/// channels once the cascade has substituted it.
	/// </summary>
	internal sealed class PendingTransform : PendingShorthand
	{
		internal static readonly PropId[] Longhands =
		{
			PropId.TranslateX,
			PropId.TranslateY,
			PropId.ScaleX,
			PropId.ScaleY,
			PropId.Rotation,
		};

		internal PendingTransform(string text) : base(text)
		{
		}

		protected override bool TryReadSubstituted(string text, PropId id, out StyleValue value)
		{
			value = default;

			if (!Shorthand.TryParseTransform(text, out var x, out var y, out var scale, out var rotation))
				return false;

			switch (id)
			{
				case PropId.TranslateX: value = StyleValue.OfLength(x); return true;
				case PropId.TranslateY: value = StyleValue.OfLength(y); return true;
				case PropId.ScaleX: value = StyleValue.OfNumber(scale.x); return true;
				case PropId.ScaleY: value = StyleValue.OfNumber(scale.y); return true;
				case PropId.Rotation: value = StyleValue.OfNumber(rotation); return true;
				default: return false;
			}
		}
	}

	/// <summary>
	/// A shorthand that reads a custom property, carried through the sheet as its text and read into
	/// a longhand only once the cascade has a scope to substitute it against.
	/// </summary>
	internal abstract class PendingShorthand
	{
		// Deep enough for a token that aliases another; a cycle would otherwise never end.
		private const int MaxDepth = 8;

		protected PendingShorthand(string text)
		{
			Text = text;
		}

		internal string Text { get; }

		internal bool TryResolve(PropId id, IReadOnlyDictionary<int, StyleValue> scope, out StyleValue resolved)
		{
			resolved = default;

			return TrySubstitute(Text, scope, 0, out var text) && TryReadSubstituted(text, id, out resolved);
		}

		/// <summary>Reads one longhand out of the substituted text.</summary>
		protected abstract bool TryReadSubstituted(string text, PropId id, out StyleValue value);

		/// <summary>
		/// Replaces every <c>var()</c> in the text with the custom property's own text. An unresolved
		/// reference fails the whole value, which drops the declaration as it does anywhere else.
		/// </summary>
		private static bool TrySubstitute(string text, IReadOnlyDictionary<int, StyleValue> scope, int depth, out string result)
		{
			result = text;

			if (depth > MaxDepth)
				return false;

			var start = text.IndexOf("var(", StringComparison.OrdinalIgnoreCase);
			if (start < 0)
				return true;

			var builder = new System.Text.StringBuilder(text.Length);
			var copied = 0;

			while (start >= 0)
			{
				var open = start + 3;
				var close = MatchingParen(text, open);

				if (close < 0)
					return false;

				// Any fallback is dropped, the way every other var site drops it.
				var name = text.Substring(open + 1, close - open - 1);
				var comma = name.IndexOf(',');
				if (comma >= 0)
					name = name.Substring(0, comma);

				if (!scope.TryGetValue(ClassTable.Intern(name.Trim()), out var token)
					|| !TryWrite(token, out var piece)
					|| !TrySubstitute(piece, scope, depth + 1, out piece))
				{
					return false;
				}

				builder.Append(text, copied, start - copied).Append(piece);
				copied = close + 1;
				start = text.IndexOf("var(", copied, StringComparison.OrdinalIgnoreCase);
			}

			result = builder.Append(text, copied, text.Length - copied).ToString();

			return true;
		}

		/// <summary>
		/// A custom property as CSS text. One kept whole arrives as its text; a single length or number
		/// was parsed when the sheet was built and is written back out.
		/// </summary>
		private static bool TryWrite(in StyleValue token, out string text)
		{
			var invariant = CultureInfo.InvariantCulture;

			switch (token.Kind)
			{
				case StyleValueKind.Reference when token.Reference is string whole:
					text = whole;
					return true;

				case StyleValueKind.Length:
					var length = token.AsLength();
					text = length.Unit switch
					{
						LengthUnit.Auto => "auto",
						LengthUnit.Percent => length.Value.ToString(invariant) + "%",
						LengthUnit.Rem => length.Value.ToString(invariant) + "rem",
						_ => length.Value.ToString(invariant) + "px",
					};
					return true;

				case StyleValueKind.Number:
					text = token.AsNumber().ToString(invariant);
					return true;

				default:
					text = string.Empty;
					return false;
			}
		}

		private static int MatchingParen(string text, int open)
		{
			var depth = 0;

			for (var i = open; i < text.Length; i++)
			{
				if (text[i] == '(')
					depth++;
				else if (text[i] == ')' && --depth == 0)
					return i;
			}

			return -1;
		}
	}
}

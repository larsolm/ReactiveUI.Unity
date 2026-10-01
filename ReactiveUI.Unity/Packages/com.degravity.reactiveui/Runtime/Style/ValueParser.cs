using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ReactiveUI
{
	internal static class ValueParser
	{
		internal static bool TryParse(string raw, in PropertyInfo info, out StyleValue value)
		{
			var text = raw.Trim();
			value = default;

			if (text.Length == 0)
				return false;

			// A var() reference cannot be resolved until a node is known, so it is carried through
			// the sheet and substituted during the cascade.
			if (text.StartsWith("var(", StringComparison.OrdinalIgnoreCase))
			{
				var name = Inner(text, "var");
				if (name is null)
					return false;

				var comma = name.IndexOf(',');
				if (comma >= 0)
					name = name.Substring(0, comma);

				value = StyleValue.OfVar(ClassTable.Intern(name.Trim()));

				return true;
			}

			// A calc whose terms are all literal folds to a plain value here; one that reads a custom
			// property is carried through the sheet and folded during the cascade, as a var() is. A
			// syntax with no arithmetic in it — a colour, a keyword — has no output to fold to.
			if (Calc.IsCalc(text))
			{
				return Calc.OutputFor(info._syntax) is { } output
					&& Calc.TryParse(text, output, allowVars: true, out value);
			}

			// `initial` means "use the property's initial value", which for this model is simply
			// leaving the property unset and letting the applier's fallback stand.
			if (text.Equals("initial", StringComparison.OrdinalIgnoreCase)
				|| text.Equals("unset", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			return info._syntax switch
			{
				ValueSyntax.Length => TryParseLength(text, out value),
				ValueSyntax.Number => TryParseNumber(text, out value),
				ValueSyntax.Color => TryParseColor(text, out value),
				ValueSyntax.Keyword => TryParseKeyword(text, info._keywords, out value),
				ValueSyntax.Time => TryParseTime(text, out value),
				ValueSyntax.LetterSpacing => TryParseLetterSpacing(text, out value),
				ValueSyntax.FontReference or ValueSyntax.ImageReference => TryParseResource(text, out value),
				ValueSyntax.PropertyName => TryParsePropertyName(text, out value),
				ValueSyntax.AnimationName => TryParseAnimationName(text, out value),
				ValueSyntax.Count => TryParseCount(text, out value),
				ValueSyntax.Shadow => TryParseShadowList(text, out value),
				ValueSyntax.Checker => TryParseChecker(text, out value),
				_ => false,
			};
		}

		#region Composite values

		internal static bool TryParseShadowList(string text, out StyleValue value)
		{
			value = default;

			if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
			{
				value = StyleValue.OfReference(new ShadowList());

				return true;
			}

			var entries = SplitTop(text, ',');
			if (entries.Count == 0)
				return false;

			var shadows = new Shadow[entries.Count];

			for (var i = 0; i < entries.Count; i++)
			{
				if (!TryParseShadow(entries[i], out shadows[i]))
					return false;
			}

			value = StyleValue.OfReference(new ShadowList(shadows));

			return true;
		}

		private static bool TryParseShadow(string text, out Shadow shadow)
		{
			shadow = default;

			var parts = SplitTop(text, ' ');
			var lengths = new StyleLength[4];
			var count = 0;
			var ink = new VarColor(UnityEngine.Color.clear);
			var sawColor = false;

			foreach (var part in parts)
			{
				if (part.Equals("inset", StringComparison.OrdinalIgnoreCase))
					return false;

				if (count < 4 && TryParseLength(part, out var length))
				{
					lengths[count++] = length.AsLength();

					continue;
				}

				if (sawColor || !TryColorOrVar(part, out ink))
					return false;

				sawColor = true;
			}

			// Offsets are mandatory in CSS; everything after them defaults to zero.
			if (count < 2 || !sawColor)
				return false;

			shadow = new Shadow(lengths[0], lengths[1], lengths[2], lengths[3], ink);

			return true;
		}

		private static bool TryParseChecker(string text, out StyleValue value)
		{
			value = default;

			var parts = SplitTop(text, ' ');
			if (parts.Count is not (2 or 3))
				return false;

			if (!TryParseLength(parts[0], out var cell) || !TryColorOrVar(parts[parts.Count - 1], out var ink))
				return false;

			var line = default(StyleLength);

			if (parts.Count == 3)
			{
				if (!TryParseLength(parts[1], out var width))
					return false;

				line = width.AsLength();
			}

			value = StyleValue.OfReference(new Checker(cell.AsLength(), line, ink));

			return true;
		}

		/// <summary>
		/// Reads <c>linear-gradient(180deg, a, b)</c> and <c>radial-gradient(a 0%, b 70%)</c>.
		/// </summary>
		/// <remarks>
		/// Only the stop list is interpreted. A radial gradient's shape/size/position clause is
		/// accepted and ignored, because the painter approximates radial gradients from the centre
		/// outward regardless — pretending to honour <c>at 20% 80%</c> would be a lie.
		/// </remarks>
		internal static bool TryParseGradient(string text, out StyleValue value)
		{
			value = default;

			var radial = true;
			var inner = Inner(text, "radial-gradient");

			if (inner is null)
			{
				radial = false;
				inner = Inner(text, "linear-gradient");
			}

			if (inner is null)
				return false;

			var args = SplitTop(inner, ',');
			if (args.Count == 0)
				return false;

			// CSS defaults a linear gradient to "to bottom", which is 180°.
			var angle = 180f;
			var first = 0;

			if (!radial && args[0].EndsWith("deg", StringComparison.OrdinalIgnoreCase))
			{
				if (!TryNumber(args[0].Substring(0, args[0].Length - 3), out angle))
					return false;

				first = 1;
			}
			else if (radial && !StartsWithColor(args[0]))
			{
				// `circle at center` and friends: skipped, not rejected.
				first = 1;
			}

			var stops = new List<GradientStop>(args.Count - first);
			var positions = new List<float>(args.Count - first);

			for (var i = first; i < args.Count; i++)
			{
				var parts = SplitTop(args[i], ' ');
				if (parts.Count is 0 or > 2)
					return false;

				if (!TryColorOrVar(parts[0], out var ink))
					return false;

				var position = float.NaN;

				if (parts.Count == 2)
				{
					if (!TryStopPosition(parts[1], out position))
						return false;
				}

				stops.Add(new GradientStop(ink, 0f));
				positions.Add(position);
			}

			if (stops.Count == 0)
				return false;

			Distribute(positions);

			var resolved = new GradientStop[stops.Count];
			for (var i = 0; i < stops.Count; i++)
				resolved[i] = new GradientStop(stops[i]._ink, positions[i]);

			value = StyleValue.OfReference(new Gradient(radial ? GradientKind.Radial : GradientKind.Linear, angle, resolved));

			return true;
		}

		private static void Distribute(List<float> positions)
		{
			if (float.IsNaN(positions[0]))
				positions[0] = 0f;

			if (float.IsNaN(positions[positions.Count - 1]))
				positions[positions.Count - 1] = 1f;

			for (var i = 1; i < positions.Count - 1; i++)
			{
				if (!float.IsNaN(positions[i]))
					continue;

				var previous = i - 1;
				var next = i;

				while (next < positions.Count && float.IsNaN(positions[next]))
					next++;

				var span = positions[next] - positions[previous];
				var steps = next - previous;

				for (var k = previous + 1; k < next; k++)
				{
					positions[k] = positions[previous] + span * (k - previous) / steps;
				}

				i = next - 1;
			}
		}

		private static bool TryStopPosition(string text, out float position)
		{
			if (text.EndsWith("%", StringComparison.Ordinal))
			{
				if (!TryNumber(text.Substring(0, text.Length - 1), out position))
					return false;

				position /= 100f;

				return true;
			}

			return TryNumber(text, out position);
		}

		private static bool StartsWithColor(string text)
		{
			var parts = SplitTop(text, ' ');

			return parts.Count > 0 && TryColorOrVar(parts[0], out _);
		}

		private static bool TryColorOrVar(string text, out VarColor ink)
		{
			var trimmed = text.Trim();

			if (TryVarWithAlpha(trimmed, out ink))
				return true;

			if (trimmed.StartsWith("var(", StringComparison.OrdinalIgnoreCase))
			{
				var name = Inner(trimmed, "var");

				if (name is not null)
				{
					var comma = name.IndexOf(',');
					if (comma >= 0)
						name = name.Substring(0, comma);

					ink = new VarColor(ClassTable.Intern(name.Trim()));

					return true;
				}
			}

			if (TryParseColor(trimmed, out var value) && value.Kind == StyleValueKind.Color)
			{
				ink = new VarColor(value.AsColor());

				return true;
			}

			ink = default;

			return false;
		}

		internal static List<string> SplitTop(string value, char separator)
		{
			var parts = new List<string>(4);
			var depth = 0;
			var start = -1;

			for (var i = 0; i <= value.Length; i++)
			{
				var atEnd = i == value.Length;
				var c = atEnd ? separator : value[i];

				if (!atEnd)
				{
					if (c == '(')
						depth++;
					else if (c == ')')
						depth--;
				}

				var breaks = separator == ' ' ? char.IsWhiteSpace(c) : c == separator;

				if (depth == 0 && (atEnd || breaks))
				{
					if (start >= 0)
					{
						parts.Add(value.Substring(start, i - start).Trim());
						start = -1;
					}

					continue;
				}

				if (start < 0 && !char.IsWhiteSpace(c))
					start = i;
			}

			return parts;
		}

		#endregion

		internal static bool TryParseLength(string text, out StyleValue value)
		{
			value = default;

			// Before the elliptical-pair trim below, which would cut an expression at its first space.
			// Vars are refused: this entry point also feeds the composite values — a shadow, a checker,
			// a gradient stop — which bake their lengths in when the sheet is built and have nowhere
			// to keep an expression that is still waiting on a scope.
			if (Calc.IsCalc(text))
				return Calc.TryParse(text, CalcOutput.Length, allowVars: false, out value);

			// A corner radius comes back from the CSS library as an elliptical pair — `12px 12px`.
			// Only circular corners can be drawn, so the horizontal radius wins.
			var space = text.IndexOf(' ');
			if (space > 0)
				text = text.Substring(0, space);

			if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
			{
				value = StyleValue.OfLength(StyleLength.Auto);

				return true;
			}

			if (text.EndsWith("%", StringComparison.Ordinal))
			{
				if (!TryNumber(text.Substring(0, text.Length - 1), out var percent))
					return false;

				value = StyleValue.OfLength(new StyleLength(percent, LengthUnit.Percent));

				return true;
			}

			if (text.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
			{
				if (!TryNumber(text.Substring(0, text.Length - 3), out var rem))
					return false;

				value = StyleValue.OfLength(new StyleLength(rem, LengthUnit.Rem));

				return true;
			}

			if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
			{
				if (!TryNumber(text.Substring(0, text.Length - 2), out var px))
					return false;

				value = StyleValue.OfLength(new StyleLength(px));

				return true;
			}

			// A bare number is only a length when it is zero, per CSS.
			if (!TryNumber(text, out var bare) || bare != 0f)
				return false;

			value = StyleValue.OfLength(new StyleLength(0f));

			return true;
		}

		/// <summary>
		/// A custom property written as a bare number — <c>--columns: 8</c>.
		/// </summary>
		/// <remarks>
		/// Strict where <see cref="TryParseNumber"/> is lenient: that one strips a trailing <c>px</c>
		/// or <c>em</c>, because the properties it serves reduce both to a multiplier. A custom
		/// property has no syntax saying they should, and a length is tried before this, so a token
		/// carrying a unit never reaches here.
		/// </remarks>
		internal static bool TryParseBareNumber(string text, out StyleValue value)
		{
			if (!TryNumber(text, out var number))
			{
				value = default;

				return false;
			}

			value = StyleValue.OfNumber(number);

			return true;
		}

		private static bool TryParseNumber(string text, out StyleValue value)
		{
			value = default;

			// `line-height: 1.4` and `letter-spacing: 0.12em` both reduce to a bare multiplier here.
			var trimmed = text.EndsWith("em", StringComparison.OrdinalIgnoreCase)
				? text.Substring(0, text.Length - 2)
				: text.EndsWith("px", StringComparison.OrdinalIgnoreCase)
					? text.Substring(0, text.Length - 2)
					: text;

			if (!TryNumber(trimmed, out var number))
				return false;

			value = StyleValue.OfNumber(number);

			return true;
		}

		private static bool TryParsePropertyName(string text, out StyleValue value)
		{
			value = default;

			if (text.Equals("all", StringComparison.OrdinalIgnoreCase))
			{
				value = StyleValue.OfKeyword((int)PropId.None);

				return true;
			}

			if (!PropertyRegistry.TryGet(text, out var target))
				return false;

			value = StyleValue.OfKeyword((int)target._id);

			return true;
		}

		/// <summary>
		/// Interns an <c>animation-name</c> the way a custom property's name is interned, so the
		/// clip is looked up by id rather than by string once a node is being styled.
		/// </summary>
		/// <remarks>
		/// A comma-separated list is rejected rather than silently taking one of its entries. The
		/// CSS library joins the list back into one value when it expands the `animation` shorthand,
		/// so this is where `animation: a 1s, b 2s` is caught — the same one-per-rule-set limit
		/// `transition` has, but named in a diagnostic instead of quietly last-winning.
		/// </remarks>
		private static bool TryParseAnimationName(string text, out StyleValue value)
		{
			value = default;

			if (text.IndexOf(',') >= 0)
				return false;

			if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
			{
				value = StyleValue.OfKeyword(0);

				return true;
			}

			value = StyleValue.OfKeyword(ClassTable.Intern(Unquote(text)));

			return true;
		}

		/// <summary>
		/// An iteration count: a non-negative number, or <c>infinite</c> as -1.
		/// </summary>
		private static bool TryParseCount(string text, out StyleValue value)
		{
			value = default;

			if (text.Equals("infinite", StringComparison.OrdinalIgnoreCase))
			{
				value = StyleValue.OfNumber(-1f);

				return true;
			}

			if (!TryNumber(text, out var count) || count < 0f)
				return false;

			value = StyleValue.OfNumber(count);

			return true;
		}

		private static bool TryParseLetterSpacing(string text, out StyleValue value)
		{
			value = default;

			if (text.EndsWith("em", StringComparison.OrdinalIgnoreCase))
			{
				if (!TryNumber(text.Substring(0, text.Length - 2), out var em))
					return false;

				value = StyleValue.OfNumber(em * 100f);

				return true;
			}

			if (!TryNumber(text, out var bare))
				return false;

			value = StyleValue.OfNumber(bare);

			return true;
		}

		private static bool TryParseTime(string text, out StyleValue value)
		{
			value = default;

			if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
			{
				if (!TryNumber(text.Substring(0, text.Length - 2), out var ms))
					return false;

				value = StyleValue.OfNumber(ms / 1000f);

				return true;
			}

			if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase))
			{
				if (!TryNumber(text.Substring(0, text.Length - 1), out var seconds))
					return false;

				value = StyleValue.OfNumber(seconds);

				return true;
			}

			return false;
		}

		/// <summary>
		/// Reads <c>rgba(var(--x), a)</c> (or <c>rgb(var(--x))</c>): a variable's colour with its alpha
		/// replaced.
		/// </summary>
		private static bool TryVarWithAlpha(string text, out VarColor ink)
		{
			ink = default;

			var isRgba = text.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase);
			if (!isRgba && !text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
				return false;

			var inner = Inner(text, isRgba ? "rgba" : "rgb");
			if (inner is null || !inner.TrimStart().StartsWith("var(", StringComparison.OrdinalIgnoreCase))
				return false;

			var parts = SplitTop(inner, ',');
			if (parts.Count is < 1 or > 2)
				return false;

			var name = Inner(parts[0].Trim(), "var");
			if (name is null)
				return false;

			var comma = name.IndexOf(',');
			if (comma >= 0)
				name = name.Substring(0, comma);

			var alpha = -1f;
			if (parts.Count == 2 && !TryNumber(parts[1], out alpha))
				return false;

			ink = new VarColor(ClassTable.Intern(name.Trim()), alpha < 0f ? -1f : Mathf.Clamp01(alpha));

			return true;
		}

		internal static bool TryParseColor(string text, out StyleValue value)
		{
			value = default;

			if (TryVarWithAlpha(text.Trim(), out var ink))
			{
				value = StyleValue.OfReference(new VarColorValue(ink));

				return true;
			}

			if (text.StartsWith("#", StringComparison.Ordinal))
			{
				if (!ColorUtility.TryParseHtmlString(text, out var hex))
					return false;

				value = StyleValue.OfColor(hex);

				return true;
			}

			var isRgba = text.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase);
			if (isRgba || text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
			{
				var inner = Inner(text, isRgba ? "rgba" : "rgb");
				if (inner is null)
					return false;

				var parts = inner.Split(',');
				if (parts.Length < 3)
					return false;

				if (!TryChannel(parts[0], out var r) || !TryChannel(parts[1], out var g) || !TryChannel(parts[2], out var b))
				{
					return false;
				}

				var a = 1f;
				if (parts.Length > 3 && !TryNumber(parts[3], out a))
					return false;

				value = StyleValue.OfColor(new Color(r, g, b, a));

				return true;
			}

			// Named colours are normalised to rgb() upstream, so anything left is unrecognised.
			return ColorUtility.TryParseHtmlString(text, out var named) && Assign(named, out value);
		}

		private static bool Assign(Color color, out StyleValue value)
		{
			value = StyleValue.OfColor(color);

			return true;
		}

		private static bool TryChannel(string text, out float channel)
		{
			var trimmed = text.Trim();

			if (trimmed.EndsWith("%", StringComparison.Ordinal))
			{
				if (!TryNumber(trimmed.Substring(0, trimmed.Length - 1), out var percent))
				{
					channel = 0f;

					return false;
				}

				channel = percent / 100f;

				return true;
			}

			if (!TryNumber(trimmed, out var raw))
			{
				channel = 0f;

				return false;
			}

			channel = raw / 255f;

			return true;
		}

		private static bool TryParseResource(string text, out StyleValue value)
		{
			value = default;

			var inner = Inner(text, "resource") ?? Inner(text, "url");
			if (inner is null)
				return false;

			value = StyleValue.OfReference(Unquote(inner));

			return true;
		}

		private static bool TryParseKeyword(string text, KeywordSet set, out StyleValue value)
		{
			value = default;

			var keyword = Keywords.Resolve(set, text);
			if (keyword < 0)
				return false;

			value = StyleValue.OfKeyword(keyword);

			return true;
		}

		internal static string Unquote(string text)
		{
			var trimmed = text.Trim();

			if (trimmed.Length >= 2
				&& ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
					|| (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
			{
				return trimmed.Substring(1, trimmed.Length - 2);
			}

			return trimmed;
		}

		internal static string? Inner(string text, string function)
		{
			if (!text.StartsWith(function + "(", StringComparison.OrdinalIgnoreCase))
				return null;

			var close = text.LastIndexOf(')');
			var open = function.Length + 1;

			return close <= open ? null : text.Substring(open, close - open);
		}

		private static bool TryNumber(string text, out float number)
		{
			return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
		}
	}

	internal static class Keywords
	{
		internal static int Resolve(KeywordSet set, string text) => set switch
		{
			KeywordSet.Display => text.ToLowerInvariant() switch
			{
				"flex" => 0,
				"none" => 1,
				_ => -1,
			},
			KeywordSet.Position => text.ToLowerInvariant() switch
			{
				"static" => 0,
				"relative" => 1,
				"absolute" => 2,
				_ => -1,
			},
			KeywordSet.FlexDirection => text.ToLowerInvariant() switch
			{
				"column" => 0,
				"column-reverse" => 1,
				"row" => 2,
				"row-reverse" => 3,
				_ => -1,
			},
			KeywordSet.FlexWrap => text.ToLowerInvariant() switch
			{
				"nowrap" => 0,
				"wrap" => 1,
				"wrap-reverse" => 2,
				_ => -1,
			},
			KeywordSet.JustifyContent => text.ToLowerInvariant() switch
			{
				"flex-start" or "start" => 0,
				"center" => 1,
				"flex-end" or "end" => 2,
				"space-between" => 3,
				"space-around" => 4,
				"space-evenly" => 5,
				_ => -1,
			},
			KeywordSet.AlignItems or KeywordSet.AlignSelf or KeywordSet.AlignContent => text.ToLowerInvariant() switch
			{
				"auto" => 0,
				"flex-start" or "start" => 1,
				"center" => 2,
				"flex-end" or "end" => 3,
				"stretch" => 4,
				"baseline" => 5,
				"space-between" => 6,
				"space-around" => 7,
				"space-evenly" => 8,
				_ => -1,
			},
			KeywordSet.Overflow => text.ToLowerInvariant() switch
			{
				"visible" => 0,
				"hidden" => 1,
				"scroll" => 2,
				_ => -1,
			},
			KeywordSet.TextAlign => text.ToLowerInvariant() switch
			{
				"left" => 0,
				"center" => 1,
				"right" => 2,
				"justify" => 3,
				_ => -1,
			},
			// `middle` is what both CSS and TMP call it; `center` is accepted because the neighbouring
			// properties here spell it that way and this alignment is TMP's, not CSS's, regardless.
			// `--geometry` and `--capline` have no CSS spelling, so they carry the prefix this
			// framework uses for values it invented — as the easing keywords do.
			KeywordSet.VerticalAlign => text.ToLowerInvariant() switch
			{
				"middle" or "center" => 0,
				"top" => 1,
				"bottom" => 2,
				"baseline" => 3,
				"--geometry" => 4,
				"--capline" => 5,
				_ => -1,
			},
			KeywordSet.WhiteSpace => text.ToLowerInvariant() switch
			{
				"normal" => 0,
				"nowrap" => 1,
				_ => -1,
			},
			// The keyword resolves to the CSS numeric weight rather than an ordinal, because it is
			// matched against the weights `@font-face` registered rather than switched on.
			KeywordSet.FontWeight => text.ToLowerInvariant() switch
			{
				"thin" or "100" => 100,
				"extralight" or "200" => 200,
				"light" or "300" => 300,
				"normal" or "regular" or "400" => 400,
				"medium" or "500" => 500,
				"semibold" or "600" => 600,
				"bold" or "700" => 700,
				"extrabold" or "800" => 800,
				"black" or "900" => 900,
				_ => -1,
			},
			KeywordSet.TextTransform => text.ToLowerInvariant() switch
			{
				"none" => 0,
				"uppercase" => 1,
				"lowercase" => 2,
				"capitalize" => 3,
				_ => -1,
			},
			// `collapse` only differs from `hidden` on table rows, which do not exist here.
			KeywordSet.Visibility => text.ToLowerInvariant() switch
			{
				"visible" => 0,
				"hidden" or "collapse" => 1,
				_ => -1,
			},
			KeywordSet.BorderStyle => text.ToLowerInvariant() switch
			{
				"solid" => (int)BorderStyle.Solid,
				"none" => (int)BorderStyle.None,
				"hidden" => (int)BorderStyle.Hidden,
				"dashed" => (int)BorderStyle.Dashed,
				"dotted" => (int)BorderStyle.Dotted,
				"double" => (int)BorderStyle.Double,
				"groove" => (int)BorderStyle.Groove,
				"ridge" => (int)BorderStyle.Ridge,
				"inset" => (int)BorderStyle.Inset,
				"outset" => (int)BorderStyle.Outset,
				_ => -1,
			},
			// Named curves map to Easing; cubic-bezier() and steps() are interned as curves of their own.
			KeywordSet.Easing => text.ToLowerInvariant() switch
			{
				"linear" => (int)Easing.Linear,
				"ease" or "ease-in-out" => (int)Easing.InOutQuad,
				"ease-in" => (int)Easing.InQuad,
				"ease-out" => (int)Easing.OutQuad,
				"--out-cubic" => (int)Easing.OutCubic,
				"--out-back" => (int)Easing.OutBack,
				_ => EasingCurves.TryParse(text, out var curve) ? curve : -1,
			},
			KeywordSet.AnimationDirection => text.ToLowerInvariant() switch
			{
				"normal" => (int)AnimationDirection.Normal,
				"reverse" => (int)AnimationDirection.Reverse,
				"alternate" => (int)AnimationDirection.Alternate,
				"alternate-reverse" => (int)AnimationDirection.AlternateReverse,
				_ => -1,
			},
			KeywordSet.AnimationFillMode => text.ToLowerInvariant() switch
			{
				"none" => (int)AnimationFill.None,
				"forwards" => (int)AnimationFill.Forwards,
				"backwards" => (int)AnimationFill.Backwards,
				"both" => (int)AnimationFill.Both,
				_ => -1,
			},
			KeywordSet.AnimationPlayState => text.ToLowerInvariant() switch
			{
				"running" => 0,
				"paused" => 1,
				_ => -1,
			},
			_ => -1,
		};
	}
}

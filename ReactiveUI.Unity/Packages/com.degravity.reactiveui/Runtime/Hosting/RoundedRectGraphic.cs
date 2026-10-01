using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ReactiveUI
{
	[RequireComponent(typeof(CanvasRenderer))]
	[NoAutoStaticsCleanup]
	internal sealed partial class RoundedRectGraphic : MaskableGraphic
	{
		private const float EdgeAa = 1.25f;
		private const int CornerSegments = 6;

		// px: topLeft, topRight, bottomRight, bottomLeft
		private Vector4 _corners;
		private Color _fillColor = Color.clear;
		private Gradient? _gradient;
		private readonly List<ResolvedShadow> _shadows = new();
		private BorderPaint _border;
		private Texture? _texture;
		private float _checkerCell;
		private float _checkerLine;
		private Color _checkerColor = Color.clear;

		public override Texture mainTexture => _texture != null ? _texture : base.mainTexture;

		/// <summary>
		/// Replaces the fill colour; transition callers drive this each frame.
		/// </summary>
		public void SetFillColor(Color color)
		{
			if (_fillColor == color)
				return;

			_fillColor = color;
			SetVerticesDirty();
		}

		/// <summary>
		/// Replaces the four edge colours, leaving their widths alone; the colour channels drive
		/// this each frame the way <see cref="SetFillColor"/> is driven.
		/// </summary>
		public void SetBorderColors(Color top, Color right, Color bottom, Color left)
		{
			if (_border.TopColor == top && _border.RightColor == right
				&& _border.BottomColor == bottom && _border.LeftColor == left)
			{
				return;
			}

			_border = _border.WithColors(top, right, bottom, left);
			SetVerticesDirty();
		}

		/// <summary>
		/// Sets everything the mesh is painted from, and rebuilds it only if any of it moved.
		/// </summary>
		/// <remarks>
		/// The guard is worth its comparison several times over: <c>SetVerticesDirty</c> queues this
		/// graphic into uGUI's rebuild registry, and the repaint that follows walks the whole
		/// perimeter, builds its normals and re-batches the canvas. A restyle driven by a pointer
		/// moving across an unrelated ancestor reaches a great many nodes whose paint is identical.
		/// </remarks>
		public void Configure(Vector4 corners, Color fillColor, Gradient? gradient,
			in BorderPaint border, IReadOnlyList<ResolvedShadow> shadows, Texture? texture,
			float checkerCell, float checkerLine, Color checkerColor)
		{
			if (!ReferenceEquals(_texture, texture))
			{
				_texture = texture;
				SetMaterialDirty();
			}

			var changed = _corners != corners
				|| _fillColor != fillColor
				|| !ReferenceEquals(_gradient, gradient)
				|| !_border.Equals(border)
				|| !_checkerCell.Equals(checkerCell)
				|| !_checkerLine.Equals(checkerLine)
				|| _checkerColor != checkerColor
				|| !ShadowsMatch(shadows);

			if (!changed)
				return;

			_corners = corners;
			_fillColor = fillColor;
			_gradient = gradient;
			_border = border;
			_checkerCell = checkerCell;
			_checkerLine = checkerLine;
			_checkerColor = checkerColor;

			_shadows.Clear();
			for (var i = 0; i < shadows.Count; i++)
				_shadows.Add(shadows[i]);

			SetVerticesDirty();
		}

		private bool ShadowsMatch(IReadOnlyList<ResolvedShadow> shadows)
		{
			if (_shadows.Count != shadows.Count)
				return false;

			for (var i = 0; i < _shadows.Count; i++)
			{
				if (!_shadows[i].Equals(shadows[i]))
					return false;
			}

			return true;
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			var rect = rectTransform.rect;
			if (rect.width <= 0f || rect.height <= 0f)
				return;

			// Shadows are painted first (furthest back). CSS lists the top-most shadow
			// first, so emit them in reverse to keep the first entry visually on top.
			for (var i = _shadows.Count - 1; i >= 0; i--)
			{
				var s = _shadows[i];
				var shadowRect = Inflate(rect, s.Spread);
				shadowRect.x += s.Offset.x;
				shadowRect.y += s.Offset.y;
				var shadowCorners = ExpandCorners(_corners, s.Spread);
				EmitBlurredShape(vh, shadowRect, shadowCorners, s.Color, s.Blur, s.Spread, s.Offset);
			}

			// Fill.
			if (_fillColor.a > 0f || _gradient != null)
				EmitShape(vh, rect, _corners, _fillColor, _gradient, EdgeAa);

			// Repeating checker or grid, painted over the fill.
			if (_checkerCell > 0f && _checkerColor.a > 0f)
			{
				if (_checkerLine > 0f)
					EmitGridLines(vh, rect, _checkerCell, _checkerLine, _checkerColor);
				else
					EmitChecker(vh, rect, _checkerCell, _checkerColor);
			}

			// Inset border stroke, rounded to match the fill.
			if (_border.Any)
				EmitStroke(vh, rect, _corners, _border);
		}

		/// <summary>
		/// The alpha a Gaussian-blurred edge carries, sampled at fixed multiples of the blur's
		/// standard deviation. Index 0 is the innermost stop.
		/// </summary>
		/// <remarks>
		/// CSS defines a shadow's blur as a Gaussian whose standard deviation is <b>half</b> the blur
		/// radius, and the transition that produces straddles the shape's edge rather than starting
		/// at it. Both halves of that matter when a value is copied from a design file:
		/// <list type="bullet">
		/// <item>a ramp that runs outward from the edge by the whole radius reaches twice as far as a
		/// browser draws it, and</item>
		/// <item>being linear, it carries far too much of its opacity out at the tail — which is what
		/// makes a copied value read as a flat wash the width of the blur instead of a soft glow
		/// hugging the shape.</item>
		/// </list>
		/// These are the complementary error function — a blurred straight edge's own profile — at
		/// -1σ, 0, +1σ and +2σ. Half the shadow's opacity sits exactly on the edge, ~84% of it lands
		/// within 1σ, and past 2σ there is too little left to be worth the triangles. The innermost
		/// stop is rounded up to fully opaque because it is nearly always covered by the element's
		/// own fill.
		/// </remarks>
		private static readonly float[] s_blurSigmas = { -1f, 0f, 1f, 2f };
		private static readonly float[] s_blurAlphas = { 1f, 0.5f, 0.159f, 0f };

		/// <summary>The blur profile's alpha anywhere between the stops, in standard deviations.</summary>
		private static float BlurAlphaAt(float sigmas)
		{
			if (sigmas <= s_blurSigmas[0])
				return s_blurAlphas[0];

			for (var i = 1; i < s_blurSigmas.Length; i++)
			{
				if (sigmas > s_blurSigmas[i])
					continue;

				return Mathf.Lerp(
					s_blurAlphas[i - 1],
					s_blurAlphas[i],
					Mathf.InverseLerp(s_blurSigmas[i - 1], s_blurSigmas[i], sigmas));
			}

			return s_blurAlphas[s_blurAlphas.Length - 1];
		}

		/// <summary>
		/// The flat band an unblurred shadow leaves outside the border box — a spread ring.
		/// </summary>
		/// <remarks>
		/// The inner edge is deliberately hard: it abuts the element's own border box, which paints
		/// over it, so feathering there would only put a seam between the two.
		/// </remarks>
		private static void EmitHardRing(
			VertexHelper vh, Rect rect, Vector4 corners, Color color, float spread, Vector2 offset)
		{
			var perimeter = BuildPerimeter(rect, corners);
			var normals = BuildNormals(perimeter);
			var n = perimeter.Count;
			var startIndex = vh.currentVertCount;
			var faded = color;
			faded.a = 0f;

			Vector2 UvAt(Vector2 p) => new((p.x - rect.xMin) / rect.width, (p.y - rect.yMin) / rect.height);

			for (var i = 0; i < n; i++)
			{
				var p = perimeter[i] - normals[i] * spread - offset;
				AddVert(vh, p, color, UvAt(p));
			}

			for (var i = 0; i < n; i++)
				AddVert(vh, perimeter[i], color, UvAt(perimeter[i]));

			for (var i = 0; i < n; i++)
			{
				var p = perimeter[i] + normals[i] * EdgeAa;
				AddVert(vh, p, faded, UvAt(p));
			}

			for (var r = 0; r < 2; r++)
			{
				var ring = startIndex + r * n;
				var next = ring + n;

				for (var i = 0; i < n; i++)
				{
					var a = ring + i;
					var b = ring + (i + 1) % n;
					var ao = next + i;
					var bo = next + (i + 1) % n;
					vh.AddTriangle(a, ao, bo);
					vh.AddTriangle(a, bo, b);
				}
			}

			ReturnPoints(normals);
			ReturnPoints(perimeter);
		}

		/// <summary>Paints one shadow, matching how a browser draws a <c>box-shadow</c>.</summary>
		/// <remarks>
		/// CSS paints an <i>outer</i> shadow only outside the element's border box — the part beneath
		/// the element is clipped away, not merely covered. That distinction is invisible under an
		/// opaque fill and glaring under a translucent one, where an unclipped body shows straight
		/// through the face and turns a soft halo into a solid tile of its own colour.
		///
		/// So the shadow is built as an annulus rather than a filled shape: the border box is the
		/// inner boundary and nothing inside it is emitted. That box is this outline deflated by the
		/// spread with the offset undone, derived by pushing <i>this</i> perimeter along its own
		/// normals rather than building a second one from another rect — the two then pair up
		/// index-for-index by construction, which is the trap <see cref="EmitStroke"/>'s remark
		/// documents.
		/// </remarks>
		private static void EmitBlurredShape(
			VertexHelper vh, Rect rect, Vector4 corners, Color color, float blur, float spread, Vector2 offset)
		{
			// An unblurred shadow has no falloff to interpolate, so it is a band of one flat colour
			// between the border box and this outline — which is what a `0 0 0 Npx` spread ring is.
			// The band exists only while the box still sits inside the outline: a keycap's downward
			// offset slides it out, leaving two shapes that merely overlap, and nothing to clip to.
			if (blur <= 0f)
			{
				if (offset.sqrMagnitude <= spread * spread)
					EmitHardRing(vh, rect, corners, color, spread, offset);
				else
					EmitShape(vh, rect, corners, color, gradient: null, EdgeAa);

				return;
			}

			var sigma = blur * 0.5f;
			var perimeter = BuildPerimeter(rect, corners);
			var normals = BuildNormals(perimeter);
			var n = perimeter.Count;

			// How far the border box can poke past this outline anywhere around it. A stop nearer
			// than that would fold its band through itself on the side the offset pushes towards, so
			// the annulus starts at the first stop that clears the box the whole way round.
			var reach = offset.magnitude - spread;
			var first = 0;

			while (first < s_blurSigmas.Length && s_blurSigmas[first] * sigma < reach)
				first++;

			// A large offset under a small blur leaves no stop enclosing the box, and so no annulus.
			// Fall back to the filled body — with no ring outside the box there is nothing to clip to,
			// and such a shadow is a hard drop under an opaque fill, which covers it regardless.
			var knockOut = first < s_blurSigmas.Length;
			if (!knockOut)
				first = 0;

			Vector2 UvAt(Vector2 p) => new((p.x - rect.xMin) / rect.width, (p.y - rect.yMin) / rect.height);

			var startIndex = vh.currentVertCount;
			var rings = 0;

			if (knockOut)
			{
				// The border box, carrying the profile's alpha *per vertex*: an offset shadow is
				// denser on the side it leans towards, and one alpha for the whole ring would flatten
				// that away.
				for (var i = 0; i < n; i++)
				{
					var p = perimeter[i] - normals[i] * spread - offset;
					var c = color;
					c.a = color.a * BlurAlphaAt((-spread - Vector2.Dot(offset, normals[i])) / sigma);

					AddVert(vh, p, c, UvAt(p));
				}

				rings++;
			}
			else
			{
				// The centre carries the innermost stop's alpha so the fan below is flat, not gradient.
				var c = color;
				c.a = color.a * s_blurAlphas[0];

				AddVert(vh, rect.center, c, UvAt(rect.center));
			}

			for (var s = first; s < s_blurSigmas.Length; s++)
			{
				var c = color;
				c.a = color.a * s_blurAlphas[s];

				for (var i = 0; i < n; i++)
				{
					var p = perimeter[i] + normals[i] * (s_blurSigmas[s] * sigma);

					AddVert(vh, p, c, UvAt(p));
				}

				rings++;
			}

			var ringBase = knockOut ? startIndex : startIndex + 1;

			// Only the fallback has an interior to fill; the knocked-out shadow is bands alone.
			if (!knockOut)
			{
				for (var i = 0; i < n; i++)
				{
					var a = ringBase + i;
					var b = ringBase + (i + 1) % n;

					vh.AddTriangle(startIndex, a, b);
				}
			}

			// One band of quads per gap between rings, each interpolating the falloff across it.
			for (var r = 0; r < rings - 1; r++)
			{
				var ring = ringBase + r * n;
				var next = ring + n;

				for (var i = 0; i < n; i++)
				{
					var a = ring + i;
					var b = ring + (i + 1) % n;
					var ao = next + i;
					var bo = next + (i + 1) % n;
					vh.AddTriangle(a, ao, bo);
					vh.AddTriangle(a, bo, b);
				}
			}

			ReturnPoints(normals);
			ReturnPoints(perimeter);
		}

		private static void EmitShape(VertexHelper vh, Rect rect, Vector4 corners, Color color, Gradient? gradient, float feather)
		{
			var perimeter = BuildPerimeter(rect, corners);
			var normals = BuildNormals(perimeter);
			var center = rect.center;

			GradientProjection projection = default;
			if (gradient != null)
				projection = GradientProjection.Build(gradient, perimeter, center);

			Color ColorAt(Vector2 p) => gradient != null ? gradient.Sample(projection.T(p)) : color;
			Vector2 UvAt(Vector2 p) => new((p.x - rect.xMin) / rect.width, (p.y - rect.yMin) / rect.height);

			var startIndex = vh.currentVertCount;

			// Center vertex (0), then perimeter (1..n), then feathered ring (n+1..2n).
			AddVert(vh, center, ColorAt(center), UvAt(center));
			foreach (var p in perimeter)
				AddVert(vh, p, ColorAt(p), UvAt(p));

			var n = perimeter.Count;
			var ringStart = startIndex + 1 + n;
			for (var i = 0; i < n; i++)
			{
				var c = ColorAt(perimeter[i]);
				c.a = 0f;
				AddVert(vh, perimeter[i] + normals[i] * feather, c, UvAt(perimeter[i]));
			}

			// Fan triangles (center → edge).
			for (var i = 0; i < n; i++)
			{
				var a = startIndex + 1 + i;
				var b = startIndex + 1 + (i + 1) % n;
				vh.AddTriangle(startIndex, a, b);
			}

			// Feather ring quads.
			for (var i = 0; i < n; i++)
			{
				var a = startIndex + 1 + i;
				var b = startIndex + 1 + (i + 1) % n;
				var ao = ringStart + i;
				var bo = ringStart + (i + 1) % n;
				vh.AddTriangle(a, ao, bo);
				vh.AddTriangle(a, bo, b);
			}

			ReturnPoints(normals);
			ReturnPoints(perimeter);
		}

		// Perimeter order, and the side each arc runs between. The arcs come out of BuildPerimeter
		// counter-clockwise from the bottom-left, so the run that follows arc `a` is the straight
		// edge it ends on.
		private const int SideTop = 0;
		private const int SideRight = 1;
		private const int SideBottom = 2;
		private const int SideLeft = 3;

		private static readonly int[] s_arcFrom = { SideLeft, SideBottom, SideRight, SideTop };
		private static readonly int[] s_arcTo = { SideBottom, SideRight, SideTop, SideLeft };
		private static readonly int[] s_arcCounts = new int[4];

		/// <summary>
		/// Paints the border as four runs rather than one ring, so each side keeps its own width,
		/// colour and style, and the corners mitre between them.
		/// </summary>
		/// <remarks>
		/// The inner edge is the outer perimeter pushed inward along its own normals, which keeps the
		/// two point counts equal by construction — the previous version built a second perimeter from
		/// an inset rect and paired the two by index, which silently mispaired every point past a
		/// corner whose radius the border had eaten. A square corner is the exception: its normal
		/// points diagonally, so the inner point is the exact inset corner instead, and the quad edge
		/// running out to it is the 45° mitre CSS draws.
		/// </remarks>
		private static void EmitStroke(VertexHelper vh, Rect rect, Vector4 corners, in BorderPaint border)
		{
			var outer = BuildPerimeter(rect, corners, s_arcCounts);
			var normals = BuildNormals(outer);
			var inner = RentPoints();

			// The four exact inset corners, in the arc order BuildPerimeter emits.
			var insetBl = new Vector2(rect.xMin + border.Left, rect.yMin + border.Bottom);
			var insetBr = new Vector2(rect.xMax - border.Right, rect.yMin + border.Bottom);
			var insetTr = new Vector2(rect.xMax - border.Right, rect.yMax - border.Top);
			var insetTl = new Vector2(rect.xMin + border.Left, rect.yMax - border.Top);

			for (var i = 0; i < outer.Count; i++)
				inner.Add(Vector2.zero);

			var offset = 0;

			for (var arc = 0; arc < 4; arc++)
			{
				var count = s_arcCounts[arc];
				var from = border.WidthOf(s_arcFrom[arc]);
				var to = border.WidthOf(s_arcTo[arc]);

				for (var k = 0; k < count; k++)
				{
					var i = offset + k;

					if (count == 1)
					{
						inner[i] = arc switch
						{
							0 => insetBl,
							1 => insetBr,
							2 => insetTr,
							_ => insetTl,
						};

						continue;
					}

					// The width sweeps between the two sides across the arc, so a corner between a
					// thick and a thin edge tapers instead of stepping.
					var width = Mathf.Lerp(from, to, k / (float)(count - 1));
					inner[i] = outer[i] - normals[i] * width;
				}

				offset += count;
			}

			BuildSegments(outer.Count);
			EmitRuns(vh, outer, inner, border);

			ReturnPoints(inner);
			ReturnPoints(normals);
			ReturnPoints(outer);
		}

		// One quad band of the perimeter: the two points it spans, and which side owns it.
		private static readonly List<int> s_segFrom = new();
		private static readonly List<int> s_segTo = new();
		private static readonly List<int> s_segSide = new();

		/// <summary>
		/// Cuts the perimeter into the quads the stroke is built from, tagging each with the side
		/// whose width, colour and style it takes.
		/// </summary>
		private static void BuildSegments(int total)
		{
			s_segFrom.Clear();
			s_segTo.Clear();
			s_segSide.Clear();

			var offset = 0;

			for (var arc = 0; arc < 4; arc++)
			{
				var count = s_arcCounts[arc];

				// Segments inside the arc, switching side at the diagonal — which is exactly the
				// halfway point of a 90° arc, and so exactly where CSS mitres.
				for (var k = 0; k < count - 1; k++)
				{
					var side = (k + 0.5f) / (count - 1) < 0.5f ? s_arcFrom[arc] : s_arcTo[arc];
					AddSegment(offset + k, (offset + k + 1) % total, side);
				}

				// The straight run from this arc to the next is the edge the arc ended on.
				AddSegment(offset + count - 1, (offset + count) % total, s_arcTo[arc]);

				offset += count;
			}
		}

		private static void AddSegment(int from, int to, int side)
		{
			s_segFrom.Add(from);
			s_segTo.Add(to);
			s_segSide.Add(side);
		}

		/// <summary>
		/// Walks the perimeter one side at a time, starting at a mitre.
		/// </summary>
		/// <remarks>
		/// Starting at a mitre rather than at point zero is what makes a dash pattern legible: the
		/// perimeter is emitted from the bottom-left corner, which sits *halfway* along the left
		/// edge's run, so a walk that began there would restart the pattern mid-edge and leave a
		/// stray part-dash at that one corner. Rotating to the first side change gives each side one
		/// unbroken run, mitre to mitre.
		/// </remarks>
		private static void EmitRuns(VertexHelper vh, List<Vector2> outer, List<Vector2> inner, in BorderPaint border)
		{
			var count = s_segSide.Count;

			if (count == 0)
				return;

			var start = 0;

			for (var i = 0; i < count; i++)
			{
				if (s_segSide[i] == s_segSide[(i - 1 + count) % count])
					continue;

				start = i;

				break;
			}

			var at = 0;

			while (at < count)
			{
				var side = s_segSide[(start + at) % count];
				var end = at;

				while (end < count && s_segSide[(start + end) % count] == side)
					end++;

				EmitRun(vh, outer, inner, border, side, start, at, end, count);

				at = end;
			}
		}

		/// <summary>A `double` border is two strokes and a gap, each a third of the width.</summary>
		private const float DoubleBand = 1f / 3f;

		// Dash geometry, in multiples of the edge's own width — the relationship browsers use, so a
		// thicker edge gets proportionally longer dashes rather than more of them.
		private const float DashedPeriod = 5f;
		private const float DashedOnRatio = 0.6f;
		private const float DottedPeriod = 2f;
		private const float DottedOnRatio = 0.5f;

		private static void EmitRun(
			VertexHelper vh, List<Vector2> outer, List<Vector2> inner, in BorderPaint border,
			int side, int start, int from, int to, int count)
		{
			if (!border.Draws(side))
				return;

			var style = border.StyleOf(side);
			var color = border.ColorOf(side);

			switch (style)
			{
				case BorderStyle.Dashed:
				case BorderStyle.Dotted:
					EmitDashes(vh, outer, inner, border, side, start, from, to, count);

					break;

				case BorderStyle.Double:
					EmitBands(vh, outer, inner, start, from, to, count, 0f, DoubleBand, color);
					EmitBands(vh, outer, inner, start, from, to, count, 1f - DoubleBand, 1f, color);

					break;

				// A groove is a ridge lit from the other side, and each is the two bevels stacked:
				// outer half shaded one way, inner half the other.
				case BorderStyle.Groove:
				case BorderStyle.Ridge:
				{
					var outerIsInset = style == BorderStyle.Groove;

					EmitBands(vh, outer, inner, start, from, to, count, 0f, 0.5f, Bevel(color, side, outerIsInset));
					EmitBands(vh, outer, inner, start, from, to, count, 0.5f, 1f, Bevel(color, side, !outerIsInset));

					break;
				}

				case BorderStyle.Inset:
				case BorderStyle.Outset:
					EmitBands(vh, outer, inner, start, from, to, count, 0f, 1f,
						Bevel(color, side, style == BorderStyle.Inset));

					break;

				default:
					EmitBands(vh, outer, inner, start, from, to, count, 0f, 1f, color);

					break;
			}
		}

		/// <summary>
		/// Emits every segment of one run as a quad spanning t0..t1 of the way from the outer edge
		/// to the inner one.
		/// </summary>
		private static void EmitBands(
			VertexHelper vh, List<Vector2> outer, List<Vector2> inner,
			int start, int from, int to, int count, float t0, float t1, Color color)
		{
			for (var i = from; i < to; i++)
			{
				var segment = (start + i) % count;
				var a = s_segFrom[segment];
				var b = s_segTo[segment];

				EmitBand(vh, outer[a], inner[a], outer[b], inner[b], t0, t1, color);
			}
		}

		/// <summary>
		/// Paints one run as dashes or dots, measured along the outer edge so the pattern carries
		/// through a rounded corner instead of restarting at it.
		/// </summary>
		/// <remarks>
		/// The pattern is fitted to the run rather than taken literally. A run goes mitre to mitre, so
		/// solving for a whole number of dashes with a gap only *between* them is what lands ink on
		/// both ends of every side and keeps the four corners symmetrical; run the nominal period
		/// straight through instead and each side ends on whatever fraction of a dash is left over.
		/// The cost is a dash up to half a period off its nominal length, which no one can see.
		/// </remarks>
		private static void EmitDashes(
			VertexHelper vh, List<Vector2> outer, List<Vector2> inner, in BorderPaint border,
			int side, int start, int from, int to, int count)
		{
			var length = 0f;

			for (var i = from; i < to; i++)
			{
				var segment = (start + i) % count;
				length += Vector2.Distance(outer[s_segFrom[segment]], outer[s_segTo[segment]]);
			}

			if (length <= 0f)
				return;

			var dotted = border.StyleOf(side) == BorderStyle.Dotted;
			var nominal = Mathf.Max(0.01f, border.WidthOf(side) * (dotted ? DottedPeriod : DashedPeriod));
			var onRatio = dotted ? DottedOnRatio : DashedOnRatio;

			// `dashes` dashes need only `dashes - 1` gaps, which is the `1 - onRatio` in the count and
			// the `(dashes - 1) / onRatio` in the length that follows.
			var dashes = Mathf.Max(1, Mathf.RoundToInt(length / nominal + 1f - onRatio));
			var on = length / (1f + (dashes - 1) / onRatio);
			var period = on / onRatio;
			var color = border.ColorOf(side);

			var travelled = 0f;

			for (var i = from; i < to; i++)
			{
				var segment = (start + i) % count;
				var a = s_segFrom[segment];
				var b = s_segTo[segment];
				var span = Vector2.Distance(outer[a], outer[b]);

				if (span <= 0f)
					continue;

				// Every dash whose "on" stretch overlaps this segment, clipped to it — one that
				// straddles a segment boundary comes out as a quad on each side of it.
				for (var dash = Mathf.FloorToInt(travelled / period); dash * period < travelled + span; dash++)
				{
					var s0 = Mathf.Max(travelled, dash * period);
					var s1 = Mathf.Min(travelled + span, dash * period + on);

					if (s1 <= s0)
						continue;

					var u0 = (s0 - travelled) / span;
					var u1 = (s1 - travelled) / span;

					EmitBand(
						vh,
						Vector2.Lerp(outer[a], outer[b], u0), Vector2.Lerp(inner[a], inner[b], u0),
						Vector2.Lerp(outer[a], outer[b], u1), Vector2.Lerp(inner[a], inner[b], u1),
						0f, 1f, color);
				}

				travelled += span;
			}
		}

		/// <summary>How much the shaded half of a bevelled edge is darkened by.</summary>
		private const float BevelShade = 0.5f;

		/// <summary>
		/// The two-tone shading <c>inset</c>, <c>outset</c>, <c>groove</c> and <c>ridge</c> are drawn
		/// from: one pair of edges keeps the authored colour and the other is darkened, so the box
		/// reads as lit from the top left the way CSS draws it. Alpha is left alone — shading that
		/// too would fade the edge out rather than darken it.
		/// </summary>
		private static Color Bevel(Color color, int side, bool darkTopLeft)
		{
			var topLeft = side is SideTop or SideLeft;

			return topLeft == darkTopLeft
				? new Color(color.r * BevelShade, color.g * BevelShade, color.b * BevelShade, color.a)
				: color;
		}

		/// <summary>
		/// One quad of stroke, spanning two perimeter points and the t0..t1 slice of the distance
		/// from the outer edge to the inner one.
		/// </summary>
		private static void EmitBand(
			VertexHelper vh, Vector2 outerFrom, Vector2 innerFrom, Vector2 outerTo, Vector2 innerTo,
			float t0, float t1, Color color)
		{
			var index = vh.currentVertCount;
			var uv = new Vector2(0.5f, 0.5f);

			AddVert(vh, Vector2.Lerp(outerFrom, innerFrom, t0), color, uv);
			AddVert(vh, Vector2.Lerp(outerTo, innerTo, t0), color, uv);
			AddVert(vh, Vector2.Lerp(outerTo, innerTo, t1), color, uv);
			AddVert(vh, Vector2.Lerp(outerFrom, innerFrom, t1), color, uv);

			vh.AddTriangle(index, index + 1, index + 2);
			vh.AddTriangle(index, index + 2, index + 3);
		}

		private static void EmitChecker(VertexHelper vh, Rect rect, float cell, Color color)
		{
			var cols = Mathf.CeilToInt(rect.width / cell);
			var rows = Mathf.CeilToInt(rect.height / cell);

			for (var j = 0; j < rows; j++)
			{
				for (var i = 0; i < cols; i++)
				{
					if (((i + j) & 1) != 0)
						continue;

					var x0 = rect.xMin + i * cell;
					var y0 = rect.yMin + j * cell;
					var x1 = Mathf.Min(x0 + cell, rect.xMax);
					var y1 = Mathf.Min(y0 + cell, rect.yMax);
					AddQuad(vh, x0, y0, x1, y1, color);
				}
			}
		}

		/// <summary>
		/// Lines at the leading edge of every cell, measured from the top-left like a CSS background.
		/// </summary>
		private static void EmitGridLines(VertexHelper vh, Rect rect, float cell, float line, Color color)
		{
			for (var x = rect.xMin; x < rect.xMax; x += cell)
				AddQuad(vh, x, rect.yMin, Mathf.Min(x + line, rect.xMax), rect.yMax, color);

			for (var y = rect.yMax; y > rect.yMin; y -= cell)
				AddQuad(vh, rect.xMin, Mathf.Max(y - line, rect.yMin), rect.xMax, y, color);
		}

		private static void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Color color)
		{
			var index = vh.currentVertCount;
			var uv = new Vector2(0.5f, 0.5f);
			AddVert(vh, new Vector2(x0, y0), color, uv);
			AddVert(vh, new Vector2(x1, y0), color, uv);
			AddVert(vh, new Vector2(x1, y1), color, uv);
			AddVert(vh, new Vector2(x0, y1), color, uv);
			vh.AddTriangle(index, index + 1, index + 2);
			vh.AddTriangle(index, index + 2, index + 3);
		}

		private static void AddVert(VertexHelper vh, Vector2 position, Color color, Vector2 uv)
		{
			var v = UIVertex.simpleVert;
			v.position = position;
			v.color = color;
			v.uv0 = uv;
			vh.AddVert(v);
		}

		[AutoStaticsCleanup]
		private static readonly Stack<List<Vector2>> s_pointPool = new();

		private static List<Vector2> RentPoints()
		{
			return s_pointPool.Count > 0 ? s_pointPool.Pop() : new List<Vector2>((CornerSegments + 1) * 4);
		}

		private static void ReturnPoints(List<Vector2> points)
		{
			points.Clear();
			s_pointPool.Push(points);
		}

		/// <param name="arcCounts">
		/// Optional; receives how many points each corner arc contributed, in emit order
		/// (bottom-left, bottom-right, top-right, top-left). The stroke needs it to tell a corner
		/// from a straight edge — a square corner collapses to a single point.
		/// </param>
		private static List<Vector2> BuildPerimeter(Rect rect, Vector4 corners, int[]? arcCounts = null)
		{
			var maxR = 0.5f * Mathf.Min(rect.width, rect.height);
			var tl = Mathf.Clamp(corners.x, 0f, maxR);
			var tr = Mathf.Clamp(corners.y, 0f, maxR);
			var br = Mathf.Clamp(corners.z, 0f, maxR);
			var bl = Mathf.Clamp(corners.w, 0f, maxR);

			var pts = RentPoints();

			// Corner arc centres.
			var cBl = new Vector2(rect.xMin + bl, rect.yMin + bl);
			var cBr = new Vector2(rect.xMax - br, rect.yMin + br);
			var cTr = new Vector2(rect.xMax - tr, rect.yMax - tr);
			var cTl = new Vector2(rect.xMin + tl, rect.yMax - tl);

			var before = 0;

			AddArc(pts, cBl, bl, 180f, 270f);
			if (arcCounts != null) arcCounts[0] = pts.Count - before;
			before = pts.Count;

			AddArc(pts, cBr, br, 270f, 360f);
			if (arcCounts != null) arcCounts[1] = pts.Count - before;
			before = pts.Count;

			AddArc(pts, cTr, tr, 0f, 90f);
			if (arcCounts != null) arcCounts[2] = pts.Count - before;
			before = pts.Count;

			AddArc(pts, cTl, tl, 90f, 180f);
			if (arcCounts != null) arcCounts[3] = pts.Count - before;

			return pts;
		}

		private static void AddArc(List<Vector2> pts, Vector2 center, float radius, float fromDeg, float toDeg)
		{
			if (radius <= 0.01f)
			{
				pts.Add(center);
				return;
			}

			for (var i = 0; i <= CornerSegments; i++)
			{
				var t = Mathf.Lerp(fromDeg, toDeg, i / (float)CornerSegments) * Mathf.Deg2Rad;
				pts.Add(new Vector2(center.x + Mathf.Cos(t) * radius, center.y + Mathf.Sin(t) * radius));
			}
		}

		private static List<Vector2> BuildNormals(List<Vector2> perimeter)
		{
			var n = perimeter.Count;
			var normals = RentPoints();
			for (var i = 0; i < n; i++)
			{
				var prev = perimeter[(i - 1 + n) % n];
				var cur = perimeter[i];
				var next = perimeter[(i + 1) % n];

				var nEdgePrev = OutwardNormal(prev, cur);
				var nEdgeNext = OutwardNormal(cur, next);
				var sum = nEdgePrev + nEdgeNext;
				normals.Add(sum.sqrMagnitude > 1e-6f ? sum.normalized : nEdgeNext);
			}

			return normals;
		}

		private static Vector2 OutwardNormal(Vector2 a, Vector2 b)
		{
			var dir = (b - a);
			return dir.sqrMagnitude > 1e-6f ? new Vector2(dir.y, -dir.x).normalized : Vector2.zero;
		}

		private static Rect Inflate(Rect rect, float amount) =>
			new(rect.xMin - amount, rect.yMin - amount, rect.width + 2f * amount, rect.height + 2f * amount);

		private static Vector4 ExpandCorners(Vector4 corners, float amount) => new(
			Mathf.Max(0f, corners.x + amount),
			Mathf.Max(0f, corners.y + amount),
			Mathf.Max(0f, corners.z + amount),
			Mathf.Max(0f, corners.w + amount));

		private readonly struct GradientProjection
		{
			private readonly Vector2 _origin;
			private readonly Vector2 _axis;
			private readonly bool _radial;
			private readonly Vector2 _center;
			private readonly float _radius;

			private GradientProjection(Vector2 origin, Vector2 axis, bool radial, Vector2 center, float radius)
			{
				_origin = origin;
				_axis = axis;
				_radial = radial;
				_center = center;
				_radius = radius;
			}

			public static GradientProjection Build(Gradient gradient, List<Vector2> perimeter, Vector2 center)
			{
				if (gradient.Kind == GradientKind.Radial)
				{
					var maxR = 0.0001f;
					foreach (var p in perimeter)
						maxR = Mathf.Max(maxR, Vector2.Distance(p, center));

					return new GradientProjection(default, default, radial: true, center, maxR);
				}

				// CSS: 0deg points up (bottom→top); angle increases clockwise.
				var rad = gradient.Angle * Mathf.Deg2Rad;
				var dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));

				float min = float.MaxValue, max = float.MinValue;
				foreach (var p in perimeter)
				{
					var d = Vector2.Dot(p, dir);
					min = Mathf.Min(min, d);
					max = Mathf.Max(max, d);
				}

				var span = Mathf.Max(0.0001f, max - min);
				return new GradientProjection(dir * min, dir / span, radial: false, default, 0f);
			}

			public float T(Vector2 p)
			{
				if (_radial)
					return Mathf.Clamp01(Vector2.Distance(p, _center) / _radius);

				return Mathf.Clamp01(Vector2.Dot(p, _axis) - Vector2.Dot(_origin, _axis));
			}
		}
	}

	internal readonly struct ResolvedShadow : System.IEquatable<ResolvedShadow>
	{
		public readonly Vector2 Offset;
		public readonly float Blur;
		public readonly float Spread;
		public readonly Color Color;

		public ResolvedShadow(Vector2 offset, float blur, float spread, Color color)
		{
			Offset = offset;
			Blur = blur;
			Spread = spread;
			Color = color;
		}

		public bool Equals(ResolvedShadow other) =>
			Offset == other.Offset && Blur.Equals(other.Blur) && Spread.Equals(other.Spread) && Color == other.Color;

		public override bool Equals(object? obj) => obj is ResolvedShadow other && Equals(other);

		public override int GetHashCode() => System.HashCode.Combine(Offset, Blur, Spread, Color);
	}
}

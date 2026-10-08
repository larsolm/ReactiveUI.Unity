using ReactiveUI.Yoga;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ReactiveUI
{
	internal sealed class ScrollHost : VisualHost
	{
		[NoAutoStaticsCleanup]
		private static readonly Vector3[] s_corners = new Vector3[4];

		private ScrollRect _scrollRect = null!;
		private RectTransform _content = null!;
		private Bar _verticalBar = null!;
		private Bar _horizontalBar = null!;

		internal override HostKind Kind => HostKind.Scroll;

		internal ScrollAxis _axis = ScrollAxis.Vertical;

		private float _barWidth;
		private Color _thumbColor;
		private Color _trackColor;

		internal override RectTransform ContentRect => _content;

		/// <summary>A scroll view clips whether or not its sheet remembers to say so.</summary>
		protected override bool AlwaysClips => true;

		internal void Build()
		{
			var contentObject = new GameObject("Content", typeof(RectTransform));
			_content = contentObject.GetComponent<RectTransform>();
			_content.SetParent(_rectTransform, worldPositionStays: false);
			_content.anchorMin = new Vector2(0f, 1f);
			_content.anchorMax = new Vector2(0f, 1f);
			_content.pivot = new Vector2(0f, 1f);
			_content.anchoredPosition = Vector2.zero;

			_scrollRect = Ensure<ScrollRect>(null);
			_scrollRect.viewport = _rectTransform;
			_scrollRect.content = _content;
			_scrollRect.movementType = ScrollRect.MovementType.Clamped;
			_scrollRect.scrollSensitivity = 30f;
			_scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
			_scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

			// Built after the content so they draw over it.
			_verticalBar = new Bar(_rectTransform, vertical: true);
			_horizontalBar = new Bar(_rectTransform, vertical: false);

			_yoga.Overflow = YogaOverflow.Scroll;
		}

		internal override void ApplyProps(int node)
		{
			_axis = ElementPool.Props<ScrollProps>(node).Axis;

			_scrollRect.vertical = _axis is ScrollAxis.Vertical or ScrollAxis.Both;
			_scrollRect.horizontal = _axis is ScrollAxis.Horizontal or ScrollAxis.Both;

			EnablePointer();
			RefreshScrollbars();
		}

		internal override void ApplyStyle(ComputedStyle style, in StyleContext ctx)
		{
			base.ApplyStyle(style, ctx);

			_yoga.Overflow = YogaOverflow.Scroll;

			_barWidth = ctx.Resolve(style.Length(PropId.ScrollbarWidth));
			_thumbColor = style.Color(PropId.ScrollbarThumbColor, Color.clear);
			_trackColor = style.Color(PropId.ScrollbarTrackColor, Color.clear);

			RefreshScrollbars();
		}

		/// <summary>
		/// Attaches a bar to each scrolling axis while the style gives the bars a width, and detaches
		/// them otherwise. The ScrollRect sizes and moves the thumb and hides a bar whose content fits.
		/// </summary>
		private void RefreshScrollbars()
		{
			var show = _barWidth > 0f;

			_scrollRect.verticalScrollbar = show && _scrollRect.vertical ? _verticalBar.Apply(_barWidth, _thumbColor, _trackColor) : null;
			_scrollRect.horizontalScrollbar = show && _scrollRect.horizontal ? _horizontalBar.Apply(_barWidth, _thumbColor, _trackColor) : null;

			if (_scrollRect.verticalScrollbar == null)
				_verticalBar.Hide();

			if (_scrollRect.horizontalScrollbar == null)
				_horizontalBar.Hide();
		}

		internal override void AfterLayout()
		{
			var width = _yoga.LayoutWidth;
			var height = _yoga.LayoutHeight;
			var extentX = 0f;
			var extentY = 0f;

			for (var i = 0; i < _yoga.Count; i++)
			{
				var child = _yoga[i];
				extentX = Mathf.Max(extentX, child.LayoutLeft + child.LayoutWidth);
				extentY = Mathf.Max(extentY, child.LayoutTop + child.LayoutHeight);
			}

			_content.sizeDelta = new Vector2(Mathf.Max(extentX, width), Mathf.Max(extentY, height));
		}

		/// <summary>
		/// Scrolls the least distance that brings a descendant fully into view.
		/// </summary>
		/// <remarks>
		/// The leading edge wins when the target is larger than the viewport, so its heading stays
		/// readable rather than its bottom.
		/// </remarks>
		internal void Reveal(RectTransform target)
		{
			var viewport = _rectTransform.rect;

			target.GetWorldCorners(s_corners);
			var min = _rectTransform.InverseTransformPoint(s_corners[0]);
			var max = _rectTransform.InverseTransformPoint(s_corners[2]);

			var offset = _content.anchoredPosition;

			if (_scrollRect.vertical)
			{
				if (max.y > viewport.yMax)
					offset.y -= max.y - viewport.yMax;
				else if (min.y < viewport.yMin)
					offset.y += viewport.yMin - min.y;

				offset.y = Mathf.Clamp(offset.y, 0f, Mathf.Max(0f, _content.rect.height - viewport.height));
			}

			if (_scrollRect.horizontal)
			{
				if (min.x < viewport.xMin)
					offset.x += viewport.xMin - min.x;
				else if (max.x > viewport.xMax)
					offset.x -= max.x - viewport.xMax;

				offset.x = Mathf.Clamp(offset.x, -Mathf.Max(0f, _content.rect.width - viewport.width), 0f);
			}

			if (offset == _content.anchoredPosition)
				return;

			_scrollRect.StopMovement();
			_content.anchoredPosition = offset;
		}

		internal override void ResetForPool()
		{
			base.ResetForPool();

			_axis = ScrollAxis.Vertical;
			_scrollRect.verticalScrollbar = null;
			_scrollRect.horizontalScrollbar = null;
			_verticalBar.Hide();
			_horizontalBar.Hide();

			if (_content != null)
			{
				_content.anchoredPosition = Vector2.zero;
				_content.sizeDelta = Vector2.zero;
			}
		}

		/// <summary>
		/// A uGUI scrollbar pinned to one edge of the viewport: a track the width of the bar with the
		/// thumb inside it. It never takes navigation focus, so focus stays with the node tree.
		/// </summary>
		private sealed class Bar
		{
			private readonly bool _vertical;
			private readonly RectTransform _rect;
			private readonly UnityEngine.UI.Image _track;
			private readonly UnityEngine.UI.Image _thumb;
			private readonly Scrollbar _scrollbar;

			public Bar(RectTransform viewport, bool vertical)
			{
				_vertical = vertical;

				var barObject = new GameObject(vertical ? "Vertical Scrollbar" : "Horizontal Scrollbar", typeof(RectTransform));
				_rect = barObject.GetComponent<RectTransform>();
				_rect.SetParent(viewport, worldPositionStays: false);
				_rect.anchorMin = vertical ? new Vector2(1f, 0f) : Vector2.zero;
				_rect.anchorMax = vertical ? Vector2.one : new Vector2(1f, 0f);
				_rect.pivot = vertical ? new Vector2(1f, 0.5f) : new Vector2(0.5f, 0f);
				_rect.anchoredPosition = Vector2.zero;

				_track = barObject.AddComponent<UnityEngine.UI.Image>();
				barObject.AddComponent<CanvasMaterialModifier>();

				var thumbObject = new GameObject("Thumb", typeof(RectTransform));
				var thumbRect = thumbObject.GetComponent<RectTransform>();
				thumbRect.SetParent(_rect, worldPositionStays: false);
				thumbRect.sizeDelta = Vector2.zero;

				_thumb = thumbObject.AddComponent<UnityEngine.UI.Image>();
				thumbObject.AddComponent<CanvasMaterialModifier>();

				_scrollbar = barObject.AddComponent<Scrollbar>();
				_scrollbar.handleRect = thumbRect;
				_scrollbar.targetGraphic = _thumb;
				_scrollbar.transition = Selectable.Transition.None;
				_scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
				_scrollbar.direction = vertical ? Scrollbar.Direction.BottomToTop : Scrollbar.Direction.LeftToRight;

				Hide();
			}

			public Scrollbar Apply(float width, Color thumb, Color track)
			{
				_rect.sizeDelta = _vertical ? new Vector2(width, 0f) : new Vector2(0f, width);
				_thumb.color = thumb;
				_track.color = track;

				return _scrollbar;
			}

			public void Hide()
			{
				_scrollbar.gameObject.SetActive(false);
			}
		}
	}
}

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

		internal override HostKind Kind => HostKind.Scroll;

		internal ScrollAxis _axis = ScrollAxis.Vertical;

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

			_yoga.Overflow = YogaOverflow.Scroll;
		}

		internal override void ApplyProps(int node)
		{
			_axis = ElementPool.Props<ScrollProps>(node).Axis;

			_scrollRect.vertical = _axis is ScrollAxis.Vertical or ScrollAxis.Both;
			_scrollRect.horizontal = _axis is ScrollAxis.Horizontal or ScrollAxis.Both;

			EnablePointer();
		}

		internal override void ApplyStyle(ComputedStyle style, in StyleContext ctx)
		{
			base.ApplyStyle(style, ctx);

			_yoga.Overflow = YogaOverflow.Scroll;
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

			if (_content != null)
			{
				_content.anchoredPosition = Vector2.zero;
				_content.sizeDelta = Vector2.zero;
			}
		}
	}
}

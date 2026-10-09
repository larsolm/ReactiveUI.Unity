using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ReactiveUI
{
	internal sealed class PressableHost : VisualHost
	{
		private PressableBehaviour? _behaviour;

		internal override HostKind Kind => HostKind.Pressable;

		protected override bool AlwaysPaint => true;

		internal Action? _onClick;
		internal Action<Vector2>? _onClickAt;
		internal Action? _onPressDown;
		internal Action? _onPressUp;
		internal Action? _onHoverEnter;
		internal Action? _onHoverExit;
		internal Func<Vector2, bool>? _onMove;
		internal Action<Vector2>? _onDragAt;
		internal bool _disabled;
		internal bool _focusable = true;

		internal void Bind(PressableBehaviour behaviour)
		{
			_behaviour = behaviour;
			_behaviour._host = this;
		}

		internal override void ApplyProps(int node)
		{
			if (_behaviour != null)
				_behaviour._host = this;

			UpdateCallbacks(node);
			EnablePointer();
		}

		internal override void UpdateCallbacks(int node)
		{
			var props = ElementPool.Props<PressableProps>(node);

			_onClick = props.OnClick;
			_onClickAt = props.OnClickAt;
			_onPressDown = props.OnPressDown;
			_onPressUp = props.OnPressUp;
			_onHoverEnter = props.OnHoverEnter;
			_onHoverExit = props.OnHoverExit;
			_onMove = props.OnMove;
			_onDragAt = props.OnDragAt;
			_disabled = props.Disabled;
			_focusable = !props.Unfocusable;

			SetState(UiStates.s_disabled, _disabled);
		}

		internal override void ResetForPool()
		{
			base.ResetForPool();

			_onClick = null;
			_onClickAt = null;
			_onPressDown = null;
			_onPressUp = null;
			_onHoverEnter = null;
			_onHoverExit = null;
			_onMove = null;
			_onDragAt = null;
			_disabled = false;
			_focusable = true;

			if (_behaviour != null)
				_behaviour._host = null;
		}
	}

	internal sealed class PressableBehaviour : MonoBehaviour,
		IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler,
		IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler
	{
		internal PressableHost? _host;

		private bool Drags => _host is { _disabled: false, _onDragAt: not null };

		void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
		{
			if (_host is null || _host._disabled)
				return;

			InputModalityTracker.NotePointer();

			if (_host._focusable)
				_host.Focus();

			_host._onClick?.Invoke();
			_host._onClickAt?.Invoke(PointFromTopLeft(eventData));
		}

		void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
		{
			if (_host is null || _host._disabled)
				return;

			_host.SetState(UiStates.s_active, true);
			_host._onPressDown?.Invoke();

			if (_host._onDragAt is not null)
			{
				InputModalityTracker.NotePointer();
				_host._onDragAt.Invoke(PointFromTopLeft(eventData));
			}
		}

		void IInitializePotentialDragHandler.OnInitializePotentialDrag(PointerEventData eventData)
		{
			if (Drags)
			{
				eventData.useDragThreshold = false;
				return;
			}

			// The module routes every drag event to the nearest drag handler, which is now this
			// element, so a non-dragging pressable hands the drag on to its ancestors (e.g. a Scroll).
			var parent = AncestorHandler<IInitializePotentialDragHandler>();
			if (parent != null)
				ExecuteEvents.Execute(parent, eventData, ExecuteEvents.initializePotentialDrag);
		}

		void IBeginDragHandler.OnBeginDrag(PointerEventData eventData)
		{
			if (Drags)
				return;

			eventData.pointerDrag = AncestorHandler<IDragHandler>();

			if (eventData.pointerDrag != null)
				ExecuteEvents.Execute(eventData.pointerDrag, eventData, ExecuteEvents.beginDragHandler);
		}

		void IDragHandler.OnDrag(PointerEventData eventData)
		{
			if (Drags)
				_host!._onDragAt!.Invoke(PointFromTopLeft(eventData));
		}

		private GameObject? AncestorHandler<T>() where T : IEventSystemHandler
		{
			var parent = transform.parent;

			return parent == null ? null : ExecuteEvents.GetEventHandler<T>(parent.gameObject);
		}

		private Vector2 PointFromTopLeft(PointerEventData eventData)
		{
			var rectTransform = _host!._rectTransform;
			var camera = eventData.pressEventCamera != null ? eventData.pressEventCamera : eventData.enterEventCamera;

			RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, camera, out var local);

			var rect = rectTransform.rect;

			return new Vector2(local.x - rect.xMin, rect.yMax - local.y);
		}

		void IPointerUpHandler.OnPointerUp(PointerEventData eventData)
		{
			if (_host is null || _host._disabled)
				return;

			_host.SetState(UiStates.s_active, false);
			_host._onPressUp?.Invoke();
		}

		void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
		{
			if (_host is null || _host._disabled)
				return;

			_host.SetPointerInside(true);
			_host._onHoverEnter?.Invoke();
		}

		void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
		{
			if (_host is null || _host._disabled)
				return;

			_host.SetPointerInside(false);
			_host.SetState(UiStates.s_active, false);
			_host._onHoverExit?.Invoke();
		}
	}
}

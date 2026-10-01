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
			_disabled = false;
			_focusable = true;

			if (_behaviour != null)
				_behaviour._host = null;
		}
	}

	internal sealed class PressableBehaviour : MonoBehaviour,
		IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
	{
		internal PressableHost? _host;

		void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
		{
			if (_host is null || _host._disabled)
				return;

			InputModalityTracker.NotePointer();

			if (_host._focusable)
				_host.Focus();

			_host._onClick?.Invoke();

			if (_host._onClickAt is null)
				return;

			RectTransformUtility.ScreenPointToLocalPointInRectangle(
				_host._rectTransform, eventData.position, eventData.pressEventCamera, out var local);
			_host._onClickAt.Invoke(local);
		}

		void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
		{
			if (_host is null || _host._disabled)
				return;

			_host.SetState(UiStates.s_active, true);
			_host._onPressDown?.Invoke();
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

			_host.SetState(UiStates.s_hover, true);
			_host._onHoverEnter?.Invoke();
		}

		void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
		{
			if (_host is null || _host._disabled)
				return;

			_host.SetState(UiStates.s_hover, false);
			_host.SetState(UiStates.s_active, false);
			_host._onHoverExit?.Invoke();
		}
	}
}

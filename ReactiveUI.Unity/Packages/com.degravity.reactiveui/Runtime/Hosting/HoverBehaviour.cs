using UnityEngine;
using UnityEngine.EventSystems;

namespace ReactiveUI
{
	internal sealed class HoverBehaviour : MonoBehaviour,
		IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
	{
		internal HostInstance? Host;

		public void OnPointerEnter(PointerEventData eventData)
		{
			Host?.SetState(UiStates.s_hover, true);
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			Host?.SetState(UiStates.s_hover, false);
			Host?.SetState(UiStates.s_active, false);
		}

		public void OnPointerDown(PointerEventData eventData)
		{
			Host?.SetState(UiStates.s_active, true);
		}

		public void OnPointerUp(PointerEventData eventData)
		{
			Host?.SetState(UiStates.s_active, false);
		}
	}
}

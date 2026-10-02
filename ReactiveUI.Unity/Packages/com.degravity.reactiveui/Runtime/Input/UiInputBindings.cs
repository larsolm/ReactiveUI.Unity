using UnityEngine.InputSystem;

namespace ReactiveUI
{
	/// <summary>
	/// The game's own actions for moving and activating focus.
	/// </summary>
	/// <remarks>
	/// Either may be left null, and that half of navigation falls back to the built-in device reads
	/// (arrows, d-pad, stick; Enter, Space, gamepad South). Supplying them is what makes navigation obey
	/// the player's rebinds and the map the game enables — a disabled action navigates nothing.
	/// </remarks>
	internal sealed class UiInputBindings
	{
		/// <summary>A Vector2 action; its value is snapped to an axis and repeats while held.</summary>
		public InputAction? Navigate { get; }

		/// <summary>A button action that activates whatever holds focus.</summary>
		public InputAction? Submit { get; }

		public UiInputBindings(InputAction? navigate = null, InputAction? submit = null)
		{
			Navigate = navigate;
			Submit = submit;
		}
	}
}

using System;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>
	/// How the player is currently driving the interface.
	/// </summary>
	public enum InputModality
	{
		Pointer,
		Navigation,
	}

	/// <summary>
	/// Tracks whether the player is using a pointer or a gamepad/keyboard.
	/// </summary>
	/// <remarks>
	/// This is what separates <c>:focus</c> from <c>:focus-visible</c>. A button clicked with a
	/// mouse is focused, but drawing a navigation highlight around it looks like a bug; the same
	/// button reached with a stick must show one, or the player cannot tell where they are. CSS
	/// solved this with <c>:focus-visible</c>, and the distinction is only meaningful if something
	/// knows which device is in charge.
	/// </remarks>
	[AutoStaticsCleanup]
	public static partial class InputModalityTracker
	{
		public static InputModality Current { get; private set; } = InputModality.Pointer;

		/// <summary>
		/// Raised when the modality changes, so focus visuals can be re-resolved.
		/// </summary>
		public static event Action<InputModality> Changed = null!;

		/// <summary>
		/// Called when a pointer moves or clicks.
		/// </summary>
		public static void NotePointer()
		{
			Set(InputModality.Pointer);
		}

		/// <summary>
		/// Called when a navigation input (stick, d-pad, arrow keys, tab) is used.
		/// </summary>
		public static void NoteNavigation()
		{
			Set(InputModality.Navigation);
		}

		private static void Set(InputModality modality)
		{
			if (Current == modality)
				return;

			Current = modality;
			Changed.Invoke(modality);
		}
	}
}

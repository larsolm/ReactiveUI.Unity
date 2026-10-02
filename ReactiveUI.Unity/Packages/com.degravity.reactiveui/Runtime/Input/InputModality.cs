using System;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>
	/// How the player is currently interacting with the UI.
	/// </summary>
	public enum InputModality
	{
		/// <summary>A mouse or touch pointer.</summary>
		Pointer,

		/// <summary>Directional navigation from a gamepad or keyboard.</summary>
		Navigation,
	}

	/// <summary>
	/// Tracks the player's current <see cref="InputModality"/>.
	/// </summary>
	[AutoStaticsCleanup]
	public static partial class InputModalityTracker
	{
		/// <summary>
		/// The current input modality.
		/// </summary>
		public static InputModality Current { get; private set; } = InputModality.Pointer;

		/// <summary>
		/// Raised when <see cref="Current"/> changes.
		/// </summary>
		public static event Action<InputModality> Changed = null!;

		/// <summary>
		/// Called when a pointer moves or clicks.
		/// </summary>
		internal static void NotePointer()
		{
			Set(InputModality.Pointer);
		}

		/// <summary>
		/// Called when a navigation input (stick, d-pad, arrow keys, tab) is used.
		/// </summary>
		internal static void NoteNavigation()
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

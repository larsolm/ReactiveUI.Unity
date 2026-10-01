using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ReactiveUI
{
	/// <summary>
	/// The kind of device the player last used.
	/// </summary>
	public enum InputDeviceKind
	{
		/// <summary>Keyboard and mouse, which are treated as one hand on one desk.</summary>
		Keyboard,
		Gamepad,
	}

	/// <summary>
	/// The button family of the gamepad the player last used, which decides which glyphs a prompt shows.
	/// </summary>
	public enum GamepadLayout
	{
		Generic,
		Xbox,
		PlayStation,
		Switch,
	}

	/// <summary>
	/// Tracks which device the player last touched, and what kind of gamepad it was.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is what <c>@media (input-device: …)</c> and <c>@media (gamepad-layout: …)</c> answer. It is
	/// a different question from <see cref="InputModalityTracker"/>: arrow keys are navigation but still
	/// a keyboard, and a prompt has to say "Enter" to that player, not "A".
	/// </para>
	/// <para>
	/// Process-wide rather than per runtime, like modality — one player, one pair of hands. Layouts are
	/// matched by name through the layout hierarchy, so derived pads (DualSense from DualShock, the
	/// platform Xbox variants from XInput) classify without this code naming each platform's type.
	/// </para>
	/// </remarks>
	[AutoStaticsCleanup]
	public static partial class InputDeviceTracker
	{
		public static InputDeviceKind Device { get; private set; } = InitialDevice();

		public static GamepadLayout Layout { get; private set; } = InitialLayout();

		/// <summary>
		/// Raised when either the device kind or the gamepad layout changes.
		/// </summary>
		public static event Action Changed = null!;

		/// <summary>
		/// Starts following input events. Idempotent, and safe to call again after statics are reset.
		/// </summary>
		/// <remarks>
		/// Event-driven rather than polled: a device's per-frame flags depend on which update type and
		/// state buffer is current, which differs between play mode, edit mode and a test fixture, while
		/// an event says plainly which device just changed.
		/// </remarks>
		public static void Listen()
		{
			InputSystem.onEvent -= OnEvent;
			InputSystem.onEvent += OnEvent;
		}

		/// <summary>
		/// Records that the player just used <paramref name="device"/>.
		/// </summary>
		public static void Note(InputDevice device)
		{
			if (device is Gamepad)
				Set(InputDeviceKind.Gamepad, Classify(device));
			else if (device is Keyboard or Mouse)
				Set(InputDeviceKind.Keyboard, Layout);
		}

		/// <remarks>
		/// A pad counts once a control moves past half its range, so stick drift and idle state reports
		/// cannot flip prompts back to gamepad glyphs while the player types. Events from the device
		/// already in charge return before anything is enumerated, which is nearly every event.
		/// </remarks>
		private static void OnEvent(InputEventPtr eventPtr, InputDevice device)
		{
			if (device is Gamepad)
			{
				if (Device == InputDeviceKind.Gamepad && Layout == Classify(device))
					return;
			}
			else if (device is Keyboard or Mouse)
			{
				if (Device == InputDeviceKind.Keyboard)
					return;
			}
			else
			{
				return;
			}

			if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>())
				return;

			foreach (var _ in eventPtr.EnumerateChangedControls(device, magnitudeThreshold: 0.5f))
			{
				Note(device);

				return;
			}
		}

		private static void Set(InputDeviceKind device, GamepadLayout layout)
		{
			if (Device == device && Layout == layout)
				return;

			Device = device;
			Layout = layout;
			Changed?.Invoke();
		}

		private static GamepadLayout Classify(InputDevice device)
		{
			var layout = device.layout;

			if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "XInputController"))
				return GamepadLayout.Xbox;

			if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "DualShockGamepad"))
				return GamepadLayout.PlayStation;

			if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "SwitchProControllerHID"))
				return GamepadLayout.Switch;

			return GamepadLayout.Generic;
		}

		/// <remarks>
		/// A machine with a pad and no keyboard — a console — starts on the pad, so the first frame
		/// already shows the right prompts.
		/// </remarks>
		private static InputDeviceKind InitialDevice()
		{
			return Keyboard.current is null && Gamepad.current is not null
				? InputDeviceKind.Gamepad
				: InputDeviceKind.Keyboard;
		}

		private static GamepadLayout InitialLayout()
		{
			return Gamepad.current is { } gamepad ? Classify(gamepad) : GamepadLayout.Generic;
		}
	}
}

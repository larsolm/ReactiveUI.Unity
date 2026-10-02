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
		/// <summary>A keyboard or mouse.</summary>
		Keyboard,

		/// <summary>A gamepad.</summary>
		Gamepad,
	}

	/// <summary>
	/// The button layout of the gamepad the player last used.
	/// </summary>
	public enum GamepadLayout
	{
		/// <summary>An unrecognized gamepad.</summary>
		Generic,

		/// <summary>An Xbox-style gamepad.</summary>
		Xbox,

		/// <summary>A PlayStation-style gamepad.</summary>
		PlayStation,

		/// <summary>A Nintendo Switch-style gamepad.</summary>
		Switch,
	}

	/// <summary>
	/// Tracks the kind of device the player last used and, for gamepads, its layout.
	/// </summary>
	[AutoStaticsCleanup]
	public static partial class InputDeviceTracker
	{
		/// <summary>
		/// The kind of device the player last used.
		/// </summary>
		public static InputDeviceKind Device { get; private set; } = InitialDevice();

		/// <summary>
		/// The layout of the gamepad the player last used.
		/// </summary>
		public static GamepadLayout Layout { get; private set; } = InitialLayout();

		/// <summary>
		/// Raised when <see cref="Device"/> or <see cref="Layout"/> changes.
		/// </summary>
		public static event Action Changed = null!;

		/// <summary>
		/// Starts following input events. Idempotent.
		/// </summary>
		internal static void Listen()
		{
			InputSystem.onEvent -= OnEvent;
			InputSystem.onEvent += OnEvent;
		}

		/// <summary>
		/// Records that the player just used <paramref name="device"/>.
		/// </summary>
		internal static void Note(InputDevice device)
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

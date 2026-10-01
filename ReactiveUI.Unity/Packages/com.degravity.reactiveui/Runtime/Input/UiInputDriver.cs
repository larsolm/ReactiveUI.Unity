using UnityEngine;
using UnityEngine.InputSystem;

namespace ReactiveUI
{
	internal sealed class UiInputDriver
	{
		private const float RepeatDelay = 0.4f;
		private const float RepeatInterval = 0.12f;

		private const float StickDeadzone = 0.5f;

		private readonly FocusManager _focus;
		private readonly HotkeyRegistry _hotkeys;
		private readonly InputActionRegistry _actions;
		private readonly UiInputBindings? _bindings;

		private Vector2 _heldDirection;
		private float _nextRepeat;
		private Vector2 _lastPointerPosition;

		public UiInputDriver(
			FocusManager focus, HotkeyRegistry hotkeys, InputActionRegistry actions, UiInputBindings? bindings)
		{
			_focus = focus;
			_hotkeys = hotkeys;
			_actions = actions;
			_bindings = bindings;
		}

		public void Update()
		{
			TrackPointer();
			TrackNavigation();
			TrackSubmit();
			TrackHotkeys();
			_actions.Dispatch();
		}

		private void TrackPointer()
		{
			var mouse = Mouse.current;
			if (mouse is null)
				return;

			var position = mouse.position.ReadValue();

			// Movement alone is enough: reaching for the mouse should drop the navigation ring
			// before the player clicks anything.
			if ((position - _lastPointerPosition).sqrMagnitude > 4f)
			{
				_lastPointerPosition = position;
				InputModalityTracker.NotePointer();
			}

			if (mouse.leftButton.wasPressedThisFrame)
			{
				InputModalityTracker.NotePointer();
			}
		}

		private void TrackNavigation()
		{
			var direction = ReadDirection();

			if (direction == Vector2.zero)
			{
				_heldDirection = Vector2.zero;

				return;
			}

			// A fresh press moves at once; holding waits out the delay before it starts repeating,
			// so a deliberate nudge never skips two entries.
			if (direction != _heldDirection)
			{
				_heldDirection = direction;
				_nextRepeat = Time.unscaledTime + RepeatDelay;
				Navigate(direction);

				return;
			}

			if (Time.unscaledTime < _nextRepeat)
			{
				return;
			}

			_nextRepeat = Time.unscaledTime + RepeatInterval;
			Navigate(direction);
		}

		private void Navigate(Vector2 direction)
		{
			InputModalityTracker.NoteNavigation();
			_focus.Move(direction);
		}

		private Vector2 ReadDirection()
		{
			if (_bindings?.Navigate is { } navigate)
				return Snap(navigate.ReadValue<Vector2>());

			var keyboard = Keyboard.current;

			if (keyboard is not null)
			{
				if (keyboard.leftArrowKey.isPressed) return Vector2.left;
				if (keyboard.rightArrowKey.isPressed) return Vector2.right;
				if (keyboard.upArrowKey.isPressed) return Vector2.up;
				if (keyboard.downArrowKey.isPressed) return Vector2.down;
			}

			var gamepad = Gamepad.current;
			if (gamepad is null)
				return Vector2.zero;

			var stick = gamepad.leftStick.ReadValue();
			if (gamepad.dpad.ReadValue() is { sqrMagnitude: > 0f } dpad)
				stick = dpad;

			return Snap(stick);
		}

		private static Vector2 Snap(Vector2 stick)
		{
			if (stick.sqrMagnitude < StickDeadzone * StickDeadzone)
				return Vector2.zero;

			// Snapped to an axis: diagonal drift should not send focus somewhere unintended.
			return Mathf.Abs(stick.x) > Mathf.Abs(stick.y)
				? new Vector2(Mathf.Sign(stick.x), 0f)
				: new Vector2(0f, Mathf.Sign(stick.y));
		}

		private void TrackSubmit()
		{
			var pressed = _bindings?.Submit is { } submit
				? submit.WasPerformedThisFrame()
				: (Keyboard.current?.enterKey.wasPressedThisFrame ?? false)
					|| (Keyboard.current?.spaceKey.wasPressedThisFrame ?? false)
					|| (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false);

			if (!pressed)
				return;

			InputModalityTracker.NoteNavigation();
			_focus.Submit();
		}

		private void TrackHotkeys()
		{
			var keyboard = Keyboard.current;
			if (keyboard is null || _hotkeys.IsEmpty)
				return;

			if (!keyboard.anyKey.wasPressedThisFrame)
				return;

			foreach (var control in keyboard.allKeys)
			{
				if (control.wasPressedThisFrame)
					_hotkeys.Dispatch(control.keyCode);
			}
		}
	}
}

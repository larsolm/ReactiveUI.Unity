using System;
using UnityEngine.InputSystem;

namespace ReactiveUI
{
	public static partial class Ui
	{
		/// <summary>
		/// Returns the runtime's focus manager.
		/// </summary>
		public static FocusManager UseFocus()
		{
			return Current().Focus;
		}

		/// <summary>
		/// Confines focus navigation to <paramref name="scope"/> while this component is mounted.
		/// </summary>
		/// <remarks>
		/// Focus outside the scope is cleared when the scope is entered. When <paramref name="restoreFocus"/>
		/// is true, the previously focused node is refocused on unmount.
		/// </remarks>
		public static void UseFocusScope(ElementRef scope, bool restoreFocus = true)
		{
			UseEffect(
				(focus: Current().Focus, scope, restoreFocus),
				static args =>
				{
					if (args.scope._host is not { } host)
						return null;

					var focus = args.focus;
					var previous = focus.Focused;

					focus.PushScope(host);

					return () =>
					{
						focus.PopScope(host);

						if (args.restoreFocus && previous is { _unmounted: false })
							focus.Focus(previous);
					};
				});
		}

		/// <summary>
		/// Focuses <paramref name="target"/> once after this component mounts, and makes it the default focus while it is mounted.
		/// </summary>
		/// <remarks>
		/// The default is where focus lands when navigation starts or the focused element goes away.
		/// The most recently mounted default in the active scope wins.
		/// </remarks>
		public static void UseAutoFocus(ElementRef target)
		{
			UseEffect(
				(focus: Current().Focus, target),
				static args =>
				{
					if (args.target._host is not { } host)
						return null;

					var focus = args.focus;

					focus.AddDefault(host);
					focus.Focus(host);

					return () => focus.RemoveDefault(host);
				});
		}

		/// <summary>
		/// Invokes <paramref name="action"/> whenever <paramref name="inputAction"/> is performed while this component is mounted.
		/// </summary>
		/// <remarks>
		/// Only the most recently mounted binding for an action is invoked.
		/// </remarks>
		public static void UseInputAction(InputAction inputAction, Action action)
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new InputActionHook(store));
			hook.Bind(inputAction, action);
		}

		/// <inheritdoc cref="UseInputAction(InputAction, Action)"/>
		public static void UseInputAction<TState>(InputAction inputAction, TState state, Action<TState> action)
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new InputActionHook<TState>(store));
			hook.Bind(inputAction, state, action);
		}
	}
}

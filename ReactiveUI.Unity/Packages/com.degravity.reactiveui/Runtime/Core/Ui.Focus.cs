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
		/// Focuses <paramref name="target"/> once after this component mounts.
		/// </summary>
		public static void UseAutoFocus(ElementRef target)
		{
			UseEffect(
				(focus: Current().Focus, target),
				static args =>
				{
					args.focus.Focus(args.target);
					return null;
				});
		}

		/// <summary>
		/// Invokes <paramref name="action"/> whenever <paramref name="inputAction"/> is performed while this component is mounted.
		/// </summary>
		/// <remarks>
		/// Only the most recently mounted binding for an action is invoked.
		/// </remarks>
		public static void UseInputAction(InputAction? inputAction, Action action)
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new InputActionHook(store));
			hook.Bind(inputAction, action);
		}

		/// <inheritdoc cref="UseInputAction(InputAction?, Action)"/>
		public static void UseInputAction<TState>(InputAction? inputAction, TState state, Action<TState> action)
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new InputActionHook<TState>(store));
			hook.Bind(inputAction, state, action);
		}
	}
}

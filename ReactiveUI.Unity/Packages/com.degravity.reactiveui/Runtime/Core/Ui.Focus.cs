using System;
using UnityEngine.InputSystem;

namespace ReactiveUI
{
	public static partial class Ui
	{
		/// <summary>
		/// The runtime's focus manager, for moving focus imperatively from a handler or effect.
		/// </summary>
		/// <remarks>
		/// Not a hook — it takes no slot and may be called anywhere in a render.
		/// </remarks>
		public static FocusManager UseFocus()
		{
			return Current().Focus;
		}

		/// <summary>
		/// Confines navigation to the node behind <paramref name="scope"/> for as long as this
		/// component is mounted.
		/// </summary>
		/// <remarks>
		/// Pushed after the first layout and popped at unmount. Focus left outside the scope is dropped
		/// when it is pushed; with <paramref name="restoreFocus"/>, whatever held focus before is focused
		/// again when the scope goes away — what closing a dialog should do.
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
		/// Focuses the node behind <paramref name="target"/> once, when this component mounts.
		/// </summary>
		/// <remarks>
		/// Runs after layout, so a target inside a scroll view is scrolled into view. The navigation
		/// ring still follows input modality: a pointer user gets <c>:focus</c> without
		/// <c>:focus-visible</c>.
		/// </remarks>
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
		/// Calls <paramref name="action"/> whenever <paramref name="inputAction"/> is performed, for as
		/// long as this component is mounted.
		/// </summary>
		/// <remarks>
		/// Last-binding-wins per action, like <see cref="UseHotkey{TState}"/>, so a modal binding Cancel
		/// shadows the screen beneath it. A null or disabled action binds nothing.
		/// </remarks>
		public static void UseInputAction<TState>(InputAction? inputAction, TState state, Action<TState> action)
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new InputActionHook<TState>(store));
			hook.Bind(inputAction, state, action);
		}
	}
}

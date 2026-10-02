using System;

namespace ReactiveUI
{
	public static partial class Ui
	{
		/// <summary>
		/// Returns state that persists across renders; setting it re-renders the component.
		/// </summary>
		public static State<T> UseState<T>(T initial)
		{
			var hook = Current().GetOrCreate(initial, static (store, value) => new StateHook<T>(store, value));
			return new State<T>(hook);
		}

		/// <summary>
		/// Returns a mutable box that persists across renders; changing it does not re-render.
		/// </summary>
		public static Ref<T> UseRef<TState, T>(TState state, Func<TState, T> initial)
		{
			var hook = Current().GetOrCreate((state, initial), static (_, args) => new RefHook<T>(args.initial(args.state)));
			return hook.Ref;
		}

		/// <summary>
		/// Returns a mutable box, initialized to a new <typeparamref name="T"/>, that persists across renders.
		/// </summary>
		public static Ref<T> UseRef<T>()
			where T : new()
		{
			var hook = Current().GetOrCreate(0, static (_, _) => new RefHook<T>(new T()));
			return hook.Ref;
		}

		/// <summary>
		/// Creates a value on the first render and returns the same value on every later render.
		/// </summary>
		public static T UseConstant<TState, T>(TState state, Func<TState, T> create)
		{
			return UseRef(state, create).Value;
		}

		/// <summary>
		/// Creates a new <typeparamref name="T"/> on the first render and returns the same value on every later render.
		/// </summary>
		public static T UseConstant<T>()
			where T : new()
		{
			return UseRef<T>().Value;
		}

		/// <summary>
		/// Returns the cached result of <paramref name="factory"/>, recomputing it when <paramref name="deps"/> changes.
		/// </summary>
		public static TValue UseMemo<TState, TValue, TDeps>(TState state, Func<TState, TValue> factory, TDeps deps)
			where TDeps : IEquatable<TDeps>
		{
			var hook = Current().GetOrCreate(0, static (_, _) => new MemoHook<TValue, TDeps>());

			return hook.GetOrCompute(deps, state, factory);
		}

		/// <summary>
		/// Runs <paramref name="effect"/> after layout whenever <paramref name="deps"/> changes and the
		/// returned cleanup action runs before the next invocation and at unmount.
		/// </summary>
		public static void UseEffect<TState, TDeps>(TState state, Func<TState, Action?> effect, TDeps deps)
			where TDeps : IEquatable<TDeps>
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new EffectHook<TState, TDeps>(store));
			hook.Schedule(deps, state, effect);
		}

		/// <summary>
		/// Runs <paramref name="effect"/> once after the component mounts and the returned cleanup action runs at unmount.
		/// </summary>
		public static void UseEffect<TState>(TState state, Func<TState, Action?> effect)
		{
			UseEffect(state, effect, 0);
		}

		/// <summary>
		/// Returns a stable handler that invokes <paramref name="callback"/> with the latest <paramref name="state"/>.
		/// </summary>
		public static Action UseCallback<TState>(TState state, Action<TState> callback)
		{
			var hook = Current().GetOrCreate(0, static (_, _) => new CallbackHook<TState>());

			return hook.Bind(callback, state);
		}

		/// <summary>
		/// Returns a stable handler that invokes <paramref name="callback"/> with the latest <paramref name="state"/> and its argument.
		/// </summary>
		public static Action<TArg> UseCallback<TState, TArg>(TState state, Action<TState, TArg> callback)
		{
			var hook = Current().GetOrCreate(0, static (_, _) => new CallbackHook<TState, TArg>());

			return hook.Bind(callback, state);
		}

		/// <summary>
		/// Returns a stable handler that invokes <paramref name="callback"/> with the latest <paramref name="argument"/>.
		/// </summary>
		public static Action UseCallback<TArg>(Action<TArg>? callback, TArg argument)
		{
			return UseCallback((callback, argument), static state => state.callback?.Invoke(state.argument));
		}

		/// <summary>
		/// Returns a persistent handle that can be attached to an element.
		/// </summary>
		public static ElementRef UseElementRef()
		{
			var hook = Current().GetOrCreate(0, static (_, _) => new RefHook<ElementRef>(new ElementRef()));
			return hook.Ref.Value;
		}
	}
}

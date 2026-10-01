using System;

namespace ReactiveUI
{
	public static partial class Ui
	{
		/// <summary>
		/// State that persists across renders; setting it schedules a re-render.
		/// </summary>
		public static State<T> UseState<T>(T initial)
		{
			var hook = Current().GetOrCreate(initial, static (store, value) => new StateHook<T>(store, value));
			return new State<T>(hook);
		}

		/// <summary>
		/// A mutable box built on the first render, which survives re-renders and never triggers one.
		/// </summary>
		public static Ref<T> UseRef<TState, T>(TState state, Func<TState, T> initial)
		{
			var hook = Current().GetOrCreate((state, initial), static (_, args) => new RefHook<T>(args.initial(args.state)));
			return hook.Ref;
		}

		/// <summary>
		/// <see cref="UseRef{TState, T}"/> holding a default <typeparamref name="T"/>, for the box
		/// whose starting value needs nothing built.
		/// </summary>
		public static Ref<T> UseRef<T>()
			where T : new()
		{
			var hook = Current().GetOrCreate(0, static (_, _) => new RefHook<T>(new T()));
			return hook.Ref;
		}

		/// <summary>
		/// Builds a value on the first render and returns that same one ever after.
		/// </summary>
		public static T UseConstant<TState, T>(TState state, Func<TState, T> create)
		{
			return UseRef(state, create).Value;
		}

		/// <summary>
		/// <see cref="UseConstant{TState, T}"/> for a value built from nothing.
		/// </summary>
		public static T UseConstant<T>()
			where T : new()
		{
			return UseRef<T>().Value;
		}

		/// <summary>
		/// Recomputes only when <paramref name="deps"/> changes.
		/// </summary>
		/// <remarks>
		/// Write <paramref name="factory"/> as a <c>static</c> lambda and reach everything it needs
		/// through <paramref name="state"/>; capturing a local instead allocates a closure on every
		/// render, including the ones the memo skips.
		/// </remarks>
		public static TValue UseMemo<TState, TValue, TDeps>(TState state, Func<TState, TValue> factory, TDeps deps)
			where TDeps : IEquatable<TDeps>
		{
			var hook = Current().GetOrCreate(0, static (_, _) => new MemoHook<TValue, TDeps>());

			return hook.GetOrCompute(deps, state, factory);
		}

		/// <summary>
		/// Runs after layout when <paramref name="deps"/> changes. Return a cleanup action to undo
		/// what the effect did; it runs before the next invocation and again at unmount.
		/// </summary>
		public static void UseEffect<TState, TDeps>(TState state, Func<TState, Action?> effect, TDeps deps)
			where TDeps : IEquatable<TDeps>
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new EffectHook<TState, TDeps>(store));
			hook.Schedule(deps, state, effect);
		}

		/// <summary>
		/// Runs once, when the component mounts. Return a cleanup action to run at unmount.
		/// </summary>
		public static void UseEffect<TState>(TState state, Func<TState, Action?> effect)
		{
			UseEffect(state, effect, 0);
		}

		/// <summary>
		/// A handler built from <paramref name="state"/> instead of a closure over it. The returned
		/// delegate keeps one identity for the component's life and always sees the latest state.
		/// </summary>
		public static Action UseCallback<TState>(TState state, Action<TState> callback)
		{
			var hook = Current().GetOrCreate(0, static (_, _) => new CallbackHook<TState>());

			return hook.Bind(callback, state);
		}

		/// <summary>
		/// <see cref="UseCallback{TState}(TState, Action{TState})"/> for a handler that is also
		/// passed an argument by whatever raises it.
		/// </summary>
		public static Action<TArg> UseCallback<TState, TArg>(TState state, Action<TState, TArg> callback)
		{
			var hook = Current().GetOrCreate(0, static (_, _) => new CallbackHook<TState, TArg>());

			return hook.Bind(callback, state);
		}

		/// <summary>
		/// A no-argument handler that calls <paramref name="callback"/> with a fixed
		/// <paramref name="argument"/> — what a leaf does with the delegate its parent handed down.
		/// </summary>
		public static Action UseCallback<TArg>(Action<TArg>? callback, TArg argument)
		{
			return UseCallback((callback, argument), static state => state.callback?.Invoke(state.argument));
		}

		/// <summary>
		/// A stable handle to hang on an element, for driving it imperatively between renders.
		/// </summary>
		public static ElementRef UseElementRef()
		{
			var hook = Current().GetOrCreate(0, static (_, _) => new RefHook<ElementRef>(new ElementRef()));
			return hook.Ref.Value;
		}
	}
}

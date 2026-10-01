using System;

namespace ReactiveUI
{
	public static partial class Ui
	{
		/// <summary>
		/// Re-renders this component whenever <paramref name="source"/> raises the event
		/// <paramref name="subscribe"/> attaches to.
		/// </summary>
		public static void UseSubscription<TSource>(
			TSource? source,
			Action<TSource, Action> subscribe,
			Action<TSource, Action> unsubscribe)
			where TSource : class
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new DelegateBindingHook<TSource>(store));

			hook.Bind(source, subscribe, unsubscribe);
		}

		/// <summary><see cref="UseSubscription{TSource}"/> for an event that carries a value.</summary>
		public static void UseSubscription<TSource, TArg>(
			TSource? source,
			Action<TSource, Action<TArg>> subscribe,
			Action<TSource, Action<TArg>> unsubscribe)
			where TSource : class
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new DelegateBindingHook<TSource, TArg>(store));
			hook.Bind(source, subscribe, unsubscribe);
		}

		/// <summary>
		/// Reads how the player is currently driving the interface, and re-renders when that changes.
		/// </summary>
		public static InputModality UseInputModality()
		{
			Current().GetOrCreate(0, static (store, _) => new ModalityHook(store));
			return InputModalityTracker.Current;
		}

		/// <summary>
		/// Reads whether a media query currently holds, and re-renders when that answer flips.
		/// </summary>
		public static bool UseMedia(string query)
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new MediaHook(store));
			return hook.Read(query);
		}

		/// <summary>
		/// Reads the space this UI is laid out in, and re-renders whenever it changes.
		/// </summary>
		public static Viewport UseViewport()
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new ViewportHook(store));
			return hook.Read();
		}

		/// <summary>
		/// Reads the value the nearest provider above this component provides, and throws when
		/// nothing does.
		/// </summary>
		public static T UseContext<T>()
			where T : class
		{
			return UseContextOrNull<T>() ?? throw new MissingContextException(
				$"{CurrentName()} read {typeof(T).Name} from context, but nothing above it "
				+ $"provides one. Wrap it in a ContextProvider<{typeof(T).Name}>.");
		}

		/// <summary>
		/// <see cref="UseContext{T}"/> that returns null instead of throwing when nothing provides one.
		/// </summary>
		/// <remarks>
		/// Takes a hook slot and subscribes to the provider it found, so a changed value still reaches
		/// this component when everything between them memoised. That is why it must not be called
		/// conditionally.
		/// </remarks>
		public static T? UseContextOrNull<T>()
			where T : class
		{
			var hooks = Current();
			var hook = hooks.GetOrCreate(0, static (store, _) => new ContextHook(store));

			for (Instance? instance = hooks.Instance; instance is not null; instance = instance._parent)
			{
				if (instance is ProviderInstance provider && provider.Value is T value)
				{
					hook.Bind(provider);

					return value;
				}
			}

			hook.Bind(null);

			return null;
		}

		/// <summary>
		/// Reads the nearest provided <see cref="Store"/> of type <typeparamref name="T"/> and
		/// re-renders this component whenever it changes.
		/// </summary>
		public static T UseStore<T>()
			where T : Store
		{
			var store = UseContext<T>();
			var hook = Current().GetOrCreate(0, static (hooks, _) => new StoreHook(hooks));

			hook.Bind(store);

			return store;
		}
	}
}

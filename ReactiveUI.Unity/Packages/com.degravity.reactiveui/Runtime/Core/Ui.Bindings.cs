using System;

namespace ReactiveUI
{
	public static partial class Ui
	{
		/// <summary>
		/// Re-renders the component whenever the event on <paramref name="source"/> is raised.
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

		/// <summary>
		/// Re-renders the component whenever the event on <paramref name="source"/> is raised.
		/// </summary>
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
		/// Returns the current input modality and re-renders the component when it changes.
		/// </summary>
		public static InputModality UseInputModality()
		{
			Current().GetOrCreate(0, static (store, _) => new ModalityHook(store));
			return InputModalityTracker.Current;
		}

		/// <summary>
		/// Returns whether the CSS media <paramref name="query"/> matches and re-renders the component when that changes.
		/// </summary>
		public static bool UseMedia(string query)
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new MediaHook(store));
			return hook.Read(query);
		}

		/// <summary>
		/// Returns the UI's viewport and re-renders the component when it changes.
		/// </summary>
		public static Viewport UseViewport()
		{
			var hook = Current().GetOrCreate(0, static (store, _) => new ViewportHook(store));
			return hook.Read();
		}

		/// <summary>
		/// Returns the value from the nearest <see cref="ContextProvider{T}"/> above this component.
		/// </summary>
		/// <exception cref="MissingContextException">No provider of <typeparamref name="T"/> exists above this component.</exception>
		public static T UseContext<T>()
			where T : class
		{
			return UseContextOrNull<T>() ?? throw new MissingContextException(
				$"{CurrentName()} read {typeof(T).Name} from context, but nothing above it "
				+ $"provides one. Wrap it in a ContextProvider<{typeof(T).Name}>.");
		}

		/// <summary>
		/// Returns the value from the nearest <see cref="ContextProvider{T}"/> above this component, or null if there is none.
		/// </summary>
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
		/// Returns the nearest provided <typeparamref name="T"/> store and re-renders the component when it changes.
		/// </summary>
		/// <exception cref="MissingContextException">No provider of <typeparamref name="T"/> exists above this component.</exception>
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

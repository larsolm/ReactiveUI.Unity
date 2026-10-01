using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Thrown when hooks are called in a different order than on the first render.
	/// </summary>
	public sealed class HookOrderException : Exception
	{
		internal HookOrderException(string message) : base(message)
		{
		}
	}

	internal abstract class Hook
	{
		public virtual void Dispose()
		{
		}
	}

	/// <summary>
	/// A hook that holds one external source and stays attached to it for as long as the component
	/// is mounted.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every reactive read in the framework is this same shape — a Soap variable, a Soap collection,
	/// a <see cref="Store"/>, a context provider — so the shape lives here once. What each of them
	/// adds is only which event to attach to.
	/// </para>
	/// <para>
	/// The source is re-bound every render rather than cached, because a component can be handed a
	/// different one, or moved under a different provider, without unmounting. <see cref="Bind"/> is
	/// a reference compare when it has not changed, which is the common case.
	/// </para>
	/// <para>
	/// The null test is a plain reference check rather than Unity's, deliberately. A Soap asset that
	/// has been destroyed is still a live managed object holding a live delegate list, so comparing
	/// with <c>!=</c> would skip the detach and leak the listener; detaching from a managed event
	/// never touches the native side and is safe on a destroyed object.
	/// </para>
	/// </remarks>
	internal abstract class BindingHook<TSource> : Hook
		where TSource : class
	{
		protected readonly HookStore _hooks;

		private TSource? _source;

		protected BindingHook(HookStore hooks)
		{
			_hooks = hooks;
		}

		/// <summary>
		/// Re-renders the component that read this source.
		/// </summary>
		protected void Invalidate()
		{
			_hooks.Scheduler.MarkDirty(_hooks.Instance);
		}

		public void Bind(TSource? source)
		{
			if (ReferenceEquals(_source, source))
				return;

			if (_source is not null)
				Unsubscribe(_source);

			_source = source;

			if (_source is not null)
				Subscribe(_source);
		}

		protected abstract void Subscribe(TSource source);

		protected abstract void Unsubscribe(TSource source);

		public sealed override void Dispose()
		{
			Bind(null);
		}
	}

	/// <summary>
	/// Keeps a consumer subscribed to the context provider it last read from.
	/// </summary>
	/// <remarks>
	/// Without this, context reaches consumers only because re-rendering a provider re-renders
	/// everything under it — which stops being true the moment anything in between is memoised.
	/// Subscribing means a changed value can mark its consumers dirty directly, so the components
	/// between them stay skipped, which is the whole point of skipping them.
	/// </remarks>
	internal sealed class ContextHook : BindingHook<ProviderInstance>
	{
		public ContextHook(HookStore hooks) : base(hooks)
		{
		}

		/// The provider marks its consumers dirty itself, so there is no event to attach to — being
		/// on the list is the subscription.
		protected override void Subscribe(ProviderInstance source) => source.AddConsumer(_hooks.Instance);

		protected override void Unsubscribe(ProviderInstance source) => source.RemoveConsumer(_hooks.Instance);
	}

	internal sealed class HookStore
	{
		public RenderInstance Instance { get; }

		public Scheduler Scheduler { get; }

		public HotkeyRegistry? Hotkeys { get; }

		public InputActionRegistry? Actions { get; }

		public FocusManager Focus { get; }

		/// <summary>
		/// The runtime's live media environment, for the hooks that read the viewport.
		/// </summary>
		public MediaWatch Media { get; }

		private readonly List<Hook> _slots = new(4);

		private int _cursor;
		private int _committedCount = -1;

		public HookStore(
			RenderInstance instance,
			Scheduler scheduler,
			HotkeyRegistry? hotkeys,
			InputActionRegistry? actions,
			FocusManager focus,
			MediaWatch media)
		{
			Instance = instance;
			Scheduler = scheduler;
			Hotkeys = hotkeys;
			Actions = actions;
			Focus = focus;
			Media = media;
		}

		public void BeginRender()
		{
			_cursor = 0;
		}

		public void EndRender()
		{
			if (_committedCount < 0)
			{
				_committedCount = _cursor;

				return;
			}

			if (_cursor == _committedCount)
				return;

			throw new HookOrderException(
				$"{Instance.Name} called {_cursor} hooks this render but "
				+ $"{_committedCount} on its first render. Hooks must not be called conditionally.");
		}

		public THook GetOrCreate<THook, TArg>(in TArg arg, Func<HookStore, TArg, THook> create)
			where THook : Hook
		{
			if (_cursor < _slots.Count)
			{
				if (_slots[_cursor] is not THook typed)
				{
					throw new HookOrderException(
						$"{Instance.Name} expected {_slots[_cursor].GetType().Name} at hook "
						+ $"slot {_cursor} but got {typeof(THook).Name}. Hooks must be called in a stable order.");
				}

				_cursor++;

				return typed;
			}

			if (_committedCount >= 0)
			{
				throw new HookOrderException(
					$"{Instance.Name} called more hooks than the {_committedCount} seen on "
					+ "its first render. Hooks must not be called conditionally.");
			}

			var hook = create(this, arg);
			_slots.Add(hook);
			_cursor++;

			return hook;
		}

		public void Dispose()
		{
			for (var i = 0; i < _slots.Count; i++)
			{
				try
				{
					_slots[i].Dispose();
				}
				catch (Exception ex)
				{
					Debug.LogException(ex);
				}
			}

			_slots.Clear();
		}
	}
}

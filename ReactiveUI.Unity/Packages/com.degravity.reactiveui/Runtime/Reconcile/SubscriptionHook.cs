using System;

namespace ReactiveUI
{
	/// <summary>
	/// Attaches to an arbitrary source's event and re-renders when it fires — the general case
	/// behind <c>UseSubscription</c>, for a reactive source the framework knows nothing about.
	/// </summary>
	/// <remarks>
	/// The subscribe and unsubscribe delegates are captured on the first render and kept, so the
	/// pair that detaches is always the pair that attached. That is why passing a different lambda
	/// on a later render cannot desynchronise them, and why the caller is asked to write them
	/// <c>static</c>: it is the allocation, not the identity, that would otherwise be paid per
	/// render.
	/// </remarks>
	internal sealed class DelegateBindingHook<TSource> : BindingHook<TSource>
		where TSource : class
	{
		private readonly Action _listener;

		private Action<TSource, Action>? _subscribe;
		private Action<TSource, Action>? _unsubscribe;

		public DelegateBindingHook(HookStore hooks) : base(hooks)
		{
			_listener = Invalidate;
		}

		public void Bind(TSource? source, Action<TSource, Action> subscribe, Action<TSource, Action> unsubscribe)
		{
			_subscribe ??= subscribe;
			_unsubscribe ??= unsubscribe;

			Bind(source);
		}

		protected override void Subscribe(TSource source) => _subscribe!(source, _listener);

		protected override void Unsubscribe(TSource source) => _unsubscribe!(source, _listener);
	}

	/// <summary>
	/// <see cref="DelegateBindingHook{TSource}"/> for an event that carries a value.
	/// </summary>
	/// <remarks>
	/// A separate hook rather than an adapter, because the delegate handed to <c>+=</c> has to be
	/// the same instance handed to <c>-=</c>; wrapping an <see cref="Action"/> in a lambda to fit an
	/// <see cref="Action{T}"/> event would mint a new one each time and never detach.
	/// </remarks>
	internal sealed class DelegateBindingHook<TSource, TArg> : BindingHook<TSource>
		where TSource : class
	{
		private readonly Action<TArg> _listener;

		private Action<TSource, Action<TArg>>? _subscribe;
		private Action<TSource, Action<TArg>>? _unsubscribe;

		public DelegateBindingHook(HookStore hooks) : base(hooks)
		{
			_listener = _ => Invalidate();
		}

		public void Bind(
			TSource? source,
			Action<TSource, Action<TArg>> subscribe,
			Action<TSource, Action<TArg>> unsubscribe)
		{
			_subscribe ??= subscribe;
			_unsubscribe ??= unsubscribe;

			Bind(source);
		}

		protected override void Subscribe(TSource source) => _subscribe!(source, _listener);

		protected override void Unsubscribe(TSource source) => _unsubscribe!(source, _listener);
	}

	/// <summary>
	/// Re-renders when the player switches between pointer and navigation input.
	/// </summary>
	/// <remarks>
	/// Its own hook rather than a <see cref="BindingHook{TSource}"/> because the source is static:
	/// there is no object to hold, so there is nothing to rebind and the subscription can simply
	/// last as long as the hook does.
	/// </remarks>
	internal sealed class ModalityHook : Hook
	{
		private readonly Action<InputModality> _listener;

		public ModalityHook(HookStore hooks)
		{
			_listener = _ => hooks.Scheduler.MarkDirty(hooks.Instance);
			InputModalityTracker.Changed += _listener;
		}

		public override void Dispose()
		{
			InputModalityTracker.Changed -= _listener;
		}
	}

	/// <summary>
	/// Re-renders when a media query's answer flips.
	/// </summary>
	/// <remarks>
	/// The one place this departs from <see cref="ModalityHook"/>, which reads its value live at every
	/// render: here the hook holds the last <em>answer</em>, because what it subscribes to is the
	/// environment, which moves on every pixel of a resize, while the answer moves a handful of times
	/// across the whole drag. Caching the boolean is what turns a resize from a re-render per frame into
	/// a re-render per breakpoint.
	/// </remarks>
	internal sealed class MediaHook : Hook
	{
		private readonly HookStore _hooks;
		private readonly Action _listener;

		private MediaCondition? _condition;
		private bool _matches;

		public MediaHook(HookStore hooks)
		{
			_hooks = hooks;
			_listener = OnChanged;
			hooks.Media.Changed += _listener;
		}

		/// <summary>Reads the query, compiling it the first time this hook is handed one.</summary>
		/// <remarks>
		/// A component passing a different string on a later render is re-bound rather than refused: the
		/// hook's identity is its slot, not the query it happens to be reading this frame.
		/// </remarks>
		public bool Read(string query)
		{
			var condition = _hooks.Media.Condition(query);

			if (!ReferenceEquals(condition, _condition))
			{
				_condition = condition;
				_matches = condition.Evaluate(_hooks.Media.Environment);
			}

			return _matches;
		}

		private void OnChanged()
		{
			if (_condition is null)
				return;

			var matches = _condition.Evaluate(_hooks.Media.Environment);

			if (matches == _matches)
				return;

			_matches = matches;
			_hooks.Scheduler.MarkDirty(_hooks.Instance);
		}

		public override void Dispose()
		{
			_hooks.Media.Changed -= _listener;
		}
	}

	/// <summary>
	/// Re-renders whenever the space this UI is laid out in changes.
	/// </summary>
	/// <remarks>
	/// Every pixel is a new answer, so unlike <see cref="MediaHook"/> this really does re-render on every
	/// frame of a drag. That is the trade a component doing arithmetic on the size is asking for; one
	/// that only needs a breakpoint should read one. The rem size is deliberately not part of the
	/// comparison — rescaling the UI does not move the viewport.
	/// </remarks>
	internal sealed class ViewportHook : Hook
	{
		private readonly HookStore _hooks;
		private readonly Action _listener;

		private Viewport _viewport;

		public ViewportHook(HookStore hooks)
		{
			_hooks = hooks;
			_viewport = hooks.Media.Environment.Viewport;
			_listener = OnChanged;
			hooks.Media.Changed += _listener;
		}

		public Viewport Read()
		{
			_viewport = _hooks.Media.Environment.Viewport;

			return _viewport;
		}

		private void OnChanged()
		{
			if (_viewport.Equals(_hooks.Media.Environment.Viewport))
				return;

			_hooks.Scheduler.MarkDirty(_hooks.Instance);
		}

		public override void Dispose()
		{
			_hooks.Media.Changed -= _listener;
		}
	}
}

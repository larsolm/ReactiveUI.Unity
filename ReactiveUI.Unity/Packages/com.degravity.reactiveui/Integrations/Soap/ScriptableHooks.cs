using System;
using Obvious.Soap;

namespace ReactiveUI.Soap
{
	internal sealed class ScriptableHook<T> : BindingHook<ScriptableVariable<T>>
	{
		private readonly Action<T> _listener;

		public ScriptableHook(HookStore hooks) : base(hooks)
		{
			_listener = _ => Invalidate();
		}

		protected override void Subscribe(ScriptableVariable<T> source) => source.OnValueChanged += _listener;

		protected override void Unsubscribe(ScriptableVariable<T> source) => source.OnValueChanged -= _listener;
	}

	/// <summary>
	/// Re-renders when a Soap collection's contents change.
	/// </summary>
	/// <remarks>
	/// Bound to <see cref="ScriptableCollection"/> rather than to a list of a particular element
	/// type, because <c>Modified</c> — the only thing this needs — is declared there. That is the
	/// whole of what makes one hook serve both lists and dictionaries, and it keeps a generic
	/// instantiation from being emitted per element type.
	/// </remarks>
	internal sealed class ScriptableCollectionHook : BindingHook<ScriptableCollection>
	{
		private readonly Action _listener;

		public ScriptableCollectionHook(HookStore hooks) : base(hooks)
		{
			_listener = Invalidate;
		}

		protected override void Subscribe(ScriptableCollection source) => source.Modified += _listener;

		protected override void Unsubscribe(ScriptableCollection source) => source.Modified -= _listener;
	}

	/// <summary>
	/// Runs a handler when a Soap event is raised, for as long as the component is mounted.
	/// </summary>
	/// <remarks>
	/// The one binding hook that does not re-render: an event is a signal rather than state, so
	/// there is nothing new for a render to read. The handler decides what to do about it.
	/// <para>
	/// The handler is refreshed every render and dispatched through a stable listener, so it is
	/// never the stale one from the first render — an event handler almost always closes over props,
	/// which is exactly what would go stale otherwise.
	/// </para>
	/// </remarks>
	internal sealed class ScriptableEventHook<TState> : BindingHook<ScriptableEventNoParam>
	{
		private readonly Action _listener;

		private Action<TState>? _handler;
		private TState _state = default!;

		public ScriptableEventHook(HookStore hooks) : base(hooks)
		{
			_listener = () => _handler?.Invoke(_state);
		}

		public void Bind(ScriptableEventNoParam? evt, TState state, Action<TState> handler)
		{
			_handler = handler;
			_state = state;

			Bind(evt);
		}

		protected override void Subscribe(ScriptableEventNoParam source) => source.OnRaised += _listener;

		protected override void Unsubscribe(ScriptableEventNoParam source) => source.OnRaised -= _listener;
	}

	/// <summary>
	/// <see cref="ScriptableEventHook{TState}"/> for an event that carries a value.
	/// </summary>
	internal sealed class ScriptableEventHook<TState, T> : BindingHook<ScriptableEvent<T>>
	{
		private readonly Action<T> _listener;

		private Action<TState, T>? _handler;
		private TState _state = default!;

		public ScriptableEventHook(HookStore hooks) : base(hooks)
		{
			_listener = value => _handler?.Invoke(_state, value);
		}

		public void Bind(ScriptableEvent<T>? evt, TState state, Action<TState, T> handler)
		{
			_handler = handler;
			_state = state;

			Bind(evt);
		}

		protected override void Subscribe(ScriptableEvent<T> source) => source.OnRaised += _listener;

		protected override void Unsubscribe(ScriptableEvent<T> source) => source.OnRaised -= _listener;
	}
}

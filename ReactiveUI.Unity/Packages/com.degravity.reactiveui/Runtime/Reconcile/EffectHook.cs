using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	internal sealed class EffectHook<TState, TDeps> : Hook
		where TDeps : IEquatable<TDeps>
	{
		[NoAutoStaticsCleanup]
		private static readonly EqualityComparer<TDeps> s_deps = EqualityComparer<TDeps>.Default;

		private readonly HookStore _store;
		private readonly Action _run;

		private TDeps _deps = default!;
		private TState _state = default!;
		private Func<TState, Action?>? _effect;
		private bool _armed;
		private bool _scheduled;
		private bool _disposed;
		private Action? _cleanup;

		public EffectHook(HookStore store)
		{
			_store = store;
			_run = Run;
		}

		public void Schedule(in TDeps deps, TState state, Func<TState, Action?> effect)
		{
			_state = state;
			_effect = effect;

			if (_armed && s_deps.Equals(_deps, deps))
				return;

			_deps = deps;
			_armed = true;

			// One delegate is enqueued rather than a fresh closure, so a second dep change in the same
			// frame would otherwise queue the same run twice and invoke the effect twice for one state.
			if (_scheduled)
				return;

			_scheduled = true;
			_store.Scheduler.Enqueue(_run);
		}

		private void Run()
		{
			_scheduled = false;

			// Effects run at the end of the frame, by which time the component may be gone: a
			// render pass can mount it and a later pass in the same frame unmount it again.
			// Running then would be worse than not running — the cleanup would be handed to a
			// hook that has already been disposed, so it would never be invoked at all.
			if (_disposed)
				return;

			_cleanup?.Invoke();
			_cleanup = _effect!(_state);
		}

		public override void Dispose()
		{
			_disposed = true;
			_cleanup?.Invoke();
			_cleanup = null;
		}
	}
}

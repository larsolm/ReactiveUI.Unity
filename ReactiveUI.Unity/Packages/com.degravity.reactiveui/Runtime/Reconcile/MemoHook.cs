using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	internal sealed class MemoHook<TValue, TDeps> : Hook
		where TDeps : IEquatable<TDeps>
	{
		[NoAutoStaticsCleanup]
		private static readonly EqualityComparer<TDeps> s_deps = EqualityComparer<TDeps>.Default;

		public TValue Value = default!;

		private TDeps _deps = default!;
		private bool _hasValue;

		public TValue GetOrCompute<TState>(in TDeps deps, TState state, Func<TState, TValue> factory)
		{
			if (_hasValue && s_deps.Equals(_deps, deps))
				return Value;

			Value = factory(state);
			_deps = deps;
			_hasValue = true;

			return Value;
		}
	}
}

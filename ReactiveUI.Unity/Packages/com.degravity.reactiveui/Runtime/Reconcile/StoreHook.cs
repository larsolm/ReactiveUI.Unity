using System;

namespace ReactiveUI
{
	/// <summary>
	/// Keeps a consumer subscribed to the <see cref="Store"/> it read, so that a change marks it
	/// dirty directly rather than having to reach it through an ancestor's render.
	/// </summary>
	/// <remarks>
	/// A component between the store and the reader may be memoised and never re-render, so the
	/// notification has to arrive at the reader itself — the same reason every other
	/// <see cref="BindingHook{TSource}"/> exists.
	/// </remarks>
	internal sealed class StoreHook : BindingHook<Store>
	{
		private readonly Action _listener;

		public StoreHook(HookStore hooks) : base(hooks)
		{
			_listener = Invalidate;
		}

		protected override void Subscribe(Store source) => source.Changed += _listener;

		protected override void Unsubscribe(Store source) => source.Changed -= _listener;
	}
}

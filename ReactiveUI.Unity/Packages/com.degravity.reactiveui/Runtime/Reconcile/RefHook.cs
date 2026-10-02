namespace ReactiveUI
{
	/// <summary>
	/// A mutable box that persists across renders; changing it does not re-render.
	/// </summary>
	public sealed class Ref<T>
	{
		/// <summary>
		/// The current value.
		/// </summary>
		public T Value;

		internal Ref(T value) => Value = value;
	}

	internal sealed class RefHook<T> : Hook
	{
		public readonly Ref<T> Ref;

		public RefHook(T initial)
		{
			Ref = new Ref<T>(initial);
		}
	}
}

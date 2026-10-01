namespace ReactiveUI
{
	/// <summary>
	/// A mutable box that survives re-renders without causing one.
	/// </summary>
	public sealed class Ref<T>
	{
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

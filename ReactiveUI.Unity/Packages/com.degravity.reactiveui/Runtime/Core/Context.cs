using System.Collections;

namespace ReactiveUI
{
	/// <summary>
	/// Makes one value visible to everything it renders — the app's services, a run's HUD controls,
	/// a board's geometry. Anything most of a tree needs and nobody wants to thread by hand.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A context is identified by <typeparamref name="T"/> itself, so there is nothing to declare:
	/// any class can be provided, and <c>UseContext&lt;T&gt;</c> finds the nearest provider above it.
	/// The type is the key, which means one context per type — if you want two of something, give
	/// them two types.
	/// </para>
	/// <para>
	/// Reference types only. A context is read by walking up the instance tree, so a value type would
	/// have to be boxed on every provider, and the values worth putting here are services rather
	/// than data anyway.
	/// </para>
	/// <para>
	/// A group node rather than a component, so it owns no render of its own: its children are
	/// reconciled in place exactly as a <see cref="Fragment"/>'s are, and the provided value lives on
	/// the instance the reconciler keeps. A changed value is noticed when the node is reconciled, and
	/// the instance marks its subscribed consumers dirty directly — which is what reaches a consumer
	/// sitting under a component that memoised and so re-rendered nothing.
	/// </para>
	/// </remarks>
	public readonly struct ContextProvider<T> : IElement
		where T : class
	{
		public Element Handle { get; }

		public ContextProvider(T value)
		{
			Handle = Element.Provider(value);
		}

		public static implicit operator Element(ContextProvider<T> self) => self.Handle;
		public static Element? operator &(bool value, ContextProvider<T> self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(ContextProvider<T> self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable("ContextProvider");
	}
}

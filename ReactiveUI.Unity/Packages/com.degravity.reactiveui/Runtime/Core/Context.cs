using System.Collections;

namespace ReactiveUI
{
	/// <summary>
	/// Provides a value of type <typeparamref name="T"/> to its descendants through <see cref="Ui.UseContext{T}"/>.
	/// </summary>
	/// <remarks>
	/// Contexts are keyed by type; the nearest provider of <typeparamref name="T"/> wins.
	/// </remarks>
	public readonly struct ContextProvider<T> : IElement
		where T : class
	{
		/// <inheritdoc/>
		public Element Handle { get; }

		/// <summary>
		/// Creates a provider for <paramref name="value"/>.
		/// </summary>
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

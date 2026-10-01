using System.Collections;

namespace ReactiveUI
{
	/// <summary>
	/// Renders its children into the overlay root instead of in place.
	/// </summary>
	public readonly struct Portal : IElement
	{
		public Element Handle { get; }

		public Portal()
		{
			Handle = Element.Group(TypeIds.Portal);
		}

		public static implicit operator Element(Portal self) => self.Handle;
		public static Element? operator &(bool value, Portal self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(Portal self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Portal));
	}
}

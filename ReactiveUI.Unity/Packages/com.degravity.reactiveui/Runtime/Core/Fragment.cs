using System.Collections;

namespace ReactiveUI
{
	/// <summary>
	/// Groups children without adding a level to the visual tree.
	/// </summary>
	public readonly struct Fragment : IElement
	{
		public Element Handle { get; }

		public Fragment()
		{
			Handle = Element.Group(TypeIds.Fragment);
		}

		public static implicit operator Element(Fragment self) => self.Handle;
		public static Element? operator &(bool value, Fragment self) => value ? self.Handle : (Element?)null;
		public static Element? operator &(Fragment self, bool value) => value ? self.Handle : (Element?)null;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Fragment));
	}
}

using System;
using System.Collections;

namespace ReactiveUI
{
	/// <summary>
	/// One declared element. A handle into the pass's arena.
	/// </summary>
	public readonly struct Element : IElement, IEquatable<Element>
	{
		internal readonly int _node;

		internal Element(int node)
		{
			_node = node;
		}

		public Element Handle => this;

		public bool IsNone => _node == 0;

		/// <summary>
		/// The children declared on this element.
		/// </summary>
		public ElementList Children => new(_node);

		/// <summary>
		/// This element when <paramref name="value"/> holds, and nothing otherwise.
		/// </summary>
		/// <remarks>
		/// Declared on every element type rather than here alone, because an operator is only looked up
		/// on its own operand types — one reached through a component's implicit conversion to
		/// <see cref="Element"/> would not be found. This overload is what a helper returning a bare
		/// <see cref="Element"/> binds to.
		/// </remarks>
		public static Element? operator &(bool value, Element self) => value ? self : (Element?)null;

		/// <summary>
		/// This element when <paramref name="value"/> holds, and nothing otherwise.
		/// </summary>
		public static Element? operator &(Element self, bool value) => value ? self : (Element?)null;

		public bool Equals(Element other) => _node == other._node;

		public override bool Equals(object? obj) => obj is Element other && Equals(other);

		public override int GetHashCode() => _node;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Element));

		/// <summary>
		/// Declares a component with props.
		/// </summary>
		public static Element Of<TComponent, TProps>(TProps? props)
			where TComponent : struct, IComponent<TProps>
			where TProps : struct, IEquatable<TProps>
		{
			var slot = PropsPool<TProps>.Add(props ?? new TProps());

			return new Element(ElementPool.NewNode(ComponentType<TComponent, TProps>.s_id, slot));
		}

		/// <summary>
		/// Declares a component that takes no props.
		/// </summary>
		public static Element Of<TComponent>()
			where TComponent : struct, IComponent
		{
			return new Element(ElementPool.NewNode(ComponentType<TComponent>.s_id, -1));
		}

		internal static Element Host(HostKind kind, ClassSet className, ElementRef? elementRef, int propsSlot)
		{
			var node = ElementPool.NewNode(TypeIds.Host(kind), propsSlot);

			ElementPool.SetIdentity(node, className, elementRef);

			return new Element(node);
		}

		internal static Element Group(int typeId)
		{
			return new Element(ElementPool.NewNode(typeId, -1));
		}

		internal static Element Provider(object value)
		{
			var node = ElementPool.NewNode(TypeIds.Provider, -1);

			ElementPool.SetProvided(node, value);

			return new Element(node);
		}
	}

	/// <summary>
	/// Implemented by every element type.
	/// </summary>
	/// <remarks>
	/// <see cref="IEnumerable"/> is here only because C# requires it of a collection-initializer
	/// target; enumerating an element is a bug. Every <c>Add</c> lives on
	/// <see cref="ElementExtensions"/> instead of here, because a collection initializer accepts an
	/// extension <c>Add</c> and an element is a struct with no base class to inherit one from.
	/// </remarks>
	public interface IElement : IEnumerable
	{
		/// <summary>
		/// This element's identity in the arena.
		/// </summary>
		Element Handle { get; }
	}
}

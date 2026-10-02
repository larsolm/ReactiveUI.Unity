using System;
using System.Collections;
using System.ComponentModel;

namespace ReactiveUI
{
	/// <summary>
	/// A declared element of any type.
	/// </summary>
	public readonly struct Element : IElement, IEquatable<Element>
	{
		internal readonly int _node;

		internal Element(int node)
		{
			_node = node;
		}

		/// <inheritdoc/>
		public Element Handle => this;

		/// <summary>
		/// Whether this is the default, empty element.
		/// </summary>
		public bool IsNone => _node == 0;

		/// <summary>
		/// The children declared on this element.
		/// </summary>
		public ElementList Children => new(_node);

		/// <summary>
		/// Returns <paramref name="self"/> when <paramref name="value"/> is true, otherwise null.
		/// </summary>
		public static Element? operator &(bool value, Element self) => value ? self : (Element?)null;

		/// <summary>
		/// Returns <paramref name="self"/> when <paramref name="value"/> is true, otherwise null.
		/// </summary>
		public static Element? operator &(Element self, bool value) => value ? self : (Element?)null;

		public bool Equals(Element other) => _node == other._node;

		public override bool Equals(object? obj) => obj is Element other && Equals(other);

		public override int GetHashCode() => _node;

		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(Element));

		/// <summary>
		/// Declares a <typeparamref name="TComponent"/> with the given props. Used by generated code.
		/// </summary>
		[EditorBrowsable(EditorBrowsableState.Never)]
		public static Element Of<TComponent, TProps>(TProps? props)
			where TComponent : struct, IComponent<TProps>
			where TProps : struct, IEquatable<TProps>
		{
			var slot = PropsPool<TProps>.Add(props ?? new TProps());

			return new Element(ElementPool.NewNode(ComponentType<TComponent, TProps>.s_id, slot));
		}

		/// <summary>
		/// Declares a <typeparamref name="TComponent"/> that takes no props. Used by generated code.
		/// </summary>
		[EditorBrowsable(EditorBrowsableState.Never)]
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
	/// Implements <see cref="IEnumerable"/> only to support collection-initializer syntax for children;
	/// enumerating an element throws.
	/// </remarks>
	public interface IElement : IEnumerable
	{
		/// <summary>
		/// The underlying <see cref="Element"/>.
		/// </summary>
		Element Handle { get; }
	}
}

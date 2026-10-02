using System.Collections;
using System.Collections.Generic;

namespace ReactiveUI
{
	/// <summary>
	/// The children of an element.
	/// </summary>
	/// <remarks>
	/// Children are matched across renders by position, not by key.
	/// </remarks>
	public readonly struct ElementList : IEnumerable
	{
		private readonly int _owner;

		internal ElementList(int owner)
		{
			_owner = owner;
		}

		/// <summary>
		/// Creates an empty list that is not attached to a parent element.
		/// </summary>
		public ElementList()
		{
			_owner = ElementPool.NewNode(TypeIds.Detached, -1);
		}

		/// <summary>
		/// The number of children.
		/// </summary>
		public int Count => ElementPool.ChildCountOf(_owner);

		/// <summary>
		/// The child at <paramref name="index"/>.
		/// </summary>
		public Element this[int index] => new(ElementPool.ChildAt(_owner, index));

		/// <summary>
		/// Appends a child; null is ignored.
		/// </summary>
		public void Add(Element? child)
		{
			if (child is { IsNone: false } value)
				ElementPool.AddChild(_owner, value._node);
		}

		/// <summary>
		/// Appends every child in <paramref name="children"/>.
		/// </summary>
		public void Add(ElementList children)
		{
			var count = children.Count;

			ElementPool.EnsureCapacity(_owner, Count + count);

			for (var i = 0; i < count; i++)
				ElementPool.AddChild(_owner, children[i]._node);
		}

		/// <summary>
		/// Appends every child in <paramref name="children"/>; null is ignored.
		/// </summary>
		public void Add(IEnumerable<Element>? children)
		{
			if (children is null)
				return;

			foreach (var child in children)
				ElementPool.AddChild(_owner, child._node);
		}

		/// <summary>
		/// Appends every child produced by <paramref name="projection"/>.
		/// </summary>
		public void Add<TSource, TState>(Projection<TSource, TState> projection) => projection.AppendTo(this);

		/// <inheritdoc cref="Add{TSource, TState}(Projection{TSource, TState})"/>
		public void Add<TSource, TState>(IndexedProjection<TSource, TState> projection) => projection.AppendTo(this);

		/// <summary>
		/// Grows the run once, for a caller that already knows how many children follow.
		/// </summary>
		internal void EnsureCapacity(int capacity) => ElementPool.EnsureCapacity(_owner, capacity);

		/// Present only as a collection-initializer target. Enumerating a list is a bug.
		IEnumerator IEnumerable.GetEnumerator() => throw Ui.NotEnumerable(nameof(ElementList));
	}
}

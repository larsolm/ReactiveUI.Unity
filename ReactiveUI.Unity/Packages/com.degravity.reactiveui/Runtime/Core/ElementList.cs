using System.Collections;
using System.Collections.Generic;

namespace ReactiveUI
{
	/// <summary>
	/// The children of one element — a run inside the pass's arena, not a collection of its own.
	/// </summary>
	/// <remarks>
	/// A struct is safe here, unlike in the previous design: this holds only the owning node's index,
	/// so a copy handed back by a property getter appends to the same place the original would.
	/// <para>
	/// A child's identity across renders is its <em>position</em> in this list. There is no key, so a
	/// list that can change length in the middle must render fixed slots.
	/// </para>
	/// </remarks>
	public readonly struct ElementList : IEnumerable
	{
		private readonly int _owner;

		internal ElementList(int owner)
		{
			_owner = owner;
		}

		/// <summary>
		/// A list with no element above it, for a helper that returns children on their own.
		/// </summary>
		public ElementList()
		{
			_owner = ElementPool.NewNode(TypeIds.Detached, -1);
		}

		public int Count => ElementPool.ChildCountOf(_owner);

		public Element this[int index] => new(ElementPool.ChildAt(_owner, index));

		/// <summary>
		/// Appends a child. Nothing is appended for <c>null</c>, so <c>cond ? x : null</c> reads
		/// naturally.
		/// </summary>
		public void Add(Element? child)
		{
			if (child is { IsNone: false } value)
				ElementPool.AddChild(_owner, value._node);
		}

		/// <summary>
		/// Splices in another list — how a component forwards the children it was given.
		/// </summary>
		public void Add(ElementList children)
		{
			var count = children.Count;

			ElementPool.EnsureCapacity(_owner, Count + count);

			for (var i = 0; i < count; i++)
				ElementPool.AddChild(_owner, children[i]._node);
		}

		/// <summary>
		/// Splices in a sequence, for children built by a LINQ query or a loop.
		/// </summary>
		/// <remarks>
		/// The one remaining path that allocates — the iterator, and whatever the query closed over.
		/// Prefer <c>Each</c>.
		/// </remarks>
		public void Add(IEnumerable<Element>? children)
		{
			if (children is null)
				return;

			foreach (var child in children)
				ElementPool.AddChild(_owner, child._node);
		}

		/// <summary>
		/// Splices in a projection — the allocation-free form of <see cref="Add(IEnumerable{Element})"/>.
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

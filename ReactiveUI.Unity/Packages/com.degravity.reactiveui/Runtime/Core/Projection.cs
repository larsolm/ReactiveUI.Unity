using System;
using System.Collections.Generic;

namespace ReactiveUI
{
	/// <summary>
	/// A list of children described but not yet built — the closure-free form of
	/// <c>items.Select(item =&gt; new Child(...))</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A LINQ projection allocates three things per render: the iterator, the display class holding
	/// whatever the selector closed over, and the delegate. This carries the same information as a
	/// struct, so the only requirement is that the selector be written <c>static</c> and read what it
	/// needs from <c>state</c> — a non-capturing lambda is cached by the compiler, so nothing is
	/// allocated at all.
	/// </para>
	/// <para>
	/// It deliberately does <em>not</em> implement <see cref="IEnumerable{T}"/>. The
	/// <c>Add(IEnumerable&lt;Element&gt;)</c> overload is not generic and would win overload resolution
	/// against the one that takes this, which would quietly put back the iterator. Not being a
	/// sequence is what keeps that from compiling.
	/// </para>
	/// </remarks>
	public readonly struct Projection<TSource, TState>
	{
		private readonly IList<TSource>? _source;
		private readonly TState _state;
		private readonly Func<TSource, TState, Element?>? _select;

		internal Projection(IList<TSource>? source, TState state, Func<TSource, TState, Element?>? select)
		{
			_source = source;
			_state = state;
			_select = select;
		}

		internal void AppendTo(ElementList target)
		{
			if (_source is null || _select is null)
				return;

			var count = _source.Count;

			target.EnsureCapacity(target.Count + count);

			for (var index = 0; index < count; index++)
				target.Add(_select(_source[index], _state));
		}
	}

	/// <summary>
	/// <see cref="Projection{TSource, TState}"/> for a selector that also wants each item's index.
	/// </summary>
	public readonly struct IndexedProjection<TSource, TState>
	{
		private readonly IList<TSource>? _source;
		private readonly TState _state;
		private readonly Func<TSource, int, TState, Element?>? _select;

		internal IndexedProjection(IList<TSource>? source, TState state, Func<TSource, int, TState, Element?>? select)
		{
			_source = source;
			_state = state;
			_select = select;
		}

		internal void AppendTo(ElementList target)
		{
			if (_source is null || _select is null)
				return;

			var count = _source.Count;

			target.EnsureCapacity(target.Count + count);

			for (var index = 0; index < count; index++)
				target.Add(_select(_source[index], index, _state));
		}
	}
}

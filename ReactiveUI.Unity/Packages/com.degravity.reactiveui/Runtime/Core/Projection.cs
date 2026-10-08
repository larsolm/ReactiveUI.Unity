using System;
using System.Collections.Generic;

namespace ReactiveUI
{
	/// <summary>
	/// A list mapped to child elements, created by <see cref="Ui.Each{TSource, TState}(IList{TSource}, TState, Func{TSource, TState, Element?})"/>.
	/// </summary>
	public readonly struct Projection<TSource, TState>
	{
		private readonly IReadOnlyList<TSource> _source;
		private readonly TState _state;
		private readonly Func<TSource, TState, Element?> _select;

		internal Projection(IReadOnlyList<TSource> source, TState state, Func<TSource, TState, Element?> select)
		{
			_source = source;
			_state = state;
			_select = select;
		}

		internal void AppendTo(ElementList target)
		{
			if (_source.Count == 0)
				return;

			var count = _source.Count;

			target.EnsureCapacity(target.Count + count);

			for (var index = 0; index < count; index++)
				target.Add(_select(_source[index], _state));
		}
	}

	/// <summary>
	/// A list mapped to child elements with each item's index, created by <see cref="Ui.Each{TSource, TState}(IList{TSource}, TState, Func{TSource, int, TState, Element?})"/>.
	/// </summary>
	public readonly struct IndexedProjection<TSource, TState>
	{
		private readonly IReadOnlyList<TSource> _source;
		private readonly TState _state;
		private readonly Func<TSource, int, TState, Element?> _select;

		internal IndexedProjection(IReadOnlyList<TSource> source, TState state, Func<TSource, int, TState, Element?> select)
		{
			_source = source;
			_state = state;
			_select = select;
		}

		internal void AppendTo(ElementList target)
		{
			if (_source.Count == 0)
				return;

			var count = _source.Count;

			target.EnsureCapacity(target.Count + count);

			for (var index = 0; index < count; index++)
				target.Add(_select(_source[index], index, _state));
		}
	}
}

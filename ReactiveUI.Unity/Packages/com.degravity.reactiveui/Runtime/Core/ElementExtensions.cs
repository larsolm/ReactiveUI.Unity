namespace ReactiveUI
{
	/// <summary>
	/// Child-adding and conditional helpers for elements.
	/// </summary>
	public static class ElementExtensions
	{
		/// <summary>
		/// Returns the element when <paramref name="condition"/> is true, otherwise null.
		/// </summary>
		public static Element? When<TElement>(this TElement element, bool condition)
			where TElement : struct, IElement
		{
			return condition ? element.Handle : (Element?)null;
		}

		/// <inheritdoc cref="ElementList.Add(Element?)"/>
		public static void Add<TParent>(this TParent parent, Element? child)
			where TParent : struct, IElement
		{
			parent.Handle.Children.Add(child);
		}

		/// <inheritdoc cref="ElementList.Add(ElementList)"/>
		public static void Add<TParent>(this TParent parent, ElementList children)
			where TParent : struct, IElement
		{
			parent.Handle.Children.Add(children);
		}

		/// <inheritdoc cref="ElementList.Add{TSource, TState}(Projection{TSource, TState})"/>
		public static void Add<TParent, TSource, TState>(this TParent parent, Projection<TSource, TState> projection)
			where TParent : struct, IElement
		{
			projection.AppendTo(parent.Handle.Children);
		}

		/// <inheritdoc cref="ElementList.Add{TSource, TState}(Projection{TSource, TState})"/>
		public static void Add<TParent, TSource, TState>(
			this TParent parent,
			IndexedProjection<TSource, TState> projection)
			where TParent : struct, IElement
		{
			projection.AppendTo(parent.Handle.Children);
		}

		/// <inheritdoc cref="ElementList.Add(Element?)"/>
		public static void Add(this Element? parent, Element? child)
		{
			if (parent is { } value)
				value.Children.Add(child);
		}

		/// <inheritdoc cref="ElementList.Add(ElementList)"/>
		public static void Add(this Element? parent, ElementList children)
		{
			if (parent is { } value)
				value.Children.Add(children);
		}

		/// <inheritdoc cref="ElementList.Add{TSource, TState}(Projection{TSource, TState})"/>
		public static void Add<TSource, TState>(this Element? parent, Projection<TSource, TState> projection)
		{
			if (parent is { } value)
				projection.AppendTo(value.Children);
		}

		/// <inheritdoc cref="ElementList.Add{TSource, TState}(Projection{TSource, TState})"/>
		public static void Add<TSource, TState>(this Element? parent, IndexedProjection<TSource, TState> projection)
		{
			if (parent is { } value)
				projection.AppendTo(value.Children);
		}
	}
}

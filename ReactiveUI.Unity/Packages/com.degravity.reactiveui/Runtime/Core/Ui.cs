using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>
	/// The render surface a component reaches for: the hooks, the children it was given, and
	/// <see cref="Each{TSource, TState}(IList{TSource}, TState, Func{TSource, TState, Element?})"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Statics resolved against the render currently running, rather than methods inherited from a
	/// base class: a component is a struct and so has no base class to inherit anything from. Write
	/// <c>using static ReactiveUI.Ui;</c> at the top of a component file and the call sites read
	/// exactly as instance methods did.
	/// </para>
	/// <para>
	/// The ambient store is set by the reconciler around a single <c>Render</c> and cleared after it,
	/// so a hook called anywhere else throws rather than reading someone else's state. That also
	/// makes a hook valid inside a helper method the render calls, which a component-scoped store
	/// only ever supported by accident.
	/// </para>
	/// </remarks>
	[AutoStaticsCleanup]
	public static partial class Ui
	{
		private static HookStore? s_hooks = null;
		private static int s_node;

		/// <summary>
		/// The children this component was given by its caller.
		/// </summary>
		public static ElementList Children => new(s_node);

		internal static void BeginRender(HookStore hooks, int node)
		{
			s_hooks = hooks;
			s_node = node;

			hooks.BeginRender();
		}

		internal static void EndRender()
		{
			s_hooks!.EndRender();
			s_hooks = null;
			s_node = 0;
		}

		/// <summary>
		/// Abandons the ambient render, for when one threw partway through.
		/// </summary>
		internal static void AbandonRender()
		{
			s_hooks = null;
			s_node = 0;
		}

		/// <summary>
		/// Projects a list into children without allocating — the <c>items.Select(…)</c> shape.
		/// A null returned by <paramref name="select"/> is skipped.
		/// </summary>
		/// <remarks>
		/// Write <paramref name="select"/> as a <c>static</c> lambda and reach everything it needs
		/// through <paramref name="state"/>; capturing a local instead restores the per-render
		/// closure this avoids.
		/// <code>
		/// Each(nodes, (current, onSelect), static (node, state) => new MapNode(new(
		///     Node: node,
		///     Current: state.current,
		///     OnSelect: state.onSelect)))
		/// </code>
		/// </remarks>
		public static Projection<TSource, TState> Each<TSource, TState>(
			IList<TSource>? source,
			TState state,
			Func<TSource, TState, Element?> select)
		{
			return new Projection<TSource, TState>(source, state, select);
		}

		/// <summary>
		/// <see cref="Each{TSource, TState}(IList{TSource}, TState, Func{TSource, TState, Element?})"/>
		/// with each item's index.
		/// </summary>
		public static IndexedProjection<TSource, TState> Each<TSource, TState>(
			IList<TSource>? source,
			TState state,
			Func<TSource, int, TState, Element?> select)
		{
			return new IndexedProjection<TSource, TState>(source, state, select);
		}

		/// <summary>
		/// The exception every element type throws from its unused enumerator.
		/// </summary>
		public static Exception NotEnumerable(string name)
		{
			return new NotSupportedException(
				$"{name} is not enumerable. It implements IEnumerable only so that "
				+ "collection-initializer syntax can add children.");
		}

		private static string CurrentName() => TypeIds.NameOf(ElementPool.TypeIdOf(s_node));

		/// <summary>
		/// The hooks of the component rendering right now. Internal rather than private so the optional
		/// integration assemblies can take a hook slot the same way the built-in hooks do.
		/// </summary>
		internal static HookStore Current()
		{
			return s_hooks ?? throw new InvalidOperationException(
				"A hook was called outside a component's Render(). Hooks read state that belongs to a "
				+ "mounted component, so they are only valid during that component's render.");
		}
	}
}

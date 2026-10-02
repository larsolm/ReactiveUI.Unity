using System;
using System.Collections.Generic;
using System.ComponentModel;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>
	/// Hooks and render helpers for the component currently rendering.
	/// </summary>
	/// <remarks>
	/// Intended for use with <c>using static ReactiveUI.Ui;</c>. Hooks throw when called outside a
	/// component's <c>Render</c>.
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
		/// Maps each item in <paramref name="source"/> to a child element, skipping nulls.
		/// </summary>
		public static Projection<TSource, TState> Each<TSource, TState>(
			IList<TSource>? source,
			TState state,
			Func<TSource, TState, Element?> select)
		{
			return new Projection<TSource, TState>(source, state, select);
		}

		/// <summary>
		/// Maps each item in <paramref name="source"/> and its index to a child element, skipping nulls.
		/// </summary>
		public static IndexedProjection<TSource, TState> Each<TSource, TState>(
			IList<TSource>? source,
			TState state,
			Func<TSource, int, TState, Element?> select)
		{
			return new IndexedProjection<TSource, TState>(source, state, select);
		}

		/// <summary>
		/// Creates the exception an element throws when enumerated. Used by generated code.
		/// </summary>
		[EditorBrowsable(EditorBrowsableState.Never)]
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

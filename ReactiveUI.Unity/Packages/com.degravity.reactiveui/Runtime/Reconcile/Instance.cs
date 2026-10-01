using System;
using System.Collections.Generic;

namespace ReactiveUI
{
	internal abstract class Instance
	{
		internal Instance? _parent;
		internal List<Instance>? _children;

		/// <summary>Which element type produced this instance. See <see cref="TypeIds"/>.</summary>
		internal int _typeId;

		internal int _depth;
		internal bool _unmounted;
		internal bool _isPortal;
		internal bool _matched;

		/// <summary>The nearest <see cref="HostInstance"/> strictly above this one.</summary>
		/// <remarks>
		/// Resolved once on attach rather than walked on demand. The walk ran twice per host per
		/// reconcile — for the parent variable scope and the parent inherited style — and the answer
		/// cannot change while the instance lives: a child's identity is its position, so one that
		/// moves is unmounted and remounted rather than re-parented.
		/// </remarks>
		internal HostInstance? _nearestHost;

		internal string Name => TypeIds.NameOf(_typeId);

		/// <summary>
		/// Puts this instance in its place in the tree.
		/// </summary>
		/// <remarks>
		/// One method rather than four assignments at each mount site, so that
		/// <see cref="_nearestHost"/> cannot be left stale by setting a parent without it.
		/// </remarks>
		internal void Attach(Instance parent, int typeId, int depth)
		{
			_parent = parent;
			_typeId = typeId;
			_depth = depth;
			_nearestHost = parent as HostInstance ?? parent._nearestHost;
		}

		/// <summary>
		/// Whether this instance can be updated to become the element at <paramref name="node"/>,
		/// rather than unmounted and replaced.
		/// </summary>
		/// <remarks>
		/// The type is the whole test. A child's identity is its position among its siblings, so the
		/// only question left is whether the thing standing there is still the same kind of thing.
		/// </remarks>
		internal bool IsCompatibleWith(int node)
		{
			return _typeId == ElementPool.TypeIdOf(node);
		}

		internal virtual void ResetForPool()
		{
			_parent = null;
			_nearestHost = null;
			_children?.Clear();
			_typeId = 0;
			_depth = 0;
			_unmounted = false;
			_matched = false;
			_isPortal = false;
		}
	}

	/// <summary>A <see cref="Fragment"/> or <see cref="Portal"/> — grouping, with no box of its own.</summary>
	internal sealed class GroupInstance : Instance
	{
	}

	/// <summary>
	/// A <see cref="ContextProvider{T}"/>: grouping that also carries a value, plus the consumers
	/// currently reading it.
	/// </summary>
	internal sealed class ProviderInstance : Instance
	{
		internal object? Value;

		/// <summary>
		/// Lazily created, because a provider with no consumer beneath it is common and the list
		/// would otherwise be one allocation per provider per mount.
		/// </summary>
		private List<RenderInstance>? _consumers;

		internal void AddConsumer(RenderInstance consumer)
		{
			_consumers ??= new List<RenderInstance>(2);

			if (!_consumers.Contains(consumer))
				_consumers.Add(consumer);
		}

		internal void RemoveConsumer(RenderInstance consumer) => _consumers?.Remove(consumer);

		/// <summary>
		/// Schedules every consumer of this provider, for when the provided value changed.
		/// </summary>
		/// <remarks>
		/// A consumer that this pass re-renders anyway drops straight back out again — rendering a
		/// component calls <c>Scheduler.Forget</c> — so this costs an extra render only for the
		/// consumers that were genuinely cut off by a skipped ancestor.
		/// </remarks>
		internal void MarkConsumersDirty(Scheduler scheduler)
		{
			if (_consumers is null)
				return;

			for (var i = 0; i < _consumers.Count; i++)
				scheduler.MarkDirty(_consumers[i]);
		}

		internal override void ResetForPool()
		{
			base.ResetForPool();
			Value = null;
			_consumers?.Clear();
		}
	}

	/// <summary>
	/// A mounted component: its hooks, its committed props, and how to render it again.
	/// </summary>
	/// <remarks>
	/// The props live here rather than on a retained element, which is what lets the element arena be
	/// dropped wholesale at the end of every pass. A re-render reads them straight back out.
	/// </remarks>
	internal abstract class RenderInstance : Instance
	{
		internal HookStore? _hooks;
		internal bool _dirty;

		/// <summary>Whether this component has rendered at least once.</summary>
		internal bool _rendered;

		/// <summary>
		/// Whether this component was given children.
		/// </summary>
		/// <remarks>
		/// A component that was given children cannot be re-rendered on its own account, because the
		/// children belong to the caller's element and that is gone once the pass ends. See
		/// <c>Reconciler.RerenderInPlace</c>, which escalates to the caller instead.
		/// </remarks>
		internal bool _tookChildren;

		/// <summary>
		/// The style generation this component last rendered against.
		/// </summary>
		/// <remarks>
		/// Reloading a stylesheet re-issues every interned rule-set and variable-scope id from zero,
		/// so an id a host cached in the previous generation now names a different rule set entirely.
		/// The hosts are brought back up to date by re-rendering the tree — which memoisation would
		/// otherwise cut short at any component whose props happened not to change, stranding its
		/// whole subtree on ids that no longer mean anything.
		/// </remarks>
		internal int _styleGeneration = -1;

		/// <summary>
		/// Copies the props declared at <paramref name="node"/> onto this instance.
		/// </summary>
		/// <returns>False when they match what is already committed, so the render can be skipped.</returns>
		internal abstract bool CommitProps(int node);

		internal abstract Element Invoke();

		internal override void ResetForPool()
		{
			base.ResetForPool();
			_hooks = null;
			_dirty = false;
			_rendered = false;
			_tookChildren = false;
			_styleGeneration = -1;
		}
	}

	/// <inheritdoc cref="RenderInstance"/>
	internal sealed class RenderInstance<TComponent, TProps> : RenderInstance
		where TComponent : struct, IComponent<TProps>
		where TProps : struct, IEquatable<TProps>
	{
		private TProps _committed;
		private bool _hasProps;

		internal override bool CommitProps(int node)
		{
			ref var declared = ref PropsPool<TProps>.At(ElementPool.PropsSlotOf(node));

			if (_hasProps && _committed.Equals(declared))
				return false;

			_committed = declared;
			_hasProps = true;

			return true;
		}

		/// <remarks>
		/// A constrained call on a struct type parameter, so this is dispatched directly and nothing
		/// is boxed — which is the whole reason a component is a struct rather than an interface
		/// reference.
		/// </remarks>
		internal override Element Invoke()
		{
			var component = default(TComponent);

			return component.Render(in _committed);
		}

		internal override void ResetForPool()
		{
			base.ResetForPool();
			_committed = default;
			_hasProps = false;
		}
	}

	/// <inheritdoc cref="RenderInstance"/>
	internal sealed class ProplessRenderInstance<TComponent> : RenderInstance
		where TComponent : struct, IComponent
	{
		/// <remarks>There are no props, so nothing can ever have changed.</remarks>
		internal override bool CommitProps(int node) => false;

		internal override Element Invoke()
		{
			var component = default(TComponent);

			return component.Render();
		}
	}
}

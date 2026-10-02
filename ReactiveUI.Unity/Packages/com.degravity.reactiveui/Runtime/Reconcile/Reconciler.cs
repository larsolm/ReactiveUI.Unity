using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	internal sealed class Reconciler : IStyleUpdater
	{
		internal StyleContext Context { get; set; }

		private readonly HostFactory _factory;
		private readonly Scheduler _scheduler;
		private readonly StyleEngine _engine;
		private readonly FocusManager _focus;
		private readonly HotkeyRegistry _hotkeys;
		private readonly InputActionRegistry _actions;
		private readonly MediaWatch _media;
		private readonly List<HostInstance> _hostScratch = new();
		private readonly List<HostInstance> _overlayScratch = new();
		private readonly List<HostInstance> _entering = new();
		private readonly List<HostInstance> _exiting = new();

		/// <summary>
		/// Scratch for <see cref="Overlay"/>. Grown, never shrunk, and safe to share because the
		/// method neither recurses nor keeps the list past the call that fills it.
		/// </summary>
		private readonly List<PropEntry> _overlayEntries = new();

		/// <summary>
		/// How many portals are mounted right now.
		/// </summary>
		/// <remarks>
		/// Kept so <see cref="SyncOverlay"/> — which runs every frame — can answer "is anything
		/// portalled" without walking the instance tree to find out. Most frames of most screens have
		/// no modal open, and that walk was the whole tree for a guaranteed-empty result.
		/// </remarks>
		private int _portalCount;

		internal Reconciler(
			HostFactory factory,
			Scheduler scheduler,
			StyleEngine engine,
			FocusManager focus,
			HotkeyRegistry hotkeys,
			InputActionRegistry actions,
			MediaWatch media,
			StyleContext ctx)
		{
			_factory = factory;
			_scheduler = scheduler;
			_engine = engine;
			_focus = focus;
			_hotkeys = hotkeys;
			_actions = actions;
			_media = media;
			Context = ctx;
		}

		internal Instance? ReconcileRoot(HostInstance parent, Element element)
		{
			ResolveRootStyle(parent);

			var existing = parent._children is { Count: > 0 } ? parent._children[0] : null;
			var result = Reconcile(parent, existing, element._node, depth: 1);

			parent._children ??= new List<Instance>(1);
			parent._children.Clear();

			if (result is not null)
				parent._children.Add(result);

			SyncHostChildren(parent);

			return result;
		}

		/// <summary>
		/// Re-renders one component in place, for a hook that marked it dirty.
		/// </summary>
		/// <remarks>
		/// A component that was given children cannot render on its own account: those children belong
		/// to the caller's element, and an element does not outlive the pass that declared it. The
		/// nearest enclosing component is asked to re-render instead, which rebuilds them on the way
		/// past; this one stays dirty, so it renders when that rebuild reaches it.
		/// </remarks>
		internal void RerenderInPlace(RenderInstance instance)
		{
			if (instance._unmounted || !instance._rendered)
				return;

			if (instance._tookChildren && NearestComponentAncestor(instance) is { } caller)
			{
				_scheduler.MarkDirty(caller);

				return;
			}

			instance._dirty = false;
			RenderComponent(instance, node: 0);

			// Only the nearest enclosing host needs its child order refreshed; a component's
			// re-render cannot change anything above it.
			var hostParent = instance._nearestHost;

			if (hostParent is not null)
				SyncHostChildren(hostParent);
		}

		private Instance? Reconcile(Instance parent, Instance? existing, int node, int depth)
		{
			if (node == 0)
			{
				if (existing is not null)
					Unmount(existing);

				return null;
			}

			if (existing is not null && existing.IsCompatibleWith(node))
			{
				Update(existing, node, depth);

				return existing;
			}

			if (existing is not null)
				Unmount(existing);

			return Mount(parent, node, depth);
		}

		private Instance Mount(Instance parent, int node, int depth)
		{
			var typeId = ElementPool.TypeIdOf(node);

			if (TypeIds.IsHost(typeId))
				return MountHost(parent, node, typeId, depth);

			// Fragments, portals and providers render nothing; they exist to group children. A portal
			// keeps its place in the tree so the cascade and lifecycle still see it there, and only its
			// GameObjects are re-parented into the overlay.
			//
			// None owns a box, so nothing stops at one: a host below a fragment is placed by whichever
			// host is above it.
			if (TypeIds.IsGroup(typeId))
			{
				var group = MountGroup(parent, node, typeId, depth);

				ReconcileChildren(group, node, depth + 1);

				return group;
			}

			var component = ComponentTypes.Of(typeId).CreateInstance();

			component.Attach(parent, typeId, depth);
			component._hooks = new HookStore(component, _scheduler, _hotkeys, _actions, _focus, _media);

			RenderComponent(component, node);

			return component;
		}

		private Instance MountGroup(Instance parent, int node, int typeId, int depth)
		{
			if (typeId == TypeIds.Provider)
			{
				var provider = new ProviderInstance { Value = ElementPool.ProvidedOf(node) };

				provider.Attach(parent, typeId, depth);

				return provider;
			}

			var group = new GroupInstance { _isPortal = typeId == TypeIds.Portal };

			group.Attach(parent, typeId, depth);

			if (group._isPortal)
				_portalCount++;

			return group;
		}

		private HostInstance MountHost(Instance parent, int node, int typeId, int depth)
		{
			var instance = _factory.Rent(TypeIds.KindOf(typeId));

			instance.Attach(parent, typeId, depth);

			// The node is born in its :enter state and snaps there, because there is no previous value
			// to animate from. The bit is cleared on the next tick, and *that* transition is what plays.
			instance._state |= UiStates.s_enter.Mask;
			_engine.NoteStateChanged();

			instance.ApplyProps(node);
			ApplyIdentityAndStyle(instance, node);

			// Anything that responds to a press is somewhere navigation can land.
			if (instance is PressableHost)
			{
				instance._focus = _focus;
				_focus.Register(instance);
			}

			if (_engine.UsesState(instance._ruleSetId, UiStates.s_enter))
				_entering.Add(instance);
			else
				instance._state &= ~UiStates.s_enter.Mask;

			ReconcileChildren(instance, node, depth + 1);
			SyncHostChildren(instance);

			return instance;
		}

		private void Update(Instance instance, int node, int depth)
		{
			instance._depth = depth;

			switch (instance)
			{
				case HostInstance host:
					CancelExit(host);
					host.ApplyProps(node);
					ApplyIdentityAndStyle(host, node);
					ReconcileChildren(host, node, depth + 1);
					SyncHostChildren(host);

					break;

				case ProviderInstance provider:
					// A changed value has to reach consumers sitting under something that memoised,
					// which by definition re-renders nothing of its own.
					var provided = ElementPool.ProvidedOf(node);

					if (!ReferenceEquals(provider.Value, provided))
					{
						provider.Value = provided;
						provider.MarkConsumersDirty(_scheduler);
					}

					ReconcileChildren(provider, node, depth + 1);

					break;

				case GroupInstance group:
					ReconcileChildren(group, node, depth + 1);

					break;

				case RenderInstance component:
					// Every component memoises on its props, so this path is the common one rather
					// than the rare one.
					//
					// Matching props are not enough on their own, twice over. A component that was
					// *given* children has to re-render, because whether those changed is a question
					// about the caller's element rather than about this component's props — the old
					// design answered it by retaining the previous element tree, which is exactly what
					// stopped the arena being reclaimable. And a stylesheet reload invalidates every id
					// the hosts below this component are holding, so skipping the render is what would
					// leave them holding one.
					var propsChanged = component.CommitProps(node);
					var tookChildren = ElementPool.ChildCountOf(node) > 0;

					if (!propsChanged
						&& !tookChildren
						&& !component._tookChildren
						&& component._rendered
						&& !component._dirty
						&& component._styleGeneration == _engine.Generation)
					{
						return;
					}

					RenderComponent(component, node);

					break;
			}
		}

		/// <summary>
		/// Renders one component and reconciles what it produced.
		/// </summary>
		/// <param name="node">
		/// The element that declared it, or 0 when a hook drove the re-render rather than the caller —
		/// in which case there are no declared children and the committed props stand.
		/// </param>
		private void RenderComponent(RenderInstance instance, int node)
		{
			// Everything that comes from the declaration is read here, and only when there is one.
			// A hook-driven re-render passes 0: the committed props already stand, and clearing
			// _tookChildren would lose the flag that sends the next one to the caller instead.
			//
			// Update also calls CommitProps, to decide whether the render can be skipped at all. A
			// mount has no such decision to make, and leaving the copy to the caller meant every
			// component mounted against default props — blank text and zeroed layout, with nothing
			// to say so. The second call is idempotent.
			if (node != 0)
			{
				instance.CommitProps(node);
				instance._tookChildren = ElementPool.ChildCountOf(node) > 0;
			}

			instance._dirty = false;
			instance._rendered = true;
			instance._styleGeneration = _engine.Generation;
			_scheduler.Forget(instance);

			Element rendered;

			Ui.BeginRender(instance._hooks!, node);

			try
			{
				rendered = instance.Invoke();
				Ui.EndRender();
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				Ui.AbandonRender();
				rendered = default;
			}

			var existing = instance._children is { Count: > 0 } ? instance._children[0] : null;
			var child = Reconcile(instance, existing, rendered._node, instance._depth + 1);

			instance._children ??= new List<Instance>(1);
			instance._children.Clear();

			if (child is not null)
				instance._children.Add(child);

		}

		private void ReconcileChildren(Instance instance, int node, int depth)
		{
			var previous = instance._children;
			var count = ElementPool.ChildCountOf(node);

			if (count == 0)
			{
				if (previous is not null)
				{
					for (var i = 0; i < previous.Count; i++)
						Unmount(previous[i]);

					previous.Clear();
				}

				return;
			}

			instance._children ??= new List<Instance>(count);

			// Both buffers come from a stack rather than being allocated fresh. Reconciling is
			// recursive, so a single shared scratch buffer would be overwritten by the children's own
			// pass — hence a pool rather than a field.
			var matched = RentMatchBuffer(count);
			var next = RentInstanceList();

			MatchPrevious(previous, node, count, matched);

			for (var i = 0; i < count; i++)
			{
				var reconciled = Reconcile(instance, matched[i], ElementPool.ChildAt(node, i), depth);

				if (reconciled is not null)
					next.Add(reconciled);
			}

			// Anything the pass never claimed is genuinely gone.
			if (previous is not null)
			{
				for (var i = 0; i < previous.Count; i++)
				{
					var stale = previous[i];

					if (!stale._matched && !stale._unmounted)
						Unmount(stale);
				}
			}

			instance._children.Clear();
			instance._children.AddRange(next);

			for (var i = 0; i < next.Count; i++)
				next[i]._matched = false;

			ReturnMatchBuffer(matched);
			ReturnInstanceList(next);
		}

		/// <summary>
		/// Pairs each new child with the instance that stood in its position last render.
		/// </summary>
		/// <remarks>
		/// Position is the whole of a child's identity — there is no key to say otherwise — so this is
		/// a walk down both lists in step. The type check is what keeps it honest: a slot whose element
		/// changed type is not a match, so it unmounts and remounts rather than being re-propped into
		/// something it is not.
		/// <para>
		/// The consequence a caller has to know is that a list which can change length in the middle
		/// shifts every child after the change onto the previous neighbour's instance. Render fixed
		/// slots when that matters.
		/// </para>
		/// </remarks>
		private static void MatchPrevious(List<Instance>? previous, int node, int count, Instance?[] result)
		{
			if (previous is null || previous.Count == 0)
				return;

			var limit = Math.Min(count, previous.Count);

			for (var i = 0; i < previous.Count; i++)
				previous[i]._matched = false;

			for (var i = 0; i < limit; i++)
			{
				var candidate = previous[i];

				if (!candidate.IsCompatibleWith(ElementPool.ChildAt(node, i)))
					continue;

				candidate._matched = true;
				result[i] = candidate;
			}
		}

		#region Scratch pools

		private readonly Stack<Instance?[]> _matchBuffers = new();
		private readonly Stack<List<Instance>> _instanceLists = new();

		private Instance?[] RentMatchBuffer(int count)
		{
			if (_matchBuffers.Count > 0)
			{
				// Only the top buffer is considered. Popping past an undersized one used to discard
				// it, so a single long list permanently drained the pool of everything smaller.
				if (_matchBuffers.Peek().Length >= count)
				{
					var buffer = _matchBuffers.Pop();

					Array.Clear(buffer, 0, count);

					return buffer;
				}
			}

			// Rounded up so a list that grows by one does not discard the buffer each time.
			return new Instance?[Mathf.NextPowerOfTwo(Mathf.Max(count, 4))];
		}

		private void ReturnMatchBuffer(Instance?[] buffer)
		{
			Array.Clear(buffer, 0, buffer.Length);
			_matchBuffers.Push(buffer);
		}

		private List<Instance> RentInstanceList()
		{
			return _instanceLists.Count > 0 ? _instanceLists.Pop() : new List<Instance>(8);
		}

		private void ReturnInstanceList(List<Instance> list)
		{
			list.Clear();
			_instanceLists.Push(list);
		}

		#endregion

		private static RenderInstance? NearestComponentAncestor(Instance instance)
		{
			for (var current = instance._parent; current is not null; current = current._parent)
			{
				if (current is RenderInstance component)
					return component;
			}

			return null;
		}

		/// <param name="canExit">
		/// Whether this node may hold itself back to play an exit. Only the outermost host of a removed
		/// subtree does: everything beneath it leaves when it does, even if its own rules name
		/// <c>:exit</c> through an ancestor, as <c>.panel:exit > .body</c> does.
		/// </param>
		private void Unmount(Instance instance, bool canExit = true)
		{
			if (instance._unmounted)
				return;

			// Decided before the subtree is touched. An exiting node keeps its children — a box
			// that fades out while its own text has already vanished is worse than no animation.
			if (canExit && instance is HostInstance candidate && TryBeginExit(candidate))
				return;

			instance._unmounted = true;

			if (instance._isPortal)
				_portalCount--;

			if (instance._children is not null)
			{
				for (var i = 0; i < instance._children.Count; i++)
					Unmount(instance._children[i], canExit && instance is not HostInstance);

				instance._children.Clear();
			}

			switch (instance)
			{
				case RenderInstance component:
					// Children tear down first, so a parent's cleanup still sees a live subtree.
					component._hooks?.Dispose();
					_scheduler.Forget(component);

					break;

				case HostInstance host:
					host._yoga.Parent?.RemoveChild(host._yoga);
					_factory.Release(host);

					break;
			}
		}

		private void SyncHostChildren(HostInstance host)
		{
			_hostScratch.Clear();
			CollectHosts(host._children, _hostScratch);
			SyncHostList(host, _hostScratch);
		}

		/// <summary>
		/// Whether <paramref name="parent"/> is already placed against exactly these children.
		/// </summary>
		/// <remarks>
		/// See <see cref="HostInstance._syncedHosts"/> for why the whole list is compared rather than
		/// a per-child index. Everything the walk below does — the sibling links, the transform order,
		/// the Yoga child list, the trailing trim — is a function of this list alone <em>and</em> of
		/// each child still being parented where it was put, which a trip through the host pool undoes
		/// without changing the list at all. See <see cref="HostInstance._placedUnder"/>.
		/// </remarks>
		private static bool AlreadyPlaced(HostInstance parent, List<HostInstance> children)
		{
			var synced = parent._syncedHosts;

			if (synced is null || synced.Count != children.Count)
				return false;

			var content = parent.ContentRect;

			for (var i = 0; i < synced.Count; i++)
			{
				var child = children[i];

				if (!ReferenceEquals(synced[i], child) || !ReferenceEquals(child._placedUnder, content))
					return false;
			}

			return true;
		}

		private static void RememberPlacement(HostInstance parent, List<HostInstance> children)
		{
			parent._syncedHosts ??= new List<HostInstance>(children.Count);
			parent._syncedHosts.Clear();
			parent._syncedHosts.AddRange(children);
		}

		private void SyncHostList(HostInstance parent, List<HostInstance> children)
		{
			if (AlreadyPlaced(parent, children))
				return;

			var content = parent.ContentRect;

			for (var i = 0; i < children.Count; i++)
			{
				var child = children[i];

				// Recorded here because this is the one place the ordered host siblings are known.
				// Matching has already run by now, so a changed link means any sibling selector on
				// this node was evaluated against the wrong neighbour and has to be redone.
				var previous = i > 0 ? children[i - 1] : null;

				if (!ReferenceEquals(child._previousHostSibling, previous))
				{
					child._previousHostSibling = previous;

					if (_engine.UsesSiblingCombinators)
					{
						child._ruleSetId = _engine.Match(child);
						child._dependsOnState = _engine.DependsOnState(child._ruleSetId);

						// `.a:hover + .b` reads the neighbour's state, so a new neighbour can change
						// this node's mask with no state anywhere having moved.
						child._conditionGeneration = -1;

						Restyle(child);
					}
				}

				if (child._rectTransform.parent != content)
				{
					child._rectTransform.SetParent(content, worldPositionStays: false);
				}

				child._placedUnder = content;

				if (child._rectTransform.GetSiblingIndex() != i)
					child._rectTransform.SetSiblingIndex(i);

				if (i < parent._yoga.Count && ReferenceEquals(parent._yoga[i], child._yoga))
					continue;

				child._yoga.Parent?.RemoveChild(child._yoga);
				parent._yoga.Insert(Mathf.Min(i, parent._yoga.Count), child._yoga);
			}

			// Trim any Yoga children left over from a shrunk list.
			while (parent._yoga.Count > children.Count)
				parent._yoga.RemoveAt(parent._yoga.Count - 1);

			RememberPlacement(parent, children);
		}

		private static void CollectHosts(List<Instance>? instances, List<HostInstance> into)
		{
			if (instances is null) return;

			for (var i = 0; i < instances.Count; i++)
			{
				// A portal's contents belong to the overlay, so they are not this parent's to place.
				if (instances[i]._isPortal) continue;

				switch (instances[i])
				{
					case HostInstance host:
						into.Add(host);

						break;

					default:
						CollectHosts(instances[i]._children, into);

						break;
				}
			}
		}

		internal void SyncOverlay(HostInstance root, HostInstance overlay)
		{
			// Nothing is portalled and nothing was, so there is no tree worth walking. Checked before
			// the walk rather than after it, which is the whole point — this runs every frame.
			if (_portalCount == 0 && overlay._yoga.Count == 0)
				return;

			_overlayScratch.Clear();
			CollectPortalContents(root, _overlayScratch);

			if (_overlayScratch.Count == 0 && overlay._yoga.Count == 0)
				return;

			SyncHostList(overlay, _overlayScratch);

			// Mirrored into the child list so the layout pass, which walks instances rather than
			// transforms, reaches portalled content too.
			overlay._children ??= new List<Instance>(_overlayScratch.Count);
			overlay._children.Clear();

			for (var i = 0; i < _overlayScratch.Count; i++)
				overlay._children.Add(_overlayScratch[i]);
		}

		private static void CollectPortalContents(Instance? root, List<HostInstance> into)
		{
			if (root?._children is null)
				return;

			for (var i = 0; i < root._children.Count; i++)
			{
				var child = root._children[i];

				if (child._isPortal)
					CollectHosts(child._children, into);

				CollectPortalContents(child, into);
			}
		}

		private void ResolveRootStyle(HostInstance root)
		{
			if (root._matchDirty)
			{
				root._ruleSetId = _engine.Match(root);
				root._matchDirty = false;
			}

			root._conditionMask = _engine.EvaluateConditions(root._ruleSetId, root);
			root._varSetId = _engine.ResolveVarScope(root._ruleSetId, 0, -1, root._conditionMask);

			var computed = _engine.Resolve(root._ruleSetId, root._conditionMask, 0, root._varSetId);
			root._inheritedId = _engine.ResolveInherited(computed, 0);
			root._style = computed;
		}

		private void ApplyIdentityAndStyle(HostInstance host, int node)
		{
			var previousVarSet = host._varSetId;
			var previousInherited = host._inheritedId;

			// A node's identity is its own and nothing else's: the class the caller wrote on this very
			// element, plus the one type name it answers to. Nothing is inherited from the components
			// above it, because a class names a CSS rule and a rule can only apply to a node in the
			// layout.
			var identityChanged = host.SetMatchIdentity(
				ElementPool.ClassOf(node), TypeIds.InternedName(ElementPool.TypeIdOf(node)));

			if (identityChanged)
			{
				// Only on a real change: Unity's GameObject.name getter marshals a fresh string on
				// every read, so naming unconditionally would allocate once per host per reconcile.
				host.RefreshName();
			}

			host._updater = this;

			// Rebound every render, because the element carrying the ref is rebuilt each time.
			var elementRef = ElementPool.RefOf(node);

			if (!ReferenceEquals(host._ref, elementRef))
			{
				host._ref?.Unbind(host);
				host._ref = elementRef;
				host._ref?.Bind(host);
			}

			// Inline properties are copied here so that a later restyle — driven by a pointer
			// rather than a render — has everything it needs without the declaration, which by
			// then is long gone.
			CaptureInline(host, node);

			Rematch(host);

			var parentVarSet = host._nearestHost?._varSetId ?? 0;
			var inlineSlot = ElementPool.InlineSlotOf(node);
			var hasVars = inlineSlot >= 0 && InlineArena.At(inlineSlot).VarCount > 0;

			// Resolving a scope allocates a dictionary and hashes it whenever the node's rules declare
			// any custom property, almost always to intern straight back to the id it already had. The
			// answer is a function of two ints, so the node can remember it — except when the element
			// brings inline variables, which are rebuilt every render and so are not in that key.
			if (hasVars)
			{
				host._varSetId = _engine.ResolveVarScope(host._ruleSetId, parentVarSet, inlineSlot, ConditionMask(host));
				host._varScopeRuleSetId = InlineVarScope;
			}
			else
			{
				ResolveCachedVarScope(host, parentVarSet);
			}

			Restyle(host);

			if (host._children is { Count: > 0 }
				&& (identityChanged || host._varSetId != previousVarSet || host._inheritedId != previousInherited))
			{
				RefreshDescendants(host, identityChanged);
			}
		}

		private void Rematch(HostInstance host)
		{
			if (!host._matchDirty)
				return;

			host._ruleSetId = _engine.Match(host);
			host._dependsOnState = _engine.DependsOnState(host._ruleSetId);
			host._matchDirty = false;

			// A node styled on hover must be hit-testable, even if it is a plain View that
			// would otherwise never take a raycast. Missing this is the difference between
			// ":hover doesn't work" and a working stylesheet.
			if (_engine.NeedsPointer(host))
				host.EnablePointer();
		}

		/// <summary>Marks a scope built from inline variables, which only the node's own render can rebuild.</summary>
		private const int InlineVarScope = -2;

		/// <remarks>
		/// A node whose element brought inline variables is left alone: those variables live in the
		/// element arena, which is gone outside the render that built them, so its scope can only be
		/// rebuilt by that node's own next render.
		/// </remarks>
		private void ResolveCachedVarScope(HostInstance host, int parentVarSet)
		{
			if (host._varScopeRuleSetId == InlineVarScope)
				return;

			var mask = ConditionMask(host);

			if (host._varScopeRuleSetId == host._ruleSetId && host._varScopeParent == parentVarSet && host._varScopeMask == mask)
				return;

			host._varSetId = _engine.ResolveVarScope(host._ruleSetId, parentVarSet, -1, mask);
			host._varScopeRuleSetId = host._ruleSetId;
			host._varScopeParent = parentVarSet;
			host._varScopeMask = mask;
		}

		/// <summary>
		/// Which of a node's rules currently hold, refreshed only when its rules, its state or any
		/// ancestor's state has moved since it was last worked out.
		/// </summary>
		private ulong ConditionMask(HostInstance host)
		{
			if (host._conditionGeneration != _engine.StateGeneration
				|| host._conditionRuleSetId != host._ruleSetId
				|| host._conditionState != host._state)
			{
				host._conditionMask = _engine.EvaluateConditions(host._ruleSetId, host);
				host._conditionGeneration = _engine.StateGeneration;
				host._conditionRuleSetId = host._ruleSetId;
				host._conditionState = host._state;
			}

			return host._conditionMask;
		}

		/// <summary>
		/// Brings every host below <paramref name="instance"/> up to date with what it now cascades
		/// from, whether or not the component that owns it re-renders.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A host is otherwise only re-matched and re-resolved when its own element is reconciled. That
		/// misses everything under a memoised component, which is most of a tree: a theme class toggled
		/// on the root, a <c>.selected</c> class on a card whose title is a component, a custom property
		/// that changed value. Selectors can name an ancestor's class and declarations can read an
		/// ancestor's variables or inherit its text properties, so any of those moving on a host is a
		/// change to what its descendants resolve to.
		/// </para>
		/// <para>
		/// The walk ignores component boundaries on purpose. Hosts that are about to be reconciled
		/// anyway pay a cache hit for arriving here first.
		/// </para>
		/// </remarks>
		private void RefreshDescendants(Instance instance, bool rematch)
		{
			if (instance._children is null)
				return;

			for (var i = 0; i < instance._children.Count; i++)
			{
				var child = instance._children[i];

				if (child._unmounted)
					continue;

				if (child is HostInstance host)
				{
					if (rematch)
						host._matchDirty = true;

					Rematch(host);
					ResolveCachedVarScope(host, host._nearestHost?._varSetId ?? 0);
					Restyle(host);
				}

				RefreshDescendants(child, rematch);
			}
		}

		/// <summary>
		/// Copies the element's inline properties onto the node, if they moved.
		/// </summary>
		/// <remarks>
		/// The comparison is the point, not the copy. An inline style is rebuilt from scratch every
		/// render, so a node carrying one — a board cell placed by arithmetic, say — would otherwise
		/// mint a fresh overlaid <see cref="ComputedStyle"/> every pass and defeat the identity check
		/// in <see cref="Restyle"/> permanently, which is exactly the case that most needs it.
		/// </remarks>
		private static void CaptureInline(HostInstance host, int node)
		{
			var slot = ElementPool.InlineSlotOf(node);
			var count = slot < 0 ? 0 : InlineArena.At(slot).Count;
			var entries = host._inlineEntries;

			if (count == 0)
			{
				if (entries is null || entries.Count == 0)
					return;

				entries.Clear();
				host.InvalidateOverlay();

				return;
			}

			if (entries is not null && InlineMatches(entries, slot))
				return;

			host._inlineEntries = entries ??= new List<PropEntry>(count);
			entries.Clear();

			for (var i = 0; i < count; i++)
			{
				var entry = InlineArena.At(slot).EntryAt(i);
				entries.Add(new PropEntry(entry.Id, entry.Value));
			}

			host.InvalidateOverlay();
		}

		private static bool InlineMatches(List<PropEntry> entries, int slot)
		{
			if (entries.Count != InlineArena.At(slot).Count)
				return false;

			for (var i = 0; i < entries.Count; i++)
			{
				var entry = InlineArena.At(slot).EntryAt(i);

				// Order is comparable because both sides are built by the same render method in the
				// same order; InlineStyle.Set appends, so a given component always emits the same
				// sequence of ids.
				if (entries[i].Id != entry.Id || !entries[i].Value.Equals(entry.Value))
					return false;
			}

			return true;
		}

		internal void TickLifecycle(float deltaTime)
		{
			// Clearing :enter one tick after the mount is what turns the snapped-to entry style
			// into a transition towards the resting style.
			for (var i = 0; i < _entering.Count; i++)
			{
				var host = _entering[i];
				if (host._unmounted)
					continue;

				host._state &= ~UiStates.s_enter.Mask;
				_engine.NoteStateChanged();
				Restyle(host);
			}

			_entering.Clear();

			for (var i = _exiting.Count - 1; i >= 0; i--)
			{
				var host = _exiting[i];
				host._exitRemaining -= deltaTime;

				if (host._exitRemaining > 0f)
					continue;

				_exiting.RemoveAt(i);
				FinishExit(host);
			}
		}

		private bool TryBeginExit(HostInstance host)
		{
			if (host._exitPlayed || !_engine.UsesState(host._ruleSetId, UiStates.s_exit))
				return false;

			host._yoga.Parent?.RemoveChild(host._yoga);
			host._state |= UiStates.s_exit.Mask;
			_engine.NoteStateChanged();

			// The subtree, not just the node: `.panel:exit > .body` is how a child animates out with it.
			((IStyleUpdater)this).RestyleSubtree(host);

			host._exitRemaining = host._style.MaxMotionDuration();

			if (host._exitRemaining <= 0f)
				return false;

			_exiting.Add(host);

			return true;
		}

		private void FinishExit(HostInstance host)
		{
			host._state &= ~UiStates.s_exit.Mask;
			_engine.NoteStateChanged();
			host._exitRemaining = 0f;
			host._exitPlayed = true;

			// Now that the exit is over the node unmounts like any other, subtree and all.
			Unmount(host);
		}

		private void CancelExit(HostInstance host)
		{
			if (!_exiting.Remove(host))
				return;

			host._state &= ~UiStates.s_exit.Mask;
			_engine.NoteStateChanged();
			host._exitRemaining = 0f;
			((IStyleUpdater)this).RestyleSubtree(host);
		}

		private void Restyle(HostInstance host)
		{
			var parentInherited = host._nearestHost?._inheritedId ?? 0;

			// A pure function of the node's rules, its own state and every ancestor's — the last of
			// which the engine's generation counter stands in for. See HostInstance._conditionMask.
			ConditionMask(host);

			var computed = _engine.Resolve(host._ruleSetId, host._conditionMask, parentInherited, host._varSetId);
			computed = Overlay(host, computed);

			// Resolved before the early-out below, because descendants read this whether or not
			// anything was applied here. It is a pure function of the two inputs — and it allocates a
			// dictionary for any node declaring an inherited property, which `color` and `font-size`
			// make almost every text-bearing node — so the node remembers what it derived it from.
			if (!ReferenceEquals(host._inheritedFrom, computed) || host._inheritedParent != parentInherited)
			{
				host._inheritedId = _engine.ResolveInherited(computed, parentInherited);
				host._inheritedFrom = computed;
				host._inheritedParent = parentInherited;
			}

			// The commit half of a render, and the whole reason a parent's re-render can stop here.
			// The engine hands back interned styles, so a result that did not change is the *same*
			// object — and re-applying it would rewrite forty Yoga properties across the interop
			// boundary, reconfigure the graphic and dirty its mesh, all to arrive back where the node
			// already was.
			if (host._styleApplied && ReferenceEquals(host._style, computed))
				return;

			host._styleApplied = true;
			host.ApplyStyle(computed, Context);

			// After the cascade has landed, because an animation reads the node's cascaded values
			// for the endpoints CSS leaves implicit — a track that starts past 0% runs from them.
			host.SyncAnimation(computed, Context, _engine);
		}

		void IStyleUpdater.RestyleSubtree(HostInstance changed)
		{
			// A state rule can declare variables — `.card:hover { --glow: … }` — so the scope moves with
			// the state, and a moved scope reaches every descendant reading it, dependent or not.
			var previousVarSet = changed._varSetId;
			ResolveCachedVarScope(changed, changed._nearestHost?._varSetId ?? 0);

			Restyle(changed);

			if (changed._varSetId != previousVarSet)
				RefreshDescendants(changed, rematch: false);
			else
				RestyleDependents(changed);
		}

		void IStyleUpdater.NoteStateChanged()
		{
			_engine.NoteStateChanged();
		}

		private void RestyleDependents(Instance instance)
		{
			if (instance._children is null)
				return;

			for (var i = 0; i < instance._children.Count; i++)
			{
				var child = instance._children[i];

				if (child is HostInstance host && host._dependsOnState)
					Restyle(host);

				RestyleDependents(child);
			}
		}

		/// <summary>
		/// Lays a node's inline properties over its cascaded style, at the highest precedence.
		/// </summary>
		/// <remarks>
		/// The result is memoised on the node, because it is a pure function of the cascaded style and
		/// the inline entries and both change rarely. That is what keeps an inline-styled node's
		/// resolved style reference-stable across renders — without it every such node would rebuild,
		/// re-sort and re-allocate its style every pass and then re-apply the whole thing, which for a
		/// board of absolutely positioned cells is the dominant cost of a move.
		/// <para>
		/// <see cref="CaptureInline"/> drops the cache when the entries themselves change, so the
		/// cascaded style's identity is the only other thing the key needs.
		/// </para>
		/// </remarks>
		private ComputedStyle Overlay(HostInstance host, ComputedStyle cascaded)
		{
			var inline = host._inlineEntries;

			if (inline is null || inline.Count == 0)
				return cascaded;

			if (host._overlayResult is not null && ReferenceEquals(host._overlayCascaded, cascaded))
				return host._overlayResult;

			// A hit on the second slot promotes it, so the two stay ordered by how recently they were
			// asked for and a node alternating between two styles hits on both crossings.
			if (host._overlayResultAlt is not null && ReferenceEquals(host._overlayCascadedAlt, cascaded))
			{
				var promoted = host._overlayResultAlt;

				host._overlayCascadedAlt = host._overlayCascaded;
				host._overlayResultAlt = host._overlayResult;
				host._overlayCascaded = cascaded;
				host._overlayResult = promoted;

				return promoted;
			}

			var entries = _overlayEntries;
			entries.Clear();

			for (var i = 0; i < cascaded.Count; i++)
				entries.Add(cascaded[i]);

			entries.AddRange(inline);

			var result = ComputedStyle.FromEntries(entries);

			host._overlayCascadedAlt = host._overlayCascaded;
			host._overlayResultAlt = host._overlayResult;
			host._overlayCascaded = cascaded;
			host._overlayResult = result;

			return result;
		}
	}
}

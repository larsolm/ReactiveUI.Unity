using System;
using System.Collections.Generic;
using ReactiveUI.Yoga;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Owns one UI tree: the instance tree, the layout engine, the scheduler, and the per-frame
	/// pipeline that drives them.
	/// </summary>
	internal sealed class UiRuntime : IDisposable
	{
		private const int MaxRenderPasses = 16;

		private readonly FocusManager _focus = new();
		private readonly HotkeyRegistry _hotkeys = new();
		private readonly InputActionRegistry _actions = new();
		private readonly Scheduler _scheduler = new();
		private readonly HostFactory _factory;
		private readonly Reconciler _reconciler;
		private readonly RootHost _root;
		private readonly RootHost _overlay;
		private readonly RectTransform _container;
		private readonly StyleEngine _engine;
		private readonly UiInputDriver _input;
		private readonly MediaWatch _watch = new();
		private readonly IStyleSheetSource? _sheetSource;
		private readonly string? _preloadKey;

		private Func<Element>? _rootFactory;
		private bool _rootPending;
		private bool _restylePending;
		private float _remSize;
		private Vector2 _lastContainerSize;
		private Vector2 _mediaSize;
		private bool _deviceChanged;

		/// <param name="inputBindings">
		/// The game's own navigate and submit actions; null keeps the built-in device reads.
		/// </param>
		public UiRuntime(RectTransform container, float remSize, UiInputBindings? inputBindings = null)
			: this(container, remSize, inputBindings, null)
		{
		}

		/// <param name="sheetSource">
		/// Sheets private to this runtime instead of the project-wide <see cref="StyleSheets"/>, so a
		/// test can style a tree without disturbing the editor's live catalog.
		/// </param>
		/// <param name="preloadKey">
		/// The <see cref="StylePreloadManifest"/> entry this runtime loads at start and records into;
		/// null for neither.
		/// </param>
		internal UiRuntime(
			RectTransform container,
			float remSize,
			UiInputBindings? inputBindings,
			IStyleSheetSource? sheetSource,
			string? preloadKey = null)
		{
			_sheetSource = sheetSource;
			_preloadKey = preloadKey;

			_container = container;
			_remSize = remSize;

			var yogaConfig = new YogaConfig() { UseWebDefaults = true };

			_factory = new HostFactory(yogaConfig);

			_root = new RootHost();
			_root.Initialize(container.gameObject, new YogaNode(yogaConfig), ownsLayout: false);
			_root._state = UiStates.s_root.Mask;

			_engine = new StyleEngine(new StyleContext(remSize));

			// Seeded before the sheets are handed over, so the first match already knows which
			// conditional rules are live rather than discovering it a frame later.
			_mediaSize = container.rect.size;
			_lastContainerSize = _mediaSize;

			var environment = CurrentEnvironment();
			_engine.SetMediaEnvironment(environment);
			_watch.Publish(environment);

			_engine.SetSheets(CurrentSource);
			Preload();

			if (_sheetSource is null)
				StyleSheets.Changed += OnStyleSheetsChanged;
			else
				_sheetSource.Changed += OnStyleSheetsChanged;

			InputDeviceTracker.Listen();
			InputDeviceTracker.Changed += OnInputDeviceChanged;
			InputModalityTracker.Changed += OnModalityChanged;

			var overlayObject = new GameObject("ReactiveUIOverlay", typeof(RectTransform));
			var overlayRect = overlayObject.GetComponent<RectTransform>();
			overlayRect.SetParent(container, worldPositionStays: false);
			overlayRect.SetAsLastSibling();
			overlayRect.anchorMin = Vector2.zero;
			overlayRect.anchorMax = Vector2.one;
			overlayRect.offsetMin = Vector2.zero;
			overlayRect.offsetMax = Vector2.zero;

			_overlay = new RootHost();
			_overlay.Initialize(overlayObject, new YogaNode(yogaConfig), ownsLayout: false);
			_overlay._yoga.PositionType = YogaPositionType.Absolute;

			_input = new UiInputDriver(_focus, _hotkeys, _actions, inputBindings);
			_reconciler = new Reconciler(
				_factory, _scheduler, _engine, _focus, _hotkeys, _actions, _watch, new StyleContext(remSize));
		}

		/// <summary>
		/// Sets the tree's root. Takes a factory rather than an element because elements are built
		/// during a render pass, not before one.
		/// </summary>
		public void SetRoot(Func<Element> factory)
		{
			_rootFactory = factory;
			_rootPending = true;
		}

		/// <summary>
		/// Where focus lives and moves, for driving it from outside a render.
		/// </summary>
		public FocusManager Focus => _focus;

		/// <summary>
		/// Rescales the whole UI without re-rendering anything.
		/// </summary>
		public void SetRemSize(float remSize)
		{
			if (_remSize == remSize)
				return;

			_remSize = remSize;
			_reconciler.Context = new StyleContext(remSize);
			_engine.Context = new StyleContext(remSize);

			// A `rem` breakpoint is relative to this, so rescaling can cross one. The Context setter has
			// already dropped the caches, which is why the return value is discarded rather than acted on.
			var environment = CurrentEnvironment();
			_engine.SetMediaEnvironment(environment);
			_watch.Publish(environment);

			MarkMatchDirty(_root);
			_restylePending = true;
		}

		internal void Update()
		{
			var previous = StylePreloads.Current;
			StylePreloads.Current = _preloadKey;

			try
			{
				UpdateFrame();
			}
			finally
			{
				StylePreloads.Current = previous;
			}
		}

		/// <summary>
		/// Loads what this runtime's root recorded: its sheets into the engine, and their fonts and
		/// textures, so none of it loads mid-game the first time a node needs it.
		/// </summary>
		private void Preload()
		{
			if (_preloadKey is null || StylePreloads.Manifest?.Find(_preloadKey) is not { } entry)
				return;

			StylePreloads.Warm(entry, CurrentSource);

			if (CurrentSource is { } source)
			{
				foreach (var path in entry.Sheets)
					_engine.Preload(source.SlotOf(path));
			}
		}

		/// <summary>
		/// Clears hover and press states, which a hidden tree never hears end.
		/// </summary>
		/// <remarks>Everything else keeps its state and resumes with the next <see cref="Update"/>.</remarks>
		internal void Pause() => ClearPointerStates(_root);

		private static void ClearPointerStates(Instance instance)
		{
			if (instance is HostInstance host)
			{
				host.SetPointerInside(false);
				host.SetState(UiStates.s_active, false);
			}

			if (instance._children is null)
				return;

			for (var i = 0; i < instance._children.Count; i++)
				ClearPointerStates(instance._children[i]);
		}

		/// <remarks>
		/// Navigating hides the hover under a resting pointer, so only the focused element is lit; the
		/// pointer moving again brings it back.
		/// </remarks>
		private void OnModalityChanged(InputModality modality)
		{
			RefreshHover(_root);
		}

		private static void RefreshHover(Instance instance)
		{
			if (instance is HostInstance host)
				host.RefreshHover();

			if (instance._children is null)
				return;

			for (var i = 0; i < instance._children.Count; i++)
				RefreshHover(instance._children[i]);
		}

		private void UpdateFrame()
		{
			// Input first, so a device switch it notices reaches this frame's styles rather than the next.
			using (UiMarkers.Input.Auto())
				_input.Update();

			using (UiMarkers.Environment.Auto())
				SampleEnvironment();

			using (UiMarkers.Reconcile.Auto())
			{
				if (_rootPending && _rootFactory is not null)
				{
					_rootPending = false;
					_reconciler.ReconcileRoot(_root, _rootFactory());
				}

				if (_restylePending)
				{
					_restylePending = false;

					if (_rootFactory is not null)
						_reconciler.ReconcileRoot(_root, _rootFactory());
				}

				FlushRenders();
			}

			using (UiMarkers.Overlay.Auto())
				_reconciler.SyncOverlay(_root, _overlay);

			Layout();

			using (UiMarkers.Lifecycle.Auto())
				_reconciler.TickLifecycle(Time.unscaledDeltaTime);

			using (UiMarkers.Effects.Auto())
				_scheduler.FlushEffects();

			// After effects, so a screen's own default focus mounts before the fallback is chosen.
			_focus.Maintain();

			// Every element declared this frame is dead now: the instances hold the committed props,
			// the hosts hold the captured inline entries, and nothing below reads an element again.
			// Dropping the whole arena here is what makes declaring one free.
			//
			// It has to be after FlushRenders — which runs up to MaxRenderPasses of them, each
			// declaring more elements — and after the effects, which can only schedule work for a
			// later frame rather than render now.
			ElementPool.Reset();
			PropsArenas.ResetAll();
		}

		/// <summary>
		/// Brings the media environment up to date before anything renders.
		/// </summary>
		/// <remarks>
		/// Sampled here rather than in <see cref="Layout"/>, which runs after the render that wanted the
		/// answer: a query resolved from last frame's size renders one frame at the wrong breakpoint and
		/// then restyles, which is a visible pop and a wasted full re-match. Layout keeps its own size
		/// diff because it is asking a different question — whether Yoga needs to run at all.
		/// </remarks>
		private void SampleEnvironment()
		{
			var size = _container.rect.size;

			if (size == _mediaSize && !_deviceChanged)
				return;

			_mediaSize = size;
			_deviceChanged = false;

			var environment = CurrentEnvironment();

			// Hooks hear about every move, because a component reading the viewport is reading the number
			// itself. Only a query flipping is worth re-matching the tree for.
			_watch.Publish(environment);

			if (!_engine.SetMediaEnvironment(environment))
				return;

			MarkMatchDirty(_root);
			_restylePending = true;
		}

		private MediaEnvironment CurrentEnvironment()
		{
			return new MediaEnvironment(
				new Viewport(_mediaSize), _remSize, InputDeviceTracker.Device, InputDeviceTracker.Layout);
		}

		/// <remarks>
		/// Only flagged here: the tracker can be told from another runtime's input pass, and this runtime
		/// applies the change at its own next sample rather than mid-way through someone else's frame.
		/// </remarks>
		private void OnInputDeviceChanged()
		{
			_deviceChanged = true;
		}

		private IStyleSheetSource? CurrentSource => _sheetSource ?? StyleSheets.Source;

		private void OnStyleSheetsChanged()
		{
			_engine.SetSheets(CurrentSource);
			Preload();
			MarkMatchDirty(_root);
			_restylePending = true;
		}

		private static void MarkMatchDirty(Instance instance)
		{
			if (instance is HostInstance host)
			{
				host._matchDirty = true;

				host.InvalidateStyleCaches();
				host.StopMotion();
			}

			if (instance._children is null)
				return;

			for (var i = 0; i < instance._children.Count; i++)
				MarkMatchDirty(instance._children[i]);
		}

		private void FlushRenders()
		{
			for (var pass = 0; pass < MaxRenderPasses; pass++)
			{
				if (!_scheduler.HasDirty)
					return;

				var dirty = _scheduler.TakeDirty();
				for (var i = 0; i < dirty.Count; i++)
				{
					// A shallower re-render may already have refreshed this one.
					if (dirty[i]._dirty && !dirty[i]._unmounted)
						_reconciler.RerenderInPlace(dirty[i]);
				}
			}

			if (!_scheduler.HasDirty) return;

			Debug.LogError(
				$"[ReactiveUI] Still re-rendering after {MaxRenderPasses} passes. A component is "
				+ "setting state during its own render, or two components are invalidating each other.");
			_scheduler.Clear();
		}

		private void Layout()
		{
			var size = _container.rect.size;
			var resized = size != _lastContainerSize;
			_lastContainerSize = size;

			if (!resized && !_root._yoga.IsDirty && !_overlay._yoga.IsDirty)
				return;

			using (UiMarkers.LayoutCalculate.Auto())
			{
				_root._yoga.Width = YogaValue.Point(size.x);
				_root._yoga.Height = YogaValue.Point(size.y);
				_root._yoga.CalculateLayout(size.x, size.y);

				// The overlay is a separate layout root: portalled content is positioned against the
				// screen, not against wherever it happened to be declared.
				_overlay._yoga.Width = YogaValue.Point(size.x);
				_overlay._yoga.Height = YogaValue.Point(size.y);
				_overlay._yoga.CalculateLayout(size.x, size.y);
			}

			using (UiMarkers.LayoutApply.Auto())
			{
				ApplyLayout(_root);
				ApplyLayout(_overlay);
			}
		}

		private static void ApplyLayout(HostInstance host)
		{
			if (host._children is not null)
				ApplyLayoutToChildren(host._children);

			if (!host._yoga.HasNewLayout)
				return;

			host._yoga.MarkLayoutSeen();
		}

		private static void ApplyLayoutToChildren(List<Instance> instances)
		{
			for (var i = 0; i < instances.Count; i++)
			{
				switch (instances[i])
				{
					case HostInstance host:
						var laidOut = host._yoga.HasNewLayout;

						if (laidOut)
						{
							host._rectTransform.sizeDelta = new Vector2(host._yoga.LayoutWidth, host._yoga.LayoutHeight);
							// Yoga measures downward from the top-left; the rect's anchored
							// position runs upward, hence the negated Y.
							host._layoutPosition = new Vector2(host._yoga.LayoutLeft, -host._yoga.LayoutTop);
							host.RefreshTransform();
							host._yoga.MarkLayoutSeen();
						}

						if (host._children is not null)
							ApplyLayoutToChildren(host._children);

						// After the children, because anything measuring them needs their final
						// geometry rather than the previous frame's.
						if (laidOut)
							host.AfterLayout();

						break;

					default:
						if (instances[i]._children is not null)
							ApplyLayoutToChildren(instances[i]._children!);

						break;
				}
			}
		}

		public void Dispose()
		{
			_hotkeys.Clear();
			_actions.Clear();
			_focus.Dispose();
			if (_sheetSource is null)
				StyleSheets.Changed -= OnStyleSheetsChanged;
			else
				_sheetSource.Changed -= OnStyleSheetsChanged;

			InputDeviceTracker.Changed -= OnInputDeviceChanged;
			InputModalityTracker.Changed -= OnModalityChanged;
			_factory.BeginShutdown();
			_reconciler.ReconcileRoot(_root, default);
			_scheduler.Clear();
			_factory.Dispose();
			_root._yoga.RemoveAll();
		}
	}
}

using System;
using System.Collections.Generic;
using ReactiveUI.Yoga;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ReactiveUI
{
	internal interface IStyleUpdater
	{
		void RestyleSubtree(HostInstance changed);

		/// <summary>
		/// Announces that some node's state bits moved, invalidating every cached condition mask.
		/// </summary>
		void NoteStateChanged();
	}

	internal abstract class HostInstance : Instance, IMatchTarget, IMotionTarget
	{
		/// <summary>The initial <c>transform-origin</c> on both axes, as CSS defines it.</summary>
		private static readonly StyleLength s_center = new(50f, LengthUnit.Percent);

		private int _typeName;

		internal GameObject _gameObject = null!;
		internal RectTransform _rectTransform = null!;
		internal YogaNode _yoga = null!;
		internal ComputedStyle _style = ComputedStyle.Empty;
		internal ClassSet _classes;
		internal ulong _state;
		internal ulong _conditionMask;

		// The inputs _conditionMask was derived from. Re-deriving it re-runs the selector matcher over
		// every state-bearing rule on this node, walking the ancestor chain for each — and during an
		// ordinary props-driven re-render nothing about state has moved, so every one of those walks
		// reproduces the mask the node is already holding.
		internal int _conditionRuleSetId = -1;
		internal ulong _conditionState;
		internal int _conditionGeneration = -1;

		internal bool _dependsOnState;
		internal List<PropEntry>? _inlineEntries;
		internal IStyleUpdater? _updater;
		internal FocusManager? _focus;
		internal float _exitRemaining;
		internal bool _exitPlayed;
		internal int _ruleSetId;
		internal int _inheritedId;
		internal int _varSetId;
		internal bool _matchDirty = true;
		internal Vector2 _layoutPosition;
		internal float _translateX;
		internal float _translateY;
		internal bool _translateXRelative;
		internal bool _translateYRelative;
		internal float _scaleValue = 1f;
		internal float _rotationValue;
		internal float _motionX;
		internal float _motionY;
		internal float _motionScale = 1f;
		internal float _motionRotation;
		internal ElementRef? _ref;
		internal HostInstance? _previousHostSibling;

		/// <summary>
		/// The ordered host children this node's transform and Yoga list were last placed against.
		/// </summary>
		/// <remarks>
		/// Placing children is the one part of a reconcile that is super-linear: keeping the transform
		/// order honest means asking Unity where each child currently sits, and
		/// <c>Transform.GetSiblingIndex</c> is a linear search of the parent's children — so one parent
		/// costs O(n²) native calls in its child count. Nearly every re-render changes a leaf's props
		/// and leaves every host list in the tree exactly as it was, and one element-wise compare is
		/// what buys the right to skip the whole walk.
		/// <para>
		/// Held as the list rather than as a per-child index, deliberately. A cached index goes stale
		/// the moment a sibling moves in front of it — <c>SetSiblingIndex</c> shifts everything it
		/// passes — and reasoning about which stale values are still safe to trust is exactly the kind
		/// of thing that is right until some permutation makes it wrong. Comparing the whole list asks
		/// a question with no such edge: either nothing moved, or the exact original walk runs.
		/// </para>
		/// </remarks>
		internal List<HostInstance>? _syncedHosts;

		/// <summary>
		/// The content rect this node was last parented into, or null if it is not placed anywhere.
		/// </summary>
		/// <remarks>
		/// The other half of the check above, and not an optimisation — the list compare is wrong
		/// without it. A host is pooled by reference, and <see cref="HostFactory.Rent"/> hands one back
		/// still parented under the pool, leaving <see cref="Reconciler.SyncHostList"/> to place it. So
		/// a screen swap that unmounts one <c>View</c> and mounts another gets the same instance back at
		/// the same position: the parent's list is reference-identical, and a skip there leaves the node
		/// sitting in the pool, invisible. Cleared in <see cref="ResetForPool"/>, which is exactly the
		/// moment that can happen.
		/// </remarks>
		internal Transform? _placedUnder;

		/// <summary>
		/// Whether <see cref="_style"/> has actually been pushed onto this node yet.
		/// </summary>
		/// <remarks>
		/// Distinguishes "resolved to the empty style" from "never applied", which matters because a
		/// pooled node arrives carrying the previous occupant's Yoga properties — <see cref="ResetForPool"/>
		/// clears the managed side but the native node keeps whatever was last written to it. Without
		/// this the identity check in <c>Restyle</c> would let a recycled node inherit them.
		/// </remarks>
		internal bool _styleApplied;

		/// <summary>
		/// Whether the Yoga node currently holds a box this style actually wrote.
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="_styleApplied"/>, and for the same reason it exists: a pooled node
		/// arrives with the previous occupant's values still on the native side while
		/// <see cref="_style"/> has been reset to empty, so the layout comparison would find nothing to
		/// do and leave them there. It is also what a rem-size change clears — two identical styles
		/// resolve to different Yoga values under different rem sizes, and the styles alone cannot say
		/// so.
		/// </remarks>
		internal bool _layoutApplied;

		// The cascaded styles the cached overlays below were built from, and their results. Kept per
		// node because an inline style is per node by definition; see Reconciler.Overlay.
		//
		// Two slots rather than one, because one is the number that fails on the case the cache exists
		// for. An inline-styled node that also styles on :hover alternates between exactly two cascaded
		// styles, so a single slot misses on every pointer crossing in both directions — which is the
		// board cell, and the very node whose overlay is most expensive to rebuild.
		internal ComputedStyle? _overlayCascaded;
		internal ComputedStyle? _overlayResult;
		internal ComputedStyle? _overlayCascadedAlt;
		internal ComputedStyle? _overlayResultAlt;

		// The inputs _inheritedId and _varSetId were last derived from. Both resolutions allocate a
		// dictionary and hash it only to intern back to the id the node already held, so remembering
		// the answer per node is what turns them into two comparisons. Cached here rather than in the
		// engine so nothing accumulates: one entry per node, overwritten in place.
		internal ComputedStyle? _inheritedFrom;
		internal int _inheritedParent = -1;
		internal int _varScopeRuleSetId = -1;
		internal int _varScopeParent = -1;
		internal ulong _varScopeMask;

		/// <summary>
		/// Drops the memoised inline overlay, for when the inline entries themselves changed.
		/// </summary>
		internal void InvalidateOverlay()
		{
			_overlayCascaded = null;
			_overlayResult = null;
			_overlayCascadedAlt = null;
			_overlayResultAlt = null;
		}

		/// <summary>
		/// Drops everything derived from the stylesheets, for when the stylesheets themselves changed.
		/// </summary>
		/// <remarks>
		/// A reload re-issues every interned id from zero, so a cached rule-set or variable-scope id
		/// compares equal to one that now names something else entirely. The inherited cache is keyed
		/// on a <see cref="ComputedStyle"/> reference and so invalidates itself — the engine's table is
		/// cleared, and every style resolved afterwards is a new object — but the id-keyed ones cannot,
		/// and neither can the record of what was last applied.
		/// </remarks>
		internal void InvalidateStyleCaches()
		{
			InvalidateOverlay();

			// A reload re-matches every node, and the sibling-combinator restyle that rides along with
			// placement has to run again — so drop the record that would let it be skipped.
			_syncedHosts?.Clear();

			_inheritedFrom = null;
			_inheritedParent = -1;
			_conditionGeneration = -1;
			_conditionRuleSetId = -1;
			_varScopeRuleSetId = -1;
			_varScopeParent = -1;
			_varScopeMask = 0UL;
			_styleApplied = false;
			_layoutApplied = false;
		}

		// `transform-origin`, split per axis into the part that scales with the box (a percentage)
		// and the part that does not (a `px`/`rem` offset), because the box is not measured yet when
		// the style is applied. Both are measured from the left and the top, as CSS measures them.
		private Vector2 _originFraction = new(0.5f, 0.5f);
		private Vector2 _originOffset;

		// What RefreshTransform last wrote. Writing an unchanged value to a Transform still dirties it
		// and forces the Canvas to re-batch that renderer, and this method runs on every layout and on
		// every frame of every transform animation — twice a frame for one animating translate(x, y),
		// since the two channels each drive it. Only the pivot was guarded, and it was guarded by
		// reading the pivot back, which is itself a native call.
		private Vector2 _writtenPivot;
		private Vector2 _writtenPosition;
		private float _writtenScale;
		private float _writtenRotation;
		private bool _transformWritten;

		private bool _ownsLayout = true;
		private bool _hidden;
		private HostChannels? _hostChannels;
		private AnimationPlayer? _animation;
		private CanvasGroup? _canvasGroup;
		private RectMask2D? _mask;

		/// <summary>
		/// The opacity last written, kept because <c>visibility</c> shares the CanvasGroup alpha with
		/// it — so the alpha on the component is not always what <c>opacity</c> resolved to.
		/// </summary>
		private float _opacityValue = 1f;

		/// <summary>The running animation, for the apply sites that must not fight it.</summary>
		protected AnimationPlayer? Animation => _animation;

		private Action<float>? _setOpacity;
		private Action<float>? _setTranslateX;
		private Action<float>? _setTranslateY;
		private Action<float>? _setScale;
		private Action<float>? _setRotation;

		internal abstract HostKind Kind { get; }

		IMatchTarget? IMatchTarget.MatchParent
		{
			get
			{
				// Components are skipped: they are not levels in the selector tree.
				for (var current = _parent; current is not null; current = current._parent)
				{
					if (current is HostInstance host) return host;
				}

				return null;
			}
		}

		IMatchTarget? IMatchTarget.MatchPreviousSibling => _previousHostSibling;

		ClassSet IMatchTarget.MatchClasses => _classes;

		ulong IMatchTarget.MatchState => _state;

		/// <summary>
		/// Gives this node focus.
		/// </summary>
		public void Focus()
		{
			_focus?.Focus(this);
		}

		internal void SetState(StateBit bit, bool on)
		{
			if (!bit.IsValid)
				return;

			var next = on ? _state | bit.Mask : _state & ~bit.Mask;
			if (next == _state)
				return;

			_state = next;

			// Before the restyle, because the restyle is what reads the masks this invalidates.
			_updater?.NoteStateChanged();
			_updater?.RestyleSubtree(this);
		}


		bool IMatchTarget.MatchesType(int typeId) => _typeName == typeId;

		/// <summary>
		/// Records what the cascade matches this node on, and says whether any of it moved.
		/// </summary>
		/// <remarks>
		/// One type name rather than a list: a node answers to the host primitive it is and nothing
		/// else, now that the components above it no longer forward their names down.
		/// </remarks>
		internal bool SetMatchIdentity(ClassSet classes, int typeId)
		{
			if (_typeName == typeId && _classes.Equals(classes))
				return false;

			_classes = classes;
			_typeName = typeId;
			_matchDirty = true;

			return true;
		}

		/// <summary>
		/// Names the GameObject after its first class, falling back to the host kind.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The first class rather than all of them: it is the base class at every call site
		/// (<c>Styles.Tile | SizeClass(…) | VariantClass(…)</c>), <see cref="ClassTable.NameOf"/> hands
		/// back the interned string without allocating, and joining the set would rename nodes as
		/// modifier classes toggled.
		/// </para>
		/// <para>
		/// There is no id to name a node with any more, and nothing lost by that: every id this
		/// replaced sat beside a class that already said the same thing.
		/// </para>
		/// </remarks>
		internal void RefreshName()
		{
			if (_gameObject == null)
				return;

			var name = _classes.Count > 0 ? ClassTable.NameOf(_classes[0]) : NameOf(Kind);

			if (_gameObject.name != name)
				_gameObject.name = name;
		}

		/// <summary>
		/// The default GameObject name for a kind. A literal per case rather than
		/// <c>kind.ToString()</c>, which allocates a fresh string on every call.
		/// </summary>
		internal static string NameOf(HostKind kind) => kind switch
		{
			HostKind.Text => "Text",
			HostKind.Image => "Image",
			HostKind.Pressable => "Pressable",
			HostKind.Scroll => "Scroll",
			_ => "View",
		};

		internal virtual RectTransform ContentRect => _rectTransform;

		internal void Initialize(GameObject gameObject, YogaNode yoga, bool ownsLayout = true)
		{
			_gameObject = gameObject;
			_gameObject.TryGetComponent(out _rectTransform);
			_yoga = yoga;
			_ownsLayout = ownsLayout;

			if (!ownsLayout)
				return;

			// Yoga owns geometry completely, so the rect is anchored to the parent's top-left and
			// never stretches. Layout then writes size and position directly. The pivot starts at
			// that same corner but belongs to <see cref="RefreshTransform"/> from here on, because
			// `transform-origin` moves it.
			_rectTransform.anchorMin = new Vector2(0f, 1f);
			_rectTransform.anchorMax = new Vector2(0f, 1f);
			_rectTransform.pivot = new Vector2(0f, 1f);
		}

		internal virtual void ApplyProps(int node)
		{
		}

		internal virtual void UpdateCallbacks(int node)
		{
		}

		internal virtual void EnablePointer()
		{
		}

		internal virtual void StopMotion()
		{
			_hostChannels?.Stop();

			// Reset rather than Stop: stopping hands the channels back through a restyle, and this
			// runs in the middle of invalidating the very tables that restyle would read. The caller
			// re-matches the whole tree straight afterwards, so the node picks its animation up again
			// — against the rebuilt clip — on its next apply.
			_animation?.Reset();
		}

		/// <summary>
		/// Starts, stops or leaves alone the <c>@keyframes</c> animation this style names.
		/// </summary>
		/// <remarks>
		/// Driven from the reconciler rather than from <see cref="ApplyStyle"/> because resolving a
		/// clip needs the engine, and because it has to happen after the cascade has been written:
		/// the animation reads the node's cascaded values for the endpoints CSS leaves implicit.
		/// </remarks>
		internal void SyncAnimation(ComputedStyle style, in StyleContext ctx, StyleEngine engine)
		{
			var spec = style.AnimationFor();

			if (spec.IsNone)
			{
				_animation?.Stop();

				return;
			}

			// A player, like a channel, only exists once something asks for one.
			_animation ??= new AnimationPlayer();
			_animation.Sync(spec, engine.Clip(spec.NameId), style, ctx, this);
		}

		void IMotionTarget.WriteMotion(MotionChannelId channel, float value) => WriteMotion(channel, value);

		void IMotionTarget.WriteMotion(MotionChannelId channel, Color value) => WriteMotion(channel, value);

		void IMotionTarget.ReleaseMotion(ushort channels) => ReleaseMotion(channels);

		void IMotionTarget.SetRelative(MotionChannelId channel, bool relative) => SetRelative(channel, relative);

		private void SetRelative(MotionChannelId channel, bool relative)
		{
			ref var flag = ref channel == MotionChannelId.TranslateX ? ref _translateXRelative : ref _translateYRelative;

			if (flag == relative)
				return;

			flag = relative;
			RefreshTransform();
		}

		internal virtual void WriteMotion(MotionChannelId channel, float value)
		{
			switch (channel)
			{
				case MotionChannelId.Opacity: _setOpacity?.Invoke(value); break;
				case MotionChannelId.TranslateX: _setTranslateX?.Invoke(value); break;
				case MotionChannelId.TranslateY: _setTranslateY?.Invoke(value); break;
				case MotionChannelId.Scale: _setScale?.Invoke(value); break;
				case MotionChannelId.Rotation: _setRotation?.Invoke(value); break;
			}
		}

		/// <summary>Overridden by <see cref="VisualHost"/>, which is where the colours live.</summary>
		internal virtual void WriteMotion(MotionChannelId channel, Color value)
		{
		}

		/// <summary>
		/// Hands channels back to the cascade after an animation that owned them has ended.
		/// </summary>
		/// <remarks>
		/// Each channel adopts the value the animation left behind first, so a <c>transition</c> on
		/// the same property eases back from where the node actually is instead of from the value it
		/// held before the animation ever started. Clearing <c>_styleApplied</c> is what gets past
		/// the reconciler's identity check: the resolved style has not changed, but what is on the
		/// node no longer matches it.
		/// </remarks>
		internal virtual void ReleaseMotion(ushort channels)
		{
			if (_hostChannels is not null)
			{
				for (var channel = MotionChannelId.Opacity; channel < MotionChannels.FirstVisual; channel++)
				{
					if ((channels & MotionChannels.Bit(channel)) != 0)
						_hostChannels[channel].Adopt(CurrentMotion(channel));
				}
			}

			_styleApplied = false;
			_updater?.RestyleSubtree(this);
		}

		private float CurrentMotion(MotionChannelId channel) => channel switch
		{
			MotionChannelId.Opacity => _opacityValue,
			MotionChannelId.TranslateX => _translateX,
			MotionChannelId.TranslateY => _translateY,
			MotionChannelId.Scale => _scaleValue,

			// Stored negated, because CSS measures rotation the other way round from Unity.
			_ => -_rotationValue,
		};

		internal virtual void AfterLayout()
		{
		}

		internal virtual void ApplyStyle(ComputedStyle style, in StyleContext ctx)
		{
			// Compared before _style moves, because the question is what the node is holding now.
			if (!_layoutApplied || !ComputedStyle.LayoutEquivalent(_style, style))
			{
				StyleApplier.ApplyLayout(_yoga, style, ctx);
				_layoutApplied = true;
			}

			_style = style;
			ApplyClip(style);
			ApplyMotion(style, ctx);
		}

		/// <summary>Whether this kind of host clips regardless of what the style says.</summary>
		protected virtual bool AlwaysClips => false;

		/// <summary>
		/// Gives `overflow: hidden` and `overflow: scroll` their visual half.
		/// </summary>
		/// <remarks>
		/// The keyword already reached Yoga, which uses it to decide what a scroll container measures
		/// — but layout alone never stopped a child from painting outside the box. A
		/// <c>RectMask2D</c> is what actually clips, and it is only attached when asked for, since it
		/// costs a rect-clipping pass on the whole subtree. Corners are not respected: the mask clips
		/// to the rect, so a rounded box still spills its children over the rounding.
		/// </remarks>
		private void ApplyClip(ComputedStyle style)
		{
			var clip = AlwaysClips
				|| (YogaOverflow)style.Keyword(PropId.Overflow, (int)YogaOverflow.Visible) != YogaOverflow.Visible;

			if (!clip && _mask == null)
				return;

			_mask = Ensure(_mask);
			_mask!.enabled = clip;
		}

		/// <summary>
		/// Writes the <c>transform</c> channels onto the RectTransform and <c>opacity</c> onto a
		/// CanvasGroup. Lives on the base host rather than on <see cref="VisualHost"/> because
		/// neither is a property of the paint: text and image nodes have no graphic to tint but they
		/// rotate, scale and fade just the same.
		/// </summary>
		private void ApplyMotion(ComputedStyle style, in StyleContext ctx)
		{
			_setOpacity ??= ApplyOpacity;
			_setTranslateX ??= value => { _translateX = value; RefreshTransform(); };
			_setTranslateY ??= value => { _translateY = value; RefreshTransform(); };
			_setScale ??= value => { _scaleValue = value; RefreshTransform(); };

			// CSS measures rotation clockwise; Unity's Z axis measures it the other way. The flip
			// lives here so PropId.Rotation means the same thing whether it came from a stylesheet
			// or from an inline Css.Rotation.
			_setRotation ??= value => { _rotationValue = -value; RefreshTransform(); };

			// Channels only exist once something asks to be animated, so a static node pays nothing.
			// An animation needs them too: when it ends, the channel is what the property eases back
			// to the cascade through.
			if (style.Has(PropId.TransitionDuration) || style.Has(PropId.AnimationName))
				_hostChannels ??= new HostChannels();

			// The origin is not a channel of its own: it says where the other four act from, so it is
			// read straight rather than transitioned, and before them so the first write already
			// turns about the right point.
			var originX = style.Length(PropId.TransformOriginX, s_center);
			var originY = style.Length(PropId.TransformOriginY, s_center);
			var fraction = new Vector2(OriginFraction(originX), OriginFraction(originY));
			var offset = new Vector2(OriginOffset(originX, ctx), OriginOffset(originY, ctx));

			if (fraction != _originFraction || offset != _originOffset)
			{
				_originFraction = fraction;
				_originOffset = offset;

				// A rule that moves only the origin changes no channel, so nothing below would reach
				// the rect on its own.
				RefreshTransform();
			}

			// `visibility` folds into the same alpha as `opacity` — it is the one that also has to
			// stop the node eating clicks, since a hidden node still occupies its layout box.
			var hidden = style.Keyword(PropId.Visibility, 0) != 0;

			if (hidden != _hidden)
			{
				_hidden = hidden;
				_setOpacity(style.Number(PropId.Opacity, 1f));
			}

			WriteCascaded(MotionChannelId.Opacity, style, ctx, _setOpacity);
			WriteCascaded(MotionChannelId.TranslateX, style, ctx, _setTranslateX);
			WriteCascaded(MotionChannelId.TranslateY, style, ctx, _setTranslateY);
			WriteCascaded(MotionChannelId.Scale, style, ctx, _setScale);
			WriteCascaded(MotionChannelId.Rotation, style, ctx, _setRotation);
		}

		/// <summary>
		/// Pushes one cascaded float onto the node, through its transition channel when it has one.
		/// </summary>
		/// <remarks>
		/// A channel an animation is currently running is skipped outright: in CSS an animation
		/// outranks a normal declaration, so while it holds a property the cascade has nothing to say
		/// about it. Writing anyway would fight the tween for the same field every frame.
		/// </remarks>
		private void WriteCascaded(MotionChannelId channel, ComputedStyle style, in StyleContext ctx, Action<float> apply)
		{
			if (_animation is not null && _animation.Owns(channel))
				return;

			if (MotionChannels.IsTranslate(channel)
				&& style.TryGet(MotionChannels.PropFor(channel), out var declared)
				&& MotionChannels.IsRelative(declared) is { } relative)
			{
				SetRelative(channel, relative);
			}

			var value = MotionChannels.FloatValue(channel, style, ctx);

			if (_hostChannels is null)
			{
				apply(value);

				return;
			}

			_hostChannels[channel].Set(value, style.TransitionFor(MotionChannels.PropFor(channel)), apply);
		}

		private void ApplyOpacity(float opacity)
		{
			_opacityValue = opacity;

			// A CanvasGroup is only worth its cost when something is actually translucent or hidden.
			if (opacity >= 1f && !_hidden && _canvasGroup == null)
				return;

			_canvasGroup = Ensure(_canvasGroup);
			_canvasGroup!.alpha = _hidden ? 0f : opacity;

			// An invisible node must not keep taking the clicks its box still covers.
			_canvasGroup.blocksRaycasts = !_hidden;
		}

		/// <summary>
		/// Fetches a component, adding it if the GameObject does not have one yet.
		/// </summary>
		/// <remarks>
		/// Uses <c>TryGetComponent</c>, which reports absence through a bool rather than a
		/// reference. That matters here: <c>GetComponent&lt;T&gt;() ?? AddComponent&lt;T&gt;()</c>
		/// looks equivalent but is not — a missing component comes back as Unity's fake null, which
		/// <c>??</c> treats as a real reference, so the add never happens and the first use throws
		/// <c>MissingComponentException</c>.
		/// </remarks>
		protected T Ensure<T>(T? existing) where T : Component
		{
			if (existing != null)
				return existing;

			return _gameObject.TryGetComponent<T>(out var component) ? component : _gameObject.AddComponent<T>();
		}

		internal void RefreshTransform()
		{
			// Scale and rotation turn about the rect's pivot, so `transform-origin` is spelled by
			// moving the pivot there — and the anchored position then has to name the pivot rather
			// than the corner, because that is what Yoga measured. A root host was handed its rect
			// by the scene and does not own its anchoring, so it keeps the corner it came with.
			var origin = Vector2.zero;
			var box = _rectTransform.rect.size;

			if (_ownsLayout)
			{
				var size = _rectTransform.sizeDelta;
				var pivot = OriginPivot(size);

				if (!_transformWritten || _writtenPivot != pivot)
				{
					_rectTransform.pivot = pivot;
					_writtenPivot = pivot;
				}

				origin = new Vector2(pivot.x * size.x, -(1f - pivot.y) * size.y);
			}

			// CSS translates downward for a positive Y; the anchored position runs upward.
			var translateX = _translateXRelative ? _translateX * 0.01f * box.x : _translateX;
			var translateY = _translateYRelative ? _translateY * 0.01f * box.y : _translateY;

			var position = new Vector2(
				_layoutPosition.x + translateX + _motionX + origin.x,
				_layoutPosition.y - translateY + _motionY + origin.y);

			var scale = _scaleValue * _motionScale;
			var rotation = _rotationValue + _motionRotation;

			if (!_transformWritten || _writtenPosition != position)
			{
				_rectTransform.anchoredPosition = position;
				_writtenPosition = position;
			}

			if (!_transformWritten || !Mathf.Approximately(_writtenScale, scale))
			{
				_rectTransform.localScale = new Vector3(scale, scale, 1f);
				_writtenScale = scale;
			}

			if (!_transformWritten || !Mathf.Approximately(_writtenRotation, rotation))
			{
				_rectTransform.localEulerAngles = new Vector3(0f, 0f, rotation);
				_writtenRotation = rotation;
			}

			_transformWritten = true;
		}

		/// <summary>
		/// Where the pivot has to sit for the transform to act from the styled origin: the fraction
		/// straight through, plus any absolute part expressed as a fraction of the measured box.
		/// </summary>
		private Vector2 OriginPivot(Vector2 size)
		{
			var x = _originFraction.x + (size.x > 0f ? _originOffset.x / size.x : 0f);
			var y = _originFraction.y + (size.y > 0f ? _originOffset.y / size.y : 0f);

			// A pivot measures Y upward from the bottom; CSS measures the origin down from the top.
			return new Vector2(x, 1f - y);
		}

		private static float OriginFraction(StyleLength length) =>
			length.Unit == LengthUnit.Percent ? length.Value * 0.01f : 0f;

		private static float OriginOffset(StyleLength length, in StyleContext ctx) =>
			length.Unit == LengthUnit.Percent ? 0f : ctx.Resolve(length);

		internal override void ResetForPool()
		{
			base.ResetForPool();

			_style = ComputedStyle.Empty;
			InvalidateStyleCaches();
			_classes = default;
			_typeName = 0;

			// Back to the kind name, so a pooled node never carries the previous occupant's.
			RefreshName();

			_state = 0UL;
			_conditionMask = 0UL;
			_dependsOnState = false;
			_inlineEntries?.Clear();
			_updater = null;
			_focus?.Unregister(this);
			_focus = null;
			_exitRemaining = 0f;
			_exitPlayed = false;
			_motionX = 0f;
			_motionY = 0f;
			_motionScale = 1f;
			_motionRotation = 0f;
			_ref?.Unbind(this);
			_ref = null;
			_ruleSetId = 0;
			_inheritedId = 0;
			_varSetId = 0;
			_matchDirty = true;
			_previousHostSibling = null;
			_syncedHosts?.Clear();
			_placedUnder = null;
			_layoutPosition = Vector2.zero;
			_translateX = 0f;
			_translateY = 0f;
			_translateXRelative = false;
			_translateYRelative = false;
			_scaleValue = 1f;
			_rotationValue = 0f;
			_originFraction = new Vector2(0.5f, 0.5f);
			_originOffset = Vector2.zero;
			_hidden = false;
			_opacityValue = 1f;

			if (_mask != null)
				_mask.enabled = false;

			// Channels reset rather than merely stop: a recycled node must snap to its first paint
			// instead of animating from whatever the previous occupant looked like. The player is
			// reset for the same reason, and without releasing — there is nothing left to hand back.
			_hostChannels?.Reset();
			_animation?.Reset();

			if (_canvasGroup != null)
			{
				_canvasGroup.alpha = 1f;
				_canvasGroup.blocksRaycasts = true;
			}

			// Forgotten rather than set to what is written just below: a recycled node re-derives its
			// pivot and position from a style it has not resolved yet, so the next refresh has to write
			// all of it rather than compare against a previous occupant's.
			_transformWritten = false;

			if (_rectTransform != null)
			{
				_rectTransform.localScale = Vector3.one;
				_rectTransform.localEulerAngles = Vector3.zero;
			}
		}
	}

	internal sealed class RootHost : HostInstance
	{
		internal override HostKind Kind => HostKind.View;
	}

	internal class VisualHost : HostInstance
	{
		[NoAutoStaticsCleanup]
		private static readonly List<ResolvedShadow> s_noShadows = new();

		private RoundedRectGraphic? _graphic;
		private HoverBehaviour? _hover;
		private bool _pointerEnabled;
		private List<ResolvedShadow>? _shadows;
		private VisualChannels? _channels;
		private Color _fillColor = Color.clear;
		private Color _borderTop;
		private Color _borderRight;
		private Color _borderBottom;
		private Color _borderLeft;
		private Color _contentColor = Color.white;
		private RectTransform? _graphicChild;

		private Action<Color>? _setFill;
		private Action<Color>? _setBorderTop;
		private Action<Color>? _setBorderRight;
		private Action<Color>? _setBorderBottom;
		private Action<Color>? _setBorderLeft;
		private Action<Color>? _setContent;

		/// <summary>
		/// Whether this host draws anything <c>color</c> applies to — glyphs, or a sprite.
		/// </summary>
		/// <remarks>
		/// A plain <c>View</c> has no content to tint, and <c>color</c> is inherited, so nearly every
		/// node in the tree carries a value for it. Gating on this keeps the lookup off the nodes
		/// that would only throw the answer away.
		/// </remarks>
		protected virtual bool PaintsContent => false;

		/// <summary>Where a subclass puts the resolved <c>color</c>.</summary>
		protected virtual void PaintContentColor(Color color)
		{
		}

		internal override HostKind Kind => HostKind.View;

		/// <summary>Pressables keep a graphic even when transparent, so they stay raycastable.</summary>
		protected virtual bool AlwaysPaint => false;

		/// <summary>
		/// Builds the child that carries this host's *own* graphic — a text run's glyphs, an image's
		/// sprite — leaving the host's GameObject free to paint the box behind it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// uGUI gives a GameObject one <c>CanvasRenderer</c> and so one <see cref="Graphic"/>, and a
		/// host that draws something of its own needs two: the paint and the thing painted over it.
		/// The paint has to be the one on the host, because a canvas draws a parent before its
		/// children — a background on the child would sit *over* the content it is meant to sit
		/// behind.
		/// </para>
		/// <para>
		/// The rect is positioned by <see cref="AfterLayout"/> and follows the same contract every
		/// host rect has with Yoga: anchored to the parent's top-left, sized by hand. Anchors are
		/// measured against the parent's rect rather than its pivot, so `transform-origin` moving
		/// the host pivot cannot drag this with it.
		/// </para>
		/// </remarks>
		protected RectTransform BuildGraphicChild(string name)
		{
			var child = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
			child.SetParent(_rectTransform, worldPositionStays: false);
			child.anchorMin = new Vector2(0f, 1f);
			child.anchorMax = new Vector2(0f, 1f);
			child.pivot = new Vector2(0f, 1f);
			child.anchoredPosition = Vector2.zero;

			return _graphicChild = child;
		}

		/// <summary>
		/// Sits the graphic child in the content box — the node's box less its border and padding.
		/// </summary>
		/// <remarks>
		/// This is what gives `padding` and `border-width` their meaning on a text or image node:
		/// without it they would grow the box and leave the content covering all of it. Yoga hands a
		/// measure function the content width already, so the two agree.
		/// </remarks>
		internal override void AfterLayout()
		{
			base.AfterLayout();

			if (_graphicChild == null)
				return;

			var left = _yoga.LayoutBorderLeft + _yoga.LayoutPaddingLeft;
			var top = _yoga.LayoutBorderTop + _yoga.LayoutPaddingTop;
			var right = _yoga.LayoutBorderRight + _yoga.LayoutPaddingRight;
			var bottom = _yoga.LayoutBorderBottom + _yoga.LayoutPaddingBottom;

			// Yoga measures downward from the top; the rect's anchored position runs upward.
			var position = new Vector2(left, -top);

			var size = new Vector2(
				Mathf.Max(0f, _yoga.LayoutWidth - left - right),
				Mathf.Max(0f, _yoga.LayoutHeight - top - bottom));

			// Writing an unchanged rect still dirties it and queues its graphic for a rebuild, and
			// this runs on every layout the node takes part in.
			if (_graphicChild.anchoredPosition != position)
				_graphicChild.anchoredPosition = position;

			if (_graphicChild.sizeDelta != size)
				_graphicChild.sizeDelta = size;
		}

		internal override void EnablePointer()
		{
			if (_pointerEnabled)
				return;

			_pointerEnabled = true;
			EnsureGraphic().raycastTarget = true;

			// A Pressable already reports hover through its own behaviour; a plain View needs one
			// attaching, which is what lets `.card:hover .title` work without the card being a button.
			if (this is PressableHost)
				return;

			_hover = Ensure(_hover);
			_hover!.Host = this;
		}

		internal override void ApplyStyle(ComputedStyle style, in StyleContext ctx)
		{
			base.ApplyStyle(style, ctx);

			var fill = style.Color(PropId.BackgroundColor, Color.clear);
			var cascaded = StyleApplier.Borders(style, ctx);
			var gradient = style.Reference<Gradient>(PropId.BackgroundGradient);
			var shadows = style.Reference<ShadowList>(PropId.BoxShadow);
			var checker = style.Reference<Checker>(PropId.Checker);

			// The sheet carries the path; the texture is resolved and cached here, because a style is
			// applied far more often than a sheet is parsed.
			var texture = UiTextures.Resolve(style.Reference<string>(PropId.BackgroundImage));

			// Channels only exist once something asks to be animated, so a static node pays nothing.
			if (style.Has(PropId.TransitionDuration) || style.Has(PropId.AnimationName))
				_channels ??= new VisualChannels();

			// The widths are the cascade's; the colours are whatever the channels currently hold, so
			// that a fill or an edge mid-interpolation is not reset by an unrelated restyle.
			var border = cascaded.WithColors(_borderTop, _borderRight, _borderBottom, _borderLeft);

			var needsPaint = AlwaysPaint || fill.a > 0f || cascaded.Any || border.Any || gradient is not null
				|| shadows is { Count: > 0 } || checker is not null || texture != null || _fillColor.a > 0f;

			if (needsPaint)
			{
				var graphic = EnsureGraphic();
				var resolvedShadows = s_noShadows;

				if (shadows is { Count: > 0 })
				{
					_shadows ??= new List<ResolvedShadow>(shadows.Count);
					shadows.Resolve(_shadows, ctx);
					resolvedShadows = _shadows;
				}

				// Everything but the colours is configured directly; those go through channels so
				// `transition: background-color` and a keyframed edge have something to interpolate.
				graphic.Configure(
					StyleApplier.Corners(style, ctx),
					_fillColor,
					gradient,
					border,
					resolvedShadows,
					texture,
					checkerCell: checker is null ? 0f : ctx.Resolve(checker.CellSize),
					checkerLine: checker is null ? 0f : ctx.Resolve(checker.LineWidth),
					checkerColor: checker?.Color ?? Color.clear);

				_setFill ??= value =>
				{
					_fillColor = value;

					if (_graphic != null)
						_graphic.SetFillColor(value);
				};

				_setBorderTop ??= value => { _borderTop = value; RefreshBorderColors(); };
				_setBorderRight ??= value => { _borderRight = value; RefreshBorderColors(); };
				_setBorderBottom ??= value => { _borderBottom = value; RefreshBorderColors(); };
				_setBorderLeft ??= value => { _borderLeft = value; RefreshBorderColors(); };

				WriteCascaded(MotionChannelId.Fill, style, _setFill);
				WriteCascaded(MotionChannelId.BorderTop, style, _setBorderTop);
				WriteCascaded(MotionChannelId.BorderRight, style, _setBorderRight);
				WriteCascaded(MotionChannelId.BorderBottom, style, _setBorderBottom);
				WriteCascaded(MotionChannelId.BorderLeft, style, _setBorderLeft);
			}
			else if (_graphic != null)
			{
				_graphic.enabled = false;
			}

			if (!PaintsContent)
				return;

			_setContent ??= value =>
			{
				_contentColor = value;
				PaintContentColor(value);
			};

			WriteCascaded(MotionChannelId.Content, style, _setContent);
		}

		/// <summary>
		/// The colour half of the base class's cascade write: same transition channel, same rule that
		/// an animation running on a channel owns it outright.
		/// </summary>
		private void WriteCascaded(MotionChannelId channel, ComputedStyle style, Action<Color> apply)
		{
			if (Animation is not null && Animation.Owns(channel))
				return;

			var value = MotionChannels.ColorValue(channel, style);

			if (_channels is null)
			{
				apply(value);

				return;
			}

			_channels[channel].Set(value, style.TransitionFor(MotionChannels.PropFor(channel)), apply);
		}

		private void RefreshBorderColors()
		{
			if (_graphic != null)
				_graphic.SetBorderColors(_borderTop, _borderRight, _borderBottom, _borderLeft);
		}

		internal override void WriteMotion(MotionChannelId channel, Color value)
		{
			switch (channel)
			{
				case MotionChannelId.Fill: _setFill?.Invoke(value); break;
				case MotionChannelId.BorderTop: _setBorderTop?.Invoke(value); break;
				case MotionChannelId.BorderRight: _setBorderRight?.Invoke(value); break;
				case MotionChannelId.BorderBottom: _setBorderBottom?.Invoke(value); break;
				case MotionChannelId.BorderLeft: _setBorderLeft?.Invoke(value); break;
				case MotionChannelId.Content: _setContent?.Invoke(value); break;
			}
		}

		internal override void ReleaseMotion(ushort channels)
		{
			if (_channels is not null)
			{
				for (var channel = MotionChannels.FirstVisual; channel < MotionChannelId.Count; channel++)
				{
					if ((channels & MotionChannels.Bit(channel)) != 0)
						_channels[channel].Adopt(CurrentColor(channel));
				}
			}

			base.ReleaseMotion(channels);
		}

		private Color CurrentColor(MotionChannelId channel) => channel switch
		{
			MotionChannelId.Fill => _fillColor,
			MotionChannelId.BorderTop => _borderTop,
			MotionChannelId.BorderRight => _borderRight,
			MotionChannelId.BorderBottom => _borderBottom,
			MotionChannelId.BorderLeft => _borderLeft,
			_ => _contentColor,
		};

		internal override void StopMotion()
		{
			base.StopMotion();
			_channels?.Stop();
		}

		private RoundedRectGraphic EnsureGraphic()
		{
			if (_graphic == null)
			{
				_graphic = Ensure<RoundedRectGraphic>(null);
				_graphic.raycastTarget = AlwaysPaint;
				Ensure<GammaMaterialModifier>(null);
			}

			_graphic.enabled = true;

			return _graphic;
		}

		internal override void ResetForPool()
		{
			base.ResetForPool();

			// Channels reset rather than merely stop: a recycled node must snap to its first paint
			// instead of animating from whatever the previous occupant looked like.
			_channels?.Reset();
			_fillColor = Color.clear;
			_borderTop = default;
			_borderRight = default;
			_borderBottom = default;
			_borderLeft = default;
			_contentColor = Color.white;
			_shadows?.Clear();

			if (_graphic != null)
				_graphic.enabled = false;

			if (_graphicChild != null)
			{
				_graphicChild.anchoredPosition = Vector2.zero;
				_graphicChild.sizeDelta = Vector2.zero;
			}
		}
	}
}

using System;
using PrimeTween;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Easing curves a transition or a keyframe segment may name.
	/// </summary>
	internal enum Easing
	{
		Linear,
		InQuad,
		OutQuad,
		InOutQuad,
		OutCubic,
		OutBack,
	}

	/// <summary>
	/// Every value the framework knows how to interpolate, and the only things a <c>transition</c>
	/// or a <c>@keyframes</c> track may name.
	/// </summary>
	/// <remarks>
	/// Animatability used to be implicit — a property was animatable if somebody had hand-written a
	/// channel for it, and a stylesheet naming anything else snapped with no diagnostic. This enum
	/// is that fact written down. It doubles as the keyframe track key and as the bit position in
	/// <see cref="AnimationPlayer"/>'s ownership mask, which is why it stays small enough to fit
	/// one: twelve ids in a <c>ushort</c>.
	/// </remarks>
	internal enum MotionChannelId : byte
	{
		// Carried by every host: the rect's transform, and the CanvasGroup's alpha.
		Opacity,
		TranslateX,
		TranslateY,
		ScaleX,
		ScaleY,
		Rotation,

		// Carried only by a host that paints.
		Fill,
		BorderTop,
		BorderRight,
		BorderBottom,
		BorderLeft,
		Content,

		Count,
	}

	internal static class MotionChannels
	{
		/// <summary>
		/// The first channel that lives on <see cref="VisualChannels"/> rather than
		/// <see cref="HostChannels"/>. Everything at or above it interpolates a colour.
		/// </summary>
		internal const MotionChannelId FirstVisual = MotionChannelId.Fill;

		internal static ushort Bit(MotionChannelId channel) => (ushort)(1 << (int)channel);

		internal static bool IsColor(MotionChannelId channel) => channel >= FirstVisual;

		internal static bool IsTranslate(MotionChannelId channel) => channel is MotionChannelId.TranslateX or MotionChannelId.TranslateY;

		/// <summary>
		/// Whether a translation is a percentage of the node's own box. Null for a zero offset, which
		/// is the same distance in every unit and so leaves the channel's unit as it was.
		/// </summary>
		internal static bool? IsRelative(in StyleValue value) =>
			value.Kind == StyleValueKind.Length && value.AsLength() is { Value: not 0f } length
				? length.Unit == LengthUnit.Percent
				: null;

		internal static bool TryChannelFor(PropId id, out MotionChannelId channel)
		{
			switch (id)
			{
				case PropId.Opacity: channel = MotionChannelId.Opacity; return true;
				case PropId.TranslateX: channel = MotionChannelId.TranslateX; return true;
				case PropId.TranslateY: channel = MotionChannelId.TranslateY; return true;
				case PropId.ScaleX: channel = MotionChannelId.ScaleX; return true;
				case PropId.ScaleY: channel = MotionChannelId.ScaleY; return true;
				case PropId.Rotation: channel = MotionChannelId.Rotation; return true;
				case PropId.BackgroundColor: channel = MotionChannelId.Fill; return true;
				case PropId.BorderTopColor: channel = MotionChannelId.BorderTop; return true;
				case PropId.BorderRightColor: channel = MotionChannelId.BorderRight; return true;
				case PropId.BorderBottomColor: channel = MotionChannelId.BorderBottom; return true;
				case PropId.BorderLeftColor: channel = MotionChannelId.BorderLeft; return true;
				case PropId.Color: channel = MotionChannelId.Content; return true;
				default: channel = MotionChannelId.Count; return false;
			}
		}

		/// <summary>The property a channel is spelled as, for looking its transition up.</summary>
		internal static PropId PropFor(MotionChannelId channel) => channel switch
		{
			MotionChannelId.Opacity => PropId.Opacity,
			MotionChannelId.TranslateX => PropId.TranslateX,
			MotionChannelId.TranslateY => PropId.TranslateY,
			MotionChannelId.ScaleX => PropId.ScaleX,
			MotionChannelId.ScaleY => PropId.ScaleY,
			MotionChannelId.Rotation => PropId.Rotation,
			MotionChannelId.Fill => PropId.BackgroundColor,
			MotionChannelId.BorderTop => PropId.BorderTopColor,
			MotionChannelId.BorderRight => PropId.BorderRightColor,
			MotionChannelId.BorderBottom => PropId.BorderBottomColor,
			MotionChannelId.BorderLeft => PropId.BorderLeftColor,
			_ => PropId.Color,
		};

		/// <summary>
		/// The cascaded value of a float channel, with the same fallback its apply site uses.
		/// </summary>
		/// <remarks>
		/// An animation needs this for the endpoints CSS leaves implicit: a track that starts past
		/// 0%, or stops before 100%, runs from — or back to — whatever the cascade says.
		/// </remarks>
		internal static float FloatValue(MotionChannelId channel, ComputedStyle style, in StyleContext ctx) => channel switch
		{
			MotionChannelId.Opacity => style.Number(PropId.Opacity, 1f),
			MotionChannelId.TranslateX => ctx.Resolve(style.Length(PropId.TranslateX)),
			MotionChannelId.TranslateY => ctx.Resolve(style.Length(PropId.TranslateY)),
			MotionChannelId.ScaleX => style.Number(PropId.ScaleX, 1f),
			MotionChannelId.ScaleY => style.Number(PropId.ScaleY, 1f),
			_ => style.Number(PropId.Rotation, 0f),
		};

		internal static Color ColorValue(MotionChannelId channel, ComputedStyle style) => channel switch
		{
			MotionChannelId.Fill => style.Color(PropId.BackgroundColor, Color.clear),
			MotionChannelId.Content => style.Color(PropId.Color, Color.white),
			_ => style.Color(PropFor(channel), s_transparent),
		};

		/// <summary>
		/// Reads a keyframe stop as the number its channel wants. A translation is authored as a
		/// length and everything else as a bare number, so the unit maths happens here — once per
		/// <c>Sync</c> rather than once per frame.
		/// </summary>
		internal static float ToFloat(in StyleValue value, in StyleContext ctx) =>
			value.Kind == StyleValueKind.Length ? ctx.Resolve(value.AsLength()) : value.AsNumber();

		private static readonly Color s_transparent = new(0f, 0f, 0f, 0f);
	}

	internal readonly struct TransitionSpec
	{
		public bool IsInstant => Duration <= 0f;

		public readonly float Duration;
		public readonly float Delay;
		public readonly Easing Easing;

		public TransitionSpec(float duration, float delay, Easing easing)
		{
			Duration = duration;
			Delay = delay;
			Easing = easing;
		}

		/// <summary>
		/// Maps the tween's linear progress through this transition's timing function.
		/// </summary>
		/// <remarks>
		/// The tween always runs linearly and the curve is applied here, so a declared
		/// <c>cubic-bezier()</c> or <c>steps()</c> is evaluated by the same code as a named easing
		/// and a keyframe segment.
		/// </remarks>
		public float Ease(float progress) => EasingCurves.Evaluate(Easing, progress);
	}

	internal sealed class ColorChannel
	{
		private Color _current;
		private bool _initialised;
		private Tween _tween;

		public Color Current => _current;

		public void Set(Color target, in TransitionSpec spec, Action<Color> apply)
		{
			// The first paint always snaps: there is no previous value to move away from, and
			// animating from black would be a flash on every mount.
			if (!_initialised)
			{
				_initialised = true;
				_current = target;
				apply(target);

				return;
			}

			if (_current == target)
				return;

			if (spec.IsInstant)
			{
				_tween.Stop();
				_current = target;
				apply(target);

				return;
			}

			_tween.Stop();

			var from = _current;
			var timing = spec;
			_tween = Tween.Custom(0f, 1f, spec.Duration, progress =>
			{
				_current = Color.LerpUnclamped(from, target, timing.Ease(progress));
				apply(_current);
			}, PrimeTween.Ease.Linear, startDelay: spec.Delay, useUnscaledTime: TransitionTime.Unscaled);
		}

		/// <summary>
		/// Records a value written by something other than this channel — an animation frame — so
		/// the channel does not later interpolate away from a value it never saw.
		/// </summary>
		public void Adopt(Color value)
		{
			_tween.Stop();
			_initialised = true;
			_current = value;
		}

		public void Stop()
		{
			_tween.Stop();
		}

		public void Reset()
		{
			_tween.Stop();
			_initialised = false;
			_current = default;
		}
	}

	internal static class TransitionTime
	{
		public const bool Unscaled = true;
	}

	internal sealed class FloatChannel
	{
		public float Current { get; private set; }

		private bool _initialised;
		private Tween _tween;

		public void Set(float target, in TransitionSpec spec, Action<float> apply)
		{
			if (!_initialised)
			{
				_initialised = true;
				Current = target;
				apply(target);

				return;
			}

			if (Mathf.Approximately(Current, target))
				return;

			if (spec.IsInstant)
			{
				_tween.Stop();
				Current = target;
				apply(target);

				return;
			}

			_tween.Stop();

			var from = Current;
			var timing = spec;
			_tween = Tween.Custom(0f, 1f, spec.Duration, progress =>
			{
				Current = Mathf.LerpUnclamped(from, target, timing.Ease(progress));
				apply(Current);
			}, PrimeTween.Ease.Linear, startDelay: spec.Delay, useUnscaledTime: TransitionTime.Unscaled);
		}

		/// <inheritdoc cref="ColorChannel.Adopt"/>
		public void Adopt(float value)
		{
			_tween.Stop();
			_initialised = true;
			Current = value;
		}

		public void Stop()
		{
			_tween.Stop();
		}

		public void Reset()
		{
			_tween.Stop();
			_initialised = false;
			Current = 0f;
		}
	}

	/// <summary>
	/// The channels every host kind carries. Separate from <see cref="VisualChannels"/> because a
	/// transform is a property of the rect and opacity a property of the CanvasGroup, so text and
	/// image nodes animate both — while only a painted node has a fill to interpolate.
	/// </summary>
	internal sealed class HostChannels
	{
		private readonly FloatChannel[] _byId = new FloatChannel[(int)MotionChannels.FirstVisual];

		public HostChannels()
		{
			for (var i = 0; i < _byId.Length; i++)
				_byId[i] = new FloatChannel();
		}

		public FloatChannel this[MotionChannelId channel] => _byId[(int)channel];

		public void Stop()
		{
			for (var i = 0; i < _byId.Length; i++)
				_byId[i].Stop();
		}

		public void Reset()
		{
			for (var i = 0; i < _byId.Length; i++)
				_byId[i].Reset();
		}
	}

	internal sealed class VisualChannels
	{
		private const int Offset = (int)MotionChannels.FirstVisual;

		private readonly ColorChannel[] _byId = new ColorChannel[(int)MotionChannelId.Count - Offset];

		public VisualChannels()
		{
			for (var i = 0; i < _byId.Length; i++)
				_byId[i] = new ColorChannel();
		}

		public ColorChannel this[MotionChannelId channel] => _byId[(int)channel - Offset];

		public void Stop()
		{
			for (var i = 0; i < _byId.Length; i++)
				_byId[i].Stop();
		}

		public void Reset()
		{
			for (var i = 0; i < _byId.Length; i++)
				_byId[i].Reset();
		}
	}
}

using System;
using PrimeTween;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>Which way round each iteration of an animation runs.</summary>
	public enum AnimationDirection
	{
		Normal,
		Reverse,
		Alternate,
		AlternateReverse,
	}

	/// <summary>Whether an animation holds its endpoints outside its running time.</summary>
	public enum AnimationFill
	{
		None,
		Forwards,
		Backwards,
		Both,
	}

	/// <summary>
	/// Everything a rule said about how an animation should run — the clip it names aside.
	/// </summary>
	/// <remarks>
	/// <see cref="RestartEquals"/> is load-bearing: the rule is that an animation restarts when, and
	/// only when, the spec that started it changed. That is what CSS means by an animation restarting
	/// on a change of <c>animation-name</c>, and it is what makes
	/// <c>.x:hover { animation: … }</c> start on hover and stop again on leaving. <c>Paused</c> is
	/// deliberately excluded from it, because toggling <c>animation-play-state</c> must suspend a
	/// running animation rather than start it over.
	/// </remarks>
	internal readonly struct AnimationSpec
	{
		public bool IsNone => NameId == 0 || Duration <= 0f || Iterations == 0;

		public readonly int NameId;
		public readonly float Duration;
		public readonly float Delay;
		public readonly Easing Easing;

		/// <summary>Iteration count, with -1 standing for <c>infinite</c>.</summary>
		public readonly int Iterations;

		public readonly AnimationDirection Direction;
		public readonly AnimationFill Fill;
		public readonly bool Paused;

		public AnimationSpec(int nameId, float duration, float delay, Easing easing, int iterations,
			AnimationDirection direction, AnimationFill fill, bool paused)
		{
			NameId = nameId;
			Duration = duration;
			Delay = delay;
			Easing = easing;
			Iterations = iterations;
			Direction = direction;
			Fill = fill;
			Paused = paused;
		}

		public bool RestartEquals(in AnimationSpec other) =>
			NameId == other.NameId
			&& Duration.Equals(other.Duration)
			&& Delay.Equals(other.Delay)
			&& Easing == other.Easing
			&& Iterations == other.Iterations
			&& Direction == other.Direction
			&& Fill == other.Fill;

		/// <summary>The part of <see cref="Delay"/> that actually waits, which is none of a negative one.</summary>
		public float StartDelay => Mathf.Max(0f, Delay);

		/// <summary>
		/// How far into the clip the animation begins. A negative <c>animation-delay</c> means the
		/// animation started that long before the node did, so it opens partway through — including
		/// partway through a later iteration, and, under <c>alternate</c>, possibly a reversed one.
		/// </summary>
		public float Seek => Mathf.Max(0f, -Delay);

		/// <summary>
		/// How long one run takes, for holding an exiting node on screen. An infinite animation
		/// contributes nothing rather than forever — a node that never finishes exiting is worse
		/// than one that snaps.
		/// </summary>
		/// <remarks>
		/// A negative delay subtracts, because that much of the run is already behind it by the time
		/// the node mounts; a seek past the end leaves nothing to wait for at all.
		/// </remarks>
		public float TotalDuration =>
			Iterations < 0 ? 0f : Mathf.Max(0f, Duration * Iterations + Delay);
	}

	/// <summary>
	/// What an <see cref="AnimationPlayer"/> writes its sampled values through.
	/// </summary>
	/// <remarks>
	/// Implemented by the host, because the setters an animation has to reach are the same cached
	/// ones a transition writes through — the value has to bypass the cascade either way, since a
	/// <see cref="ComputedStyle"/> is interned and shared and so cannot be re-resolved per frame.
	/// </remarks>
	internal interface IMotionTarget
	{
		void WriteMotion(MotionChannelId channel, float value);

		void WriteMotion(MotionChannelId channel, Color value);

		/// <summary>
		/// Hands the named channels back to the cascade, once an animation that owned them has
		/// finished without holding its final value.
		/// </summary>
		void ReleaseMotion(ushort channels);

		/// <summary>
		/// Tells the node whether a translation channel's values are percentages of its own box.
		/// </summary>
		void SetRelative(MotionChannelId channel, bool relative);
	}

	/// <summary>
	/// Runs one <c>@keyframes</c> clip on one node.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A single PrimeTween handle drives progress from 0 to 1 with <see cref="Ease.Linear"/>, and
	/// every track is sampled off that one clock. Easing is applied per *segment* inside
	/// <see cref="OnProgress"/> rather than handed to the tween, because that is where CSS puts it:
	/// <c>animation-timing-function</c> describes the interval between two keyframes, not the run as
	/// a whole. One tween per node also means a three-property animation costs one handle rather
	/// than three that could drift apart.
	/// </para>
	/// <para>
	/// While it runs the player *owns* the channels it writes, and the apply sites skip them.
	/// Animations outrank normal declarations in the CSS cascade, so this is replacement rather than
	/// the additive layer <c>ElementRef</c> motion uses — there is nothing additive about a colour.
	/// </para>
	/// </remarks>
	internal sealed class AnimationPlayer
	{
		private struct ResolvedStop
		{
			public float Offset;
			public float Number;
			public Color Color;
			public Easing Easing;
			public bool HasEasing;
		}

		private struct ResolvedTrack
		{
			public MotionChannelId Channel;
			public ResolvedStop[] Stops;
		}

		private AnimationSpec _spec;
		private KeyframesClip? _clip;
		private ResolvedTrack[]? _tracks;
		private IMotionTarget? _target;
		private Tween _tween;
		private ushort _owned;

		internal bool Owns(MotionChannelId channel) => (_owned & MotionChannels.Bit(channel)) != 0;

		internal ushort OwnedChannels => _owned;

		/// <summary>
		/// Brings the player in line with what the style now says, starting, stopping or leaving a
		/// run alone as the spec demands.
		/// </summary>
		internal void Sync(in AnimationSpec spec, KeyframesClip? clip, ComputedStyle style, in StyleContext ctx, IMotionTarget target)
		{
			if (spec.IsNone || clip is null)
			{
				Stop();

				return;
			}

			// This exact animation has already been started on this node — whether it is still
			// running or has since finished. Either way there is nothing to do but pass on a change
			// of play state.
			//
			// Covering the finished case is what stops a `fill: none` animation looping forever: the
			// restyle that hands its channels back reaches this method again, and a player that had
			// forgotten its clip would take that as a cue to start over.
			if (_clip is not null && ReferenceEquals(_clip, clip) && _spec.RestartEquals(spec))
			{
				ApplyPlayState(spec.Paused);
				_spec = spec;

				return;
			}

			_tween.Stop();

			_spec = spec;
			_clip = clip;
			_target = target;
			_owned = clip.ChannelMask;

			Resolve(clip, style, ctx);
			ApplyUnits(clip, target);

			// The first keyframe is written before the tween is even created, so the node never shows
			// a frame of the cascaded value it is about to animate away from. With a delay that is
			// what `backwards` and `both` ask for, and without one it is simply the animation
			// starting on the frame the rule began applying.
			if (spec.StartDelay <= 0f || spec.Fill is AnimationFill.Backwards or AnimationFill.Both)
				OnProgress(0f);

			var alternates = spec.Direction is AnimationDirection.Alternate or AnimationDirection.AlternateReverse;

			_tween = Tween.Custom(
				0f, 1f, spec.Duration, OnProgress,
				Ease.Linear,
				cycles: spec.Iterations,
				cycleMode: alternates ? CycleMode.Yoyo : CycleMode.Restart,
				startDelay: spec.StartDelay,
				useUnscaledTime: TransitionTime.Unscaled);

			_tween.OnComplete(this, static self => self.Finish());

			// A negative delay is served by winding the clock forward, not by waiting: PrimeTween
			// takes no negative `startDelay` (it clamps one to zero), but it will seek, and seeking
			// is the truer reading anyway — it walks whole cycles, so an animation that opens three
			// iterations in lands in the right one, facing the right way under `alternate`.
			//
			// After OnComplete is registered, because a seek past the end of a finite animation
			// completes it there and then, and that has to reach Finish() like any other ending.
			if (spec.Seek > 0f)
				_tween.elapsedTimeTotal = spec.Seek;

			ApplyPlayState(spec.Paused);
		}

		private void ApplyPlayState(bool paused)
		{
			if (_tween.isAlive)
				_tween.isPaused = paused;
		}

		/// <summary>
		/// Turns the shared clip into the numbers this node will actually interpolate: lengths
		/// resolved against its rem size, and the endpoints CSS leaves implicit filled in from the
		/// cascade. Done once per run so the per-frame path is only a search and a lerp.
		/// </summary>
		private void Resolve(KeyframesClip clip, ComputedStyle style, in StyleContext ctx)
		{
			if (_tracks is null || _tracks.Length != clip.Tracks.Length)
				_tracks = new ResolvedTrack[clip.Tracks.Length];

			for (var t = 0; t < clip.Tracks.Length; t++)
			{
				var track = clip.Tracks[t];
				var stops = track.Stops;
				var isColor = MotionChannels.IsColor(track.Channel);

				var padStart = stops[0].Offset > 0f;
				var padEnd = stops[stops.Length - 1].Offset < 1f;
				var count = stops.Length + (padStart ? 1 : 0) + (padEnd ? 1 : 0);

				var resolved = _tracks[t].Stops;

				if (resolved is null || resolved.Length != count)
					resolved = new ResolvedStop[count];

				var index = 0;

				if (padStart)
					resolved[index++] = Basis(track.Channel, 0f, isColor, style, ctx);

				for (var s = 0; s < stops.Length; s++)
				{
					var stop = stops[s];

					resolved[index++] = new ResolvedStop
					{
						Offset = stop.Offset,
						Number = isColor ? 0f : MotionChannels.ToFloat(stop.Value, ctx),
						Color = isColor ? stop.Value.AsColor() : default,
						Easing = stop.Easing,
						HasEasing = stop.HasEasing,
					};
				}

				if (padEnd)
				{
					// The synthesised tail inherits the last real stop's easing, so the segment
					// running back to the cascaded value is shaped like the ones before it.
					var tail = Basis(track.Channel, 1f, isColor, style, ctx);
					tail.Easing = resolved[index - 1].Easing;
					tail.HasEasing = resolved[index - 1].HasEasing;
					resolved[index] = tail;
				}

				_tracks[t].Channel = track.Channel;
				_tracks[t].Stops = resolved;
			}
		}

		/// <remarks>
		/// A translation track takes the unit of its first nonzero stop. Mixing percentages and
		/// lengths within one track is not supported: the track is read in that one unit throughout.
		/// </remarks>
		private static void ApplyUnits(KeyframesClip clip, IMotionTarget target)
		{
			foreach (var track in clip.Tracks)
			{
				if (!MotionChannels.IsTranslate(track.Channel))
					continue;

				foreach (var stop in track.Stops)
				{
					if (MotionChannels.IsRelative(stop.Value) is { } relative)
					{
						target.SetRelative(track.Channel, relative);

						break;
					}
				}
			}
		}

		private static ResolvedStop Basis(MotionChannelId channel, float offset, bool isColor, ComputedStyle style, in StyleContext ctx) =>
			new()
			{
				Offset = offset,
				Number = isColor ? 0f : MotionChannels.FloatValue(channel, style, ctx),
				Color = isColor ? MotionChannels.ColorValue(channel, style) : default,
			};

		private void OnProgress(float progress)
		{
			var tracks = _tracks;
			var target = _target;

			if (tracks is null || target is null)
				return;

			// Yoyo already walks the clock back for `alternate`; the two `reverse` modes flip which
			// end of the clip that clock is read from.
			var t = _spec.Direction is AnimationDirection.Reverse or AnimationDirection.AlternateReverse
				? 1f - progress
				: progress;

			for (var i = 0; i < tracks.Length; i++)
			{
				var stops = tracks[i].Stops;
				var index = Segment(stops, t);
				var from = stops[index];
				var to = stops[index + 1];

				var span = to.Offset - from.Offset;
				var local = span > 0f ? Mathf.Clamp01((t - from.Offset) / span) : 0f;

				local = EasingCurves.Evaluate(from.HasEasing ? from.Easing : _spec.Easing, local);

				// Unclamped, because --out-back overshoots its endpoints on purpose.
				if (MotionChannels.IsColor(tracks[i].Channel))
					target.WriteMotion(tracks[i].Channel, Color.LerpUnclamped(from.Color, to.Color, local));
				else
					target.WriteMotion(tracks[i].Channel, Mathf.LerpUnclamped(from.Number, to.Number, local));
			}
		}

		/// <summary>The left end of the segment the given progress falls in. Never the last stop.</summary>
		private static int Segment(ResolvedStop[] stops, float t)
		{
			var low = 0;
			var high = stops.Length - 1;

			while (low < high)
			{
				var mid = (low + high + 1) >> 1;

				if (stops[mid].Offset <= t) low = mid;
				else high = mid - 1;
			}

			return low < stops.Length - 1 ? low : stops.Length - 2;
		}

		private void Finish()
		{
			// `forwards` and `both` keep the final keyframe on screen, which means keeping the
			// channels too: handing them back would snap to the cascaded value on the next frame.
			if (_spec.Fill is AnimationFill.Forwards or AnimationFill.Both)
				return;

			var released = _owned;
			_owned = 0;

			// The clip and spec stay: they are the record of what this node has already played, and
			// dropping them here is what would make the release restart the animation.
			_target?.ReleaseMotion(released);
		}

		/// <summary>
		/// Ends the animation because the rule that named it no longer applies, handing back whatever
		/// it still owns. Forgetting the spec is the point — should the same rule apply again later,
		/// CSS says the animation plays again.
		/// </summary>
		internal void Stop()
		{
			_tween.Stop();

			var released = _owned;

			_owned = 0;
			_clip = null;
			_spec = default;

			if (released != 0)
				_target?.ReleaseMotion(released);
		}

		internal void Reset()
		{
			_tween.Stop();
			_owned = 0;
			_clip = null;
			_tracks = null;
			_target = null;
			_spec = default;
		}
	}
}

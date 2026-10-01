using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// One stop on one track: where in the animation it sits, what the value is there, and the
	/// easing that carries the segment *starting* here to the next stop.
	/// </summary>
	/// <remarks>
	/// The easing belongs to the stop rather than to the clip because that is what CSS means by
	/// <c>animation-timing-function</c> inside a keyframe block: it describes the interval that
	/// begins at that keyframe, not the animation as a whole. It arrives as an ordinary declaration
	/// among the stop's other properties, so honouring it costs a field.
	/// </remarks>
	internal readonly struct KeyframeStop
	{
		public readonly float Offset;
		public readonly StyleValue Value;
		public readonly Easing Easing;
		public readonly bool HasEasing;

		public KeyframeStop(float offset, StyleValue value, Easing easing, bool hasEasing)
		{
			Offset = offset;
			Value = value;
			Easing = easing;
			HasEasing = hasEasing;
		}
	}

	/// <summary>
	/// Every stop a single channel passes through, sorted by offset.
	/// </summary>
	/// <remarks>
	/// A track per channel rather than a list of whole keyframe blocks: sampling is then a binary
	/// search through a short sorted array, and interpolation only ever has to handle one value
	/// kind. It is the shape <see cref="ComputedStyle"/> already uses, for the same reason.
	/// </remarks>
	internal readonly struct KeyframeTrack
	{
		public readonly MotionChannelId Channel;
		public readonly KeyframeStop[] Stops;

		public KeyframeTrack(MotionChannelId channel, KeyframeStop[] stops)
		{
			Channel = channel;
			Stops = stops;
		}
	}

	/// <summary>
	/// A parsed <c>@keyframes</c> block: the tracks it drives, keyed by the interned name a
	/// stylesheet refers to it by.
	/// </summary>
	/// <remarks>
	/// A clip is immutable and shared by every node that names it. Anything per-node — resolved
	/// lengths, the implicit endpoints taken from the cascade — belongs on the player instead.
	/// </remarks>
	internal sealed class KeyframesClip
	{
		[NoAutoStaticsCleanup]
		internal static readonly KeyframesClip[] s_none = Array.Empty<KeyframesClip>();

		public readonly int NameId;
		public readonly KeyframeTrack[] Tracks;

		/// <summary>Which channels this clip writes, for the player's ownership mask.</summary>
		public readonly ushort ChannelMask;

		public KeyframesClip(int nameId, KeyframeTrack[] tracks)
		{
			NameId = nameId;
			Tracks = tracks;

			for (var i = 0; i < tracks.Length; i++)
				ChannelMask |= MotionChannels.Bit(tracks[i].Channel);
		}
	}

	/// <summary>
	/// Assembles <see cref="KeyframesClip"/>s out of the stops a stylesheet declared, which arrive
	/// one whole block at a time and have to be transposed into one list per channel.
	/// </summary>
	internal sealed class KeyframesBuilder
	{
		private readonly Dictionary<MotionChannelId, List<KeyframeStop>> _tracks = new();

		internal void Add(MotionChannelId channel, float offset, StyleValue value, Easing easing, bool hasEasing)
		{
			if (!_tracks.TryGetValue(channel, out var stops))
			{
				stops = new List<KeyframeStop>();
				_tracks[channel] = stops;
			}

			// A stop repeated for the same channel is last-wins, as a duplicate declaration is
			// everywhere else in the cascade.
			for (var i = 0; i < stops.Count; i++)
			{
				if (!Mathf.Approximately(stops[i].Offset, offset))
					continue;

				stops[i] = new KeyframeStop(offset, value, easing, hasEasing);

				return;
			}

			stops.Add(new KeyframeStop(offset, value, easing, hasEasing));
		}

		internal bool IsEmpty => _tracks.Count == 0;

		internal KeyframesClip Build(int nameId)
		{
			var tracks = new KeyframeTrack[_tracks.Count];
			var index = 0;

			foreach (var pair in _tracks)
			{
				var stops = pair.Value;

				// Insertion sort: a keyframe block has a handful of stops, and unlike List.Sort this
				// is stable — which matters because equal offsets were already de-duplicated above
				// and must keep the order they were declared in.
				for (var i = 1; i < stops.Count; i++)
				{
					var stop = stops[i];
					var j = i - 1;

					while (j >= 0 && stops[j].Offset > stop.Offset)
					{
						stops[j + 1] = stops[j];
						j--;
					}

					stops[j + 1] = stop;
				}

				tracks[index++] = new KeyframeTrack(pair.Key, stops.ToArray());
			}

			return new KeyframesClip(nameId, tracks);
		}

		internal void Clear() => _tracks.Clear();
	}
}

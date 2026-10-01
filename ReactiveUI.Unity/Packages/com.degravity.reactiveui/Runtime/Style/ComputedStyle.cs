using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// One resolved property.
	/// </summary>
	public readonly struct PropEntry
	{
		public readonly PropId Id;
		public readonly StyleValue Value;

		public PropEntry(PropId id, StyleValue value)
		{
			Id = id;
			Value = value;
		}
	}

	/// <summary>
	/// A node's fully resolved style, as a sparse array sorted by <see cref="PropId"/>.
	/// </summary>
	/// <remarks>
	/// Deliberately not a class with a nullable field per property. That shape made adding one
	/// property a five-place edit — the field, a merge, an equality comparer, a hash, and the
	/// applier — and roughly a quarter of the old styling layer was that duplication. Here a
	/// property costs one enum entry plus one registry line; merge, equality and hashing are all
	/// loops over the array. It is also the shape a ScriptableObject can serialise directly, so the
	/// baked stylesheet and the runtime form are the same thing.
	/// </remarks>
	[NoAutoStaticsCleanup]
	public sealed class ComputedStyle
	{
		public static readonly ComputedStyle Empty = new(Array.Empty<PropEntry>());

		private static PropEntry[] s_sortScratch = new PropEntry[64];

		public int Count => _entries.Length;

		internal ComputedStyle(PropEntry[] entries) => _entries = entries;

		private readonly PropEntry[] _entries;

		public bool TryGet(PropId id, out StyleValue value)
		{
			var low = 0;
			var high = _entries.Length - 1;

			while (low <= high)
			{
				var mid = (low + high) >> 1;
				var candidate = _entries[mid].Id;

				if (candidate == id)
				{
					value = _entries[mid].Value;

					return true;
				}

				if (candidate < id) low = mid + 1;
				else high = mid - 1;
			}

			value = default;

			return false;
		}

		public PropEntry this[int index] => _entries[index];

		public StyleLength Length(PropId id, StyleLength fallback = default) => TryGet(id, out var value) ? value.AsLength() : fallback;

		public Color Color(PropId id, Color fallback) => TryGet(id, out var value) ? value.AsColor() : fallback;

		public float Number(PropId id, float fallback) => TryGet(id, out var value) ? value.AsNumber() : fallback;

		public int Keyword(PropId id, int fallback) => TryGet(id, out var value) ? value.AsKeyword() : fallback;

		public T? Reference<T>(PropId id) where T : class => TryGet(id, out var value) ? value.Reference as T : null;

		public bool Has(PropId id) => TryGet(id, out _);

		/// <summary>
		/// The last property <see cref="StyleApplier.ApplyLayout"/> writes to Yoga. Every layout
		/// property sits at or below it, which is what makes the comparison below a prefix walk —
		/// see the ordering PropId declares.
		/// </summary>
		private const PropId LastLayoutProp = PropId.ColumnGap;

		/// <summary>
		/// Whether two styles would write the same box to Yoga.
		/// </summary>
		/// <remarks>
		/// Every Yoga setter is a P/Invoke through a <c>SafeHandle</c>, and applying a style writes
		/// some forty-five of them. A restyle driven by a pointer moving changes <c>background-color</c>
		/// and nothing else on nearly every node it reaches, so rewriting the whole layout box to the
		/// values it already holds is the most expensive thing an apply could do for no effect.
		/// <para>
		/// Exact rather than a hash: entries are sorted by id and de-duplicated, so the layout
		/// properties are a prefix of each array and a lockstep walk answers the question outright.
		/// A hash would be smaller but would trade a guarantee for a collision, and a collision here
		/// is a node silently keeping the wrong geometry.
		/// </para>
		/// </remarks>
		internal static bool LayoutEquivalent(ComputedStyle a, ComputedStyle b)
		{
			if (ReferenceEquals(a, b))
				return true;

			var i = 0;
			var j = 0;

			while (i < a._entries.Length && a._entries[i].Id <= LastLayoutProp
				&& j < b._entries.Length && b._entries[j].Id <= LastLayoutProp)
			{
				if (a._entries[i].Id != b._entries[j].Id || !a._entries[i].Value.Equals(b._entries[j].Value))
					return false;

				i++;
				j++;
			}

			// Both runs have to end together: one style carrying a layout property the other does not
			// is a difference, even though everything before it matched.
			var endOfA = i >= a._entries.Length || a._entries[i].Id > LastLayoutProp;
			var endOfB = j >= b._entries.Length || b._entries[j].Id > LastLayoutProp;

			return endOfA && endOfB;
		}

		internal TransitionSpec TransitionFor(PropId id)
		{
			if (!TryGet(PropId.TransitionDuration, out var duration))
				return default;

			if (TryGet(PropId.TransitionProperty, out var property))
			{
				var target = (PropId)property.AsKeyword();

				// `transition-property: transform` resolves to TranslateX, which stands for the whole
				// transform group — otherwise naming it would animate the translation and snap the
				// rotation, which is not what anyone writing CSS means by it.
				var matches = target == PropId.None
					|| target == id
					|| (target == PropId.TranslateX && IsTransformChannel(id))
					|| (target == PropId.BorderTopColor && IsBorderColour(id));

				if (!matches)
					return default;
			}

			var delay = TryGet(PropId.TransitionDelay, out var d) ? d.AsNumber() : 0f;
			var easing = TryGet(PropId.TransitionTimingFunction, out var e)
				? (Easing)e.AsKeyword()
				: Easing.InOutQuad;

			return new TransitionSpec(duration.AsNumber(), delay, easing);
		}

		private static bool IsTransformChannel(PropId id) => id is PropId.TranslateX or PropId.TranslateY or PropId.Scale or PropId.Rotation;

		// `border-color` is four longhands, and BorderTopColor stands for the group the way
		// TranslateX stands for the transform — naming one side and snapping the other three is
		// not what anyone writing the shorthand means by it.
		private static bool IsBorderColour(PropId id) =>
			id is PropId.BorderTopColor or PropId.BorderRightColor or PropId.BorderBottomColor or PropId.BorderLeftColor;

		/// <summary>
		/// The <c>animation</c> this style declares, if any.
		/// </summary>
		/// <remarks>
		/// Unlike <see cref="TransitionFor"/> there is no per-property question to ask: an animation
		/// drives whatever channels its clip happens to carry, so the name is the whole lookup.
		/// </remarks>
		internal AnimationSpec AnimationFor()
		{
			if (!TryGet(PropId.AnimationName, out var name))
				return default;

			var nameId = name.AsKeyword();

			if (nameId == 0)
				return default;

			var duration = TryGet(PropId.AnimationDuration, out var d) ? d.AsNumber() : 0f;
			var delay = TryGet(PropId.AnimationDelay, out var y) ? y.AsNumber() : 0f;
			var easing = TryGet(PropId.AnimationTimingFunction, out var e) ? (Easing)e.AsKeyword() : Easing.InOutQuad;
			var iterations = TryGet(PropId.AnimationIterationCount, out var c) ? Mathf.RoundToInt(c.AsNumber()) : 1;
			var direction = TryGet(PropId.AnimationDirection, out var r) ? (AnimationDirection)r.AsKeyword() : AnimationDirection.Normal;
			var fill = TryGet(PropId.AnimationFillMode, out var f) ? (AnimationFill)f.AsKeyword() : AnimationFill.None;
			var paused = TryGet(PropId.AnimationPlayState, out var p) && p.AsKeyword() != 0;

			// The delay keeps its sign. A negative one is not a delay at all — CSS means "start as
			// though this much of it had already run" — and AnimationPlayer turns it into a seek.
			return new AnimationSpec(nameId, duration, delay, easing, iterations, direction, fill, paused);
		}

		/// <summary>
		/// How long a node has to be held on screen for its <c>:exit</c> styling to be seen —
		/// whichever of its transition and its animation runs longer.
		/// </summary>
		internal float MaxMotionDuration()
		{
			var transition = 0f;

			if (TryGet(PropId.TransitionDuration, out var duration))
			{
				var delay = TryGet(PropId.TransitionDelay, out var d) ? d.AsNumber() : 0f;
				transition = duration.AsNumber() + Mathf.Max(0f, delay);
			}

			return Mathf.Max(transition, AnimationFor().TotalDuration);
		}

		internal static ComputedStyle FromEntries(List<PropEntry> entries)
		{
			var count = entries.Count;

			if (count == 0)
				return Empty;

			if (s_sortScratch.Length < count)
				s_sortScratch = new PropEntry[Mathf.NextPowerOfTwo(count)];

			var scratch = s_sortScratch;

			for (var i = 0; i < count; i++)
				scratch[i] = entries[i];

			// An insertion sort in place, rather than the index sort with a capturing comparison this
			// replaces — that one allocated an index array, a closure, and the sort helper's comparer
			// wrapper on every call, and this is called per node per render for anything carrying an
			// inline style. Insertion sort is stable, so a later declaration still follows an earlier
			// one of the same id and the dedupe below keeps the right winner without a tie-break. The
			// counts here are the declarations on one node, where it is also simply the faster sort.
			for (var i = 1; i < count; i++)
			{
				var entry = scratch[i];
				var j = i - 1;

				while (j >= 0 && (ushort)scratch[j].Id > (ushort)entry.Id)
				{
					scratch[j + 1] = scratch[j];
					j--;
				}

				scratch[j + 1] = entry;
			}

			// Counted first so the result is allocated at its exact size — the array is the one thing
			// here that has to survive the call.
			var distinct = 0;

			for (var i = 0; i < count; i++)
			{
				if (i + 1 < count && scratch[i + 1].Id == scratch[i].Id)
					continue;

				distinct++;
			}

			var result = new PropEntry[distinct];
			var next = 0;

			for (var i = 0; i < count; i++)
			{
				// Later entries override earlier ones, so keep only the last of each run.
				if (i + 1 < count && scratch[i + 1].Id == scratch[i].Id)
					continue;

				result[next++] = scratch[i];
			}

			return new ComputedStyle(result);
		}
	}
}

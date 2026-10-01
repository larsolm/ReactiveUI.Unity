using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace ReactiveUI
{
	internal sealed class Scheduler
	{
		[NoAutoStaticsCleanup]
		private static readonly DepthComparer s_byDepth = new();

		internal bool HasDirty => _dirty.Count > 0;
		internal bool HasEffects => _effects.Count > 0;

		private readonly HashSet<RenderInstance> _dirty = new();
		private readonly List<RenderInstance> _ordered = new();
		private readonly Queue<Action> _effects = new();

		private sealed class DepthComparer : IComparer<RenderInstance>
		{
			public int Compare(RenderInstance? a, RenderInstance? b) => a!._depth.CompareTo(b!._depth);
		}

		internal void MarkDirty(RenderInstance instance)
		{
			if (instance._unmounted) return;

			instance._dirty = true;
			_dirty.Add(instance);
		}

		internal void Forget(RenderInstance instance)
		{
			_dirty.Remove(instance);
		}

		internal void Enqueue(Action effect)
		{
			_effects.Enqueue(effect);
		}

		internal List<RenderInstance> TakeDirty()
		{
			_ordered.Clear();

			foreach (var instance in _dirty)
			{
				if (!instance._unmounted)
					_ordered.Add(instance);
			}

			_dirty.Clear();
			_ordered.Sort(s_byDepth);

			return _ordered;
		}

		internal void FlushEffects()
		{
			while (_effects.Count > 0)
			{
				var effect = _effects.Dequeue();

				try
				{
					effect();
				}
				catch (Exception ex)
				{
					Debug.LogException(ex);
				}
			}
		}

		internal void Clear()
		{
			_dirty.Clear();
			_ordered.Clear();
			_effects.Clear();
		}
	}
}

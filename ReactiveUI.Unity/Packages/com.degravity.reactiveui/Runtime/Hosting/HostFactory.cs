using System.Collections.Generic;
using ReactiveUI.Yoga;
using UnityEngine;

namespace ReactiveUI
{
	internal sealed class HostFactory
	{
		private const int MaxPooledPerKind = 128;

		private bool _shuttingDown;

		private readonly Dictionary<HostKind, Stack<HostInstance>> _pools = new();
		private readonly YogaConfig _yogaConfig;
		private readonly Transform _poolParent;

		internal HostFactory(YogaConfig yogaConfig)
		{
			_yogaConfig = yogaConfig;

			// Parked off-scene and never saved, so pooled nodes cost nothing to render and do not
			// leak into the user's hierarchy.
			var parent = new GameObject("ReactiveUIPool") { hideFlags = HideFlags.DontSave };
			parent.SetActive(false);
			_poolParent = parent.transform;
		}

		internal HostInstance Rent(HostKind kind)
		{
			if (_pools.TryGetValue(kind, out var stack) && stack.Count > 0)
			{
				var pooled = stack.Pop();
				pooled._gameObject.SetActive(true);

				return pooled;
			}

			return Create(kind);
		}

		internal void BeginShutdown()
		{
			_shuttingDown = true;
		}

		internal void Release(HostInstance host)
		{
			host._yoga.RemoveAll();
			host.ResetForPool();

			if (_shuttingDown)
			{
				DestroyObject(host._gameObject);
				return;
			}

			if (!_pools.TryGetValue(host.Kind, out var stack))
			{
				_pools[host.Kind] = stack = new Stack<HostInstance>();
			}

			if (stack.Count >= MaxPooledPerKind)
			{
				DestroyObject(host._gameObject);
				return;
			}

			host._gameObject.SetActive(false);
			host._gameObject.transform.SetParent(_poolParent, worldPositionStays: false);
			stack.Push(host);
		}

		private HostInstance Create(HostKind kind)
		{
			using var marker = UiMarkers.CreateHost[(int)kind].Auto();

			var gameObject = new GameObject(HostInstance.NameOf(kind), typeof(RectTransform));
			var yoga = new YogaNode(_yogaConfig);

			switch (kind)
			{
				case HostKind.Text:
					{
						var host = new TextHost();
						host.Initialize(gameObject, yoga);
						host.Build();

						return host;
					}
				case HostKind.Image:
					{
						var host = new ImageHost();
						host.Initialize(gameObject, yoga);
						host.Build();

						return host;
					}
				case HostKind.Pressable:
					{
						var host = new PressableHost();
						host.Initialize(gameObject, yoga);
						host.Bind(gameObject.AddComponent<PressableBehaviour>());

						return host;
					}
				case HostKind.Scroll:
					{
						var host = new ScrollHost();
						host.Initialize(gameObject, yoga);
						host.Build();

						return host;
					}
				default:
					{
						var host = new VisualHost();
						host.Initialize(gameObject, yoga);

						return host;
					}
			}
		}

		/// <summary>
		/// Destroys at end of frame in play mode, and at once in the editor, where deferred destruction
		/// is refused — a runtime driven from an edit-mode tool or test tears down the same way.
		/// </summary>
		private static void DestroyObject(Object target)
		{
			if (Application.isPlaying)
				Object.Destroy(target);
			else
				Object.DestroyImmediate(target);
		}

		internal void Dispose()
		{
			foreach (var stack in _pools.Values)
			{
				while (stack.Count > 0)
				{
					var host = stack.Pop();
					if (host._gameObject != null)
						DestroyObject(host._gameObject);
				}
			}

			_pools.Clear();

			if (_poolParent != null)
				DestroyObject(_poolParent.gameObject);
		}
	}
}

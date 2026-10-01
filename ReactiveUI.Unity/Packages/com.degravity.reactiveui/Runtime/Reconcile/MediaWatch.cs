using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// One runtime's live media environment, and the event a hook attaches to.
	/// </summary>
	/// <remarks>
	/// An object per runtime rather than a static tracker like <see cref="InputModalityTracker"/>. Input
	/// modality is genuinely process-wide — one player, one pair of hands — but a viewport belongs to one
	/// container, and two runtimes in a scene answer differently. A static one would hand the second
	/// runtime the first one's answer, with no error and no symptom beyond a layout that is subtly wrong.
	/// </remarks>
	internal sealed class MediaWatch
	{
		private readonly Dictionary<string, MediaCondition> _conditions = new(StringComparer.Ordinal);
		private readonly List<string> _diagnostics = new();

		internal MediaEnvironment Environment { get; private set; }

		internal event Action? Changed;

		/// <summary>Publishes a new environment, raising <see cref="Changed"/> only when it moved.</summary>
		internal void Publish(in MediaEnvironment environment)
		{
			if (Environment.Equals(environment))
				return;

			Environment = environment;
			Changed?.Invoke();
		}

		/// <summary>
		/// The compiled form of a query string, parsed the first time it is seen and shared thereafter.
		/// </summary>
		/// <remarks>
		/// Cached on the runtime rather than on the hook, because the same breakpoint string is written at
		/// a dozen call sites and parsing CSS per component per mount is not a cost worth paying for a
		/// value that never changes. Keyed on the literal text, which for a string constant in a render
		/// method is stable and cheap to hash.
		/// </remarks>
		internal MediaCondition Condition(string query)
		{
			if (_conditions.TryGetValue(query, out var cached))
				return cached;

			_diagnostics.Clear();

			var condition = MediaQueryCompiler.CompileCondition(query, _diagnostics);

			for (var i = 0; i < _diagnostics.Count; i++)
				Debug.LogWarning($"[ReactiveUI] {_diagnostics[i]}");

			_conditions[query] = condition;

			return condition;
		}
	}
}

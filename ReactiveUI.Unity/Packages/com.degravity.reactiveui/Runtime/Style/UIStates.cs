using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>
	/// The pseudo-class registry. Built-ins occupy the low bits; anything else a project needs is
	/// registered by name and addressed the same way, which is what makes <c>:enter</c> and a
	/// game-specific <c>:scoring</c> the same kind of thing.
	/// </summary>
	[NoAutoStaticsCleanup]
	internal static class UiStates
	{
		private const int MaxBits = 64;

		private static readonly Dictionary<string, int> s_bits = new(StringComparer.Ordinal);
		private static readonly List<string> s_names = new();

		// Interaction.
		internal static readonly StateBit s_hover = Register("hover");
		internal static readonly StateBit s_active = Register("active");
		internal static readonly StateBit s_focus = Register("focus");
		internal static readonly StateBit s_focusVisible = Register("focus-visible");
		internal static readonly StateBit s_disabled = Register("disabled");

		// Lifecycle. Set by the framework around mount and unmount.
		internal static readonly StateBit s_enter = Register("enter");
		internal static readonly StateBit s_exit = Register("exit");

		// The tree's own root. A built-in rather than something UiRuntime registers on construction,
		// so that `:root` resolves the same whether or not a runtime happens to exist yet — the
		// editor parses sheets at import time, long before one does.
		internal static readonly StateBit s_root = Register("root");

		// Structural pseudo-classes (`:first-child`, `:nth-child()`, `:empty`, …) are not state bits.
		// They are their own selector kinds, matched against the node's position when the reconciler
		// places it.

		/// <summary>
		/// Every registered pseudo-class name, in registration order.
		/// </summary>
		/// <remarks>
		/// Handed to the CSS parser so it treats these as real pseudo-classes rather than errors. It
		/// only knows the standard ones, and a name it does not know invalidates the selector — which
		/// a flat rule survives on its raw source text, but a nested one does not, because its selector
		/// is a string synthesised while resolving <c>&amp;</c> and has no source text to fall back on.
		/// Registering the whole list means a project's own state nests correctly the moment it exists.
		/// </remarks>
		public static IReadOnlyList<string> Names => s_names;

		/// <summary>
		/// Looks a pseudo-class up without registering it.
		/// </summary>
		/// <remarks>
		/// What the parser asks, so that an unknown <c>:name</c> is a diagnostic rather than a bit
		/// nothing will ever set. Registering on sight made every typo a rule that parsed cleanly and
		/// silently matched nothing.
		/// </remarks>
		public static bool TryGet(string name, out StateBit bit)
		{
			if (s_bits.TryGetValue(name, out var index))
			{
				bit = new StateBit(index);

				return true;
			}

			bit = StateBit.None;

			return false;
		}

		/// <summary>
		/// Registers a pseudo-class, or returns the existing bit if it is already known.
		/// </summary>
		public static StateBit Register(string name)
		{
			if (s_bits.TryGetValue(name, out var index))
			{
				return new StateBit(index);
			}

			if (s_names.Count >= MaxBits)
			{
				throw new InvalidOperationException($"Cannot register pseudo-class ':{name}' — all {MaxBits} state bits are in use.");
			}

			index = s_names.Count;
			s_names.Add(name);
			s_bits[name] = index;

			return new StateBit(index);
		}
	}
}

using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace ReactiveUI
{
	internal interface IPropsArena
	{
		void Reset();
	}

	/// <summary>
	/// The props declared this pass for one props type.
	/// </summary>
	/// <remarks>
	/// One typed array per <typeparamref name="TProps"/>, which is the whole point: a heterogeneous
	/// store would have to box, and props are declared once per element per render. Reset with the
	/// element arena.
	/// </remarks>
	[NoAutoStaticsCleanup]
	internal static partial class PropsPool<TProps>
		where TProps : struct
	{
		private static TProps[] s_items = new TProps[64];
		private static int s_count;

		static PropsPool()
		{
			PropsArenas.Register(new Resetter());
		}

		internal static int Add(in TProps props)
		{
			if (s_count == s_items.Length)
				Array.Resize(ref s_items, s_items.Length * 2);

			s_items[s_count] = props;

			return s_count++;
		}

		internal static ref TProps At(int slot) => ref s_items[slot];

		private sealed class Resetter : IPropsArena
		{
			public void Reset()
			{
				Array.Clear(s_items, 0, s_count);
				s_count = 0;
			}
		}
	}

	/// <summary>
	/// Every <see cref="PropsPool{TProps}"/> the process has touched, so the pass can reset them all.
	/// </summary>
	/// <remarks>
	/// A static constructor registers each arena the first time its type is used, so this list is
	/// built by the renders themselves rather than declared anywhere.
	/// </remarks>
	[NoAutoStaticsCleanup]
	internal static partial class PropsArenas
	{
		private static readonly List<IPropsArena> s_arenas = new(32);

		internal static void Register(IPropsArena arena) => s_arenas.Add(arena);

		[OnEnteringPlayMode]
		internal static void ResetAll()
		{
			for (var i = 0; i < s_arenas.Count; i++)
				s_arenas[i].Reset();
		}
	}
}

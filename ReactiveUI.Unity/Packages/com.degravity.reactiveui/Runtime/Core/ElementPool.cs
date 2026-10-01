using System;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>
	/// One declared element, as data. Elements live here for the length of a render pass and are
	/// dropped wholesale at the end of it, which is what makes declaring one free.
	/// </summary>
	internal struct ElementNode
	{
		/// <summary>
		/// Which element type this is — a component id, or a negated <see cref="HostKind"/>.
		/// </summary>
		public int TypeId;

		/// <summary>
		/// Index into the props arena for this element's props type, or -1 when propless.
		/// </summary>
		public int PropsSlot;

		public int ChildRun;
		public int ChildCount;
		public int ChildCapacity;

		/// <summary>
		/// Hosts only: the classes this node matches on, and the handle bound to it.
		/// </summary>
		public ClassSet Class;

		public ElementRef? Ref;

		/// <summary>
		/// Index into the inline arena, or -1 when this node declares no inline style.
		/// </summary>
		public int InlineSlot;

		/// <summary>
		/// A context provider node only: the value it makes visible below itself.
		/// </summary>
		public object? Provided;
	}

	/// <summary>
	/// The element tree for the pass currently rendering.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Nodes are addressed by index and never by reference. Returning a <c>ref ElementNode</c> would
	/// be a trap: rendering a child appends nodes, which can resize the backing array, and a caller
	/// holding a ref across that would silently write into the abandoned copy. Every accessor here
	/// therefore takes an index. <see cref="InlineArena"/> is the one place a ref does escape, and it
	/// is paged for exactly this reason.
	/// </para>
	/// <para>
	/// Index 0 is reserved as "no element", so a default <see cref="Element"/> is empty rather than being
	/// the first node ever allocated.
	/// </para>
	/// </remarks>
	[AutoStaticsCleanup]
	internal static partial class ElementPool
	{
		private static ElementNode[] s_nodes = new ElementNode[512];
		private static int s_nodeCount = 1;

		private static int[] s_childRuns = new int[2048];
		private static int s_runCount;

		internal static int PeakNodes { get; private set; }

		internal static int NewNode(int typeId, int propsSlot)
		{
			if (s_nodeCount == s_nodes.Length)
				Array.Resize(ref s_nodes, s_nodes.Length * 2);

			var node = s_nodeCount++;

			s_nodes[node] = new ElementNode { TypeId = typeId, PropsSlot = propsSlot, InlineSlot = -1 };

			if (s_nodeCount > PeakNodes)
				PeakNodes = s_nodeCount;

			return node;
		}

		internal static int TypeIdOf(int node) => s_nodes[node].TypeId;

		internal static int PropsSlotOf(int node) => s_nodes[node].PropsSlot;

		internal static ClassSet ClassOf(int node) => s_nodes[node].Class;

		internal static ElementRef? RefOf(int node) => s_nodes[node].Ref;

		internal static int InlineSlotOf(int node) => s_nodes[node].InlineSlot;

		internal static void SetInlineSlot(int node, int slot) => s_nodes[node].InlineSlot = slot;

		internal static ref TProps Props<TProps>(int node)
			where TProps : struct
		{
			return ref PropsPool<TProps>.At(s_nodes[node].PropsSlot);
		}

		internal static bool HasInline(int node) => node != 0 && s_nodes[node].InlineSlot >= 0;

		internal static object? ProvidedOf(int node) => s_nodes[node].Provided;

		internal static void SetProvided(int node, object? value) => s_nodes[node].Provided = value;

		internal static int ChildCountOf(int node) => node == 0 ? 0 : s_nodes[node].ChildCount;

		internal static void SetIdentity(int node, ClassSet className, ElementRef? elementRef)
		{
			s_nodes[node].Class = className;
			s_nodes[node].Ref = elementRef;
		}

		internal static int ChildAt(int node, int index) => s_childRuns[s_nodes[node].ChildRun + index];

		internal static void AddChild(int parent, int child)
		{
			if (parent == 0 || child == 0)
				return;

			if (s_nodes[parent].ChildCount == s_nodes[parent].ChildCapacity)
				Grow(parent, s_nodes[parent].ChildCapacity == 0 ? 4 : s_nodes[parent].ChildCapacity * 2);

			s_childRuns[s_nodes[parent].ChildRun + s_nodes[parent].ChildCount] = child;
			s_nodes[parent].ChildCount++;
		}

		/// <summary>
		/// Grows a node's child run up front, for a caller that already knows how many follow.
		/// </summary>
		internal static void EnsureCapacity(int node, int capacity)
		{
			if (node != 0 && capacity > s_nodes[node].ChildCapacity)
				Grow(node, capacity);
		}

		/// <summary>
		/// Moves a node's children to a longer run. The old run is abandoned rather than freed —
		/// the whole arena is reclaimed at the end of the pass, so there is nothing to give back to.
		/// </summary>
		private static void Grow(int node, int capacity)
		{
			var run = Reserve(capacity);
			var previous = s_nodes[node].ChildRun;

			for (var i = 0; i < s_nodes[node].ChildCount; i++)
				s_childRuns[run + i] = s_childRuns[previous + i];

			s_nodes[node].ChildRun = run;
			s_nodes[node].ChildCapacity = capacity;
		}

		private static int Reserve(int capacity)
		{
			while (s_runCount + capacity > s_childRuns.Length)
				Array.Resize(ref s_childRuns, s_childRuns.Length * 2);

			var start = s_runCount;
			s_runCount += capacity;

			return start;
		}

		/// <summary>
		/// Drops every element declared this pass.
		/// </summary>
		/// <remarks>
		/// Cleared rather than merely rewound: a node holds an <see cref="ElementRef"/> and a
		/// <see cref="ClassSet"/> that can carry a spill array, and leaving those behind would pin
		/// them for as long as the arena lives.
		/// </remarks>
		internal static void Reset()
		{
			InlineArena.Reset();
			Array.Clear(s_nodes, 0, s_nodeCount);
			s_nodeCount = 1;
			s_runCount = 0;
		}
	}
}

using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>
	/// A component: one type with one <see cref="Render"/>, whose props are
	/// <typeparamref name="TProps"/>.
	/// </summary>
	/// <remarks>
	/// Declare the props as a <c>readonly record struct</c>: the compiler writes the equality, and the
	/// <see cref="IEquatable{T}"/> constraint keeps the memo comparison off
	/// <see cref="ValueType.Equals(object)"/>, which would box both sides and walk the fields
	/// reflectively. To decide equality yourself, write <c>public bool Equals(TProps other)</c> inside
	/// the props struct.
	/// </remarks>
	public interface IComponent<TProps> : IElement
		where TProps : struct, IEquatable<TProps>
	{
		/// <summary>
		/// Produces this component's subtree.
		/// </summary>
		/// <remarks>
		/// Props arrive as a parameter rather than being read off the element, because the element is
		/// gone by the time a re-render runs: the committed props live on the instance.
		/// </remarks>
		Element Render(in TProps props);
	}

	/// <summary>
	/// A component with no props — a screen, or anything that reads everything it needs from hooks.
	/// </summary>
	public interface IComponent : IElement
	{
		Element Render();
	}

	/// <summary>
	/// What the reconciler knows about one component type: how to build its instance, and what to
	/// call it in a diagnostic.
	/// </summary>
	internal interface IComponentType
	{
		string Name { get; }

		RenderInstance CreateInstance();
	}

	/// <summary>
	/// Every component type the process has declared, keyed by the dense id stored on an arena node.
	/// </summary>
	internal static partial class ComponentTypes
	{
		// ComponentType<T>.s_id is a generic static that never re-initialises, so the ids it holds
		// have to stay addressable.
		[NoAutoStaticsCleanup]
		private static readonly List<IComponentType?> s_types = new(64) { null };

		internal static int Register(IComponentType type)
		{
			s_types.Add(type);

			return s_types.Count - 1;
		}

		internal static IComponentType Of(int id) => s_types[id]!;
	}

	/// <summary>
	/// The id and the instance factory for one <c>(component, props)</c> pair.
	/// </summary>
	/// <remarks>
	/// A static generic, so the id is assigned once by the runtime the first time that component is
	/// declared and costs a static field read ever after.
	/// </remarks>
	internal static class ComponentType<TComponent, TProps>
		where TComponent : struct, IComponent<TProps>
		where TProps : struct, IEquatable<TProps>
	{
		internal static readonly int s_id = ComponentTypes.Register(new Registration());

		private sealed class Registration : IComponentType
		{
			public string Name => typeof(TComponent).Name;

			public RenderInstance CreateInstance() => new RenderInstance<TComponent, TProps>();
		}
	}

	/// <inheritdoc cref="ComponentType{TComponent, TProps}"/>
	internal static class ComponentType<TComponent>
		where TComponent : struct, IComponent
	{
		internal static readonly int s_id = ComponentTypes.Register(new Registration());

		private sealed class Registration : IComponentType
		{
			public string Name => typeof(TComponent).Name;

			public RenderInstance CreateInstance() => new ProplessRenderInstance<TComponent>();
		}
	}

	/// <summary>
	/// How an arena node says what it is. Components take positive ids from
	/// <see cref="ComponentTypes"/>; everything the framework owns takes a fixed negative one.
	/// </summary>
	internal static partial class TypeIds
	{
		// Kept in step with ClassTable, which persists.
		[NoAutoStaticsCleanup]
		private static readonly Dictionary<int, int> s_internedNames = new(64);

		internal const int Fragment = -100;
		internal const int Portal = -101;

		/// <summary>A context provider — a group node that also carries a value.</summary>
		internal const int Provider = -103;

		/// <summary>A children list built by a helper, with no element above it.</summary>
		internal const int Detached = -102;

		internal static int Host(HostKind kind) => -1 - (int)kind;

		internal static bool IsHost(int typeId) => typeId < 0 && typeId > -100;

		internal static HostKind KindOf(int typeId) => (HostKind)(-typeId - 1);

		internal static bool IsGroup(int typeId) => typeId is Fragment or Portal or Provider;

		/// <summary>
		/// The interned class name a node of this type answers to, for the type selector.
		/// </summary>
		/// <remarks>
		/// Cached per type id, because the previous design reached this through
		/// <c>element.GetType().Name</c> once per host per reconcile — and on Mono that getter is not
		/// guaranteed to hand back the same string twice.
		/// </remarks>
		internal static int InternedName(int typeId)
		{
			if (s_internedNames.TryGetValue(typeId, out var id))
				return id;

			id = ClassTable.Intern(NameOf(typeId));
			s_internedNames[typeId] = id;

			return id;
		}

		internal static string NameOf(int typeId)
		{
			if (IsHost(typeId))
				return KindOf(typeId).ToString();

			return typeId switch
			{
				Fragment => nameof(Fragment),
				Portal => nameof(Portal),
				Provider => "ContextProvider",
				Detached => "ElementList",
				_ => ComponentTypes.Of(typeId).Name,
			};
		}
	}
}

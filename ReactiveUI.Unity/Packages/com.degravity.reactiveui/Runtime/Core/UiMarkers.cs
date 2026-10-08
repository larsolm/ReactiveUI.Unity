using System;
using Unity.Profiling;
using Unity.Scripting.LifecycleManagement;

namespace ReactiveUI
{
	/// <summary>The profiler markers ReactiveUI records its work under.</summary>
	[NoAutoStaticsCleanup]
	internal static class UiMarkers
	{
		internal static readonly ProfilerMarker Input = new("ReactiveUI.Input");
		internal static readonly ProfilerMarker Environment = new("ReactiveUI.Environment");
		internal static readonly ProfilerMarker Reconcile = new("ReactiveUI.Reconcile");
		internal static readonly ProfilerMarker Overlay = new("ReactiveUI.Overlay");
		internal static readonly ProfilerMarker LayoutCalculate = new("ReactiveUI.Layout.Calculate");
		internal static readonly ProfilerMarker LayoutApply = new("ReactiveUI.Layout.Apply");
		internal static readonly ProfilerMarker Lifecycle = new("ReactiveUI.Lifecycle");
		internal static readonly ProfilerMarker Effects = new("ReactiveUI.Effects");
		internal static readonly ProfilerMarker Preload = new("ReactiveUI.Preload");

		internal static readonly ProfilerMarker Match = new("ReactiveUI.Style.Match");
		internal static readonly ProfilerMarker ComputeStyle = new("ReactiveUI.Style.Compute");
		internal static readonly ProfilerMarker ActivateSheet = new("ReactiveUI.Style.ActivateSheet");
		internal static readonly ProfilerMarker ReadSheet = new("ReactiveUI.Style.ReadSheet");
		internal static readonly ProfilerMarker LoadFont = new("ReactiveUI.Load.Font");
		internal static readonly ProfilerMarker WarmFont = new("ReactiveUI.Load.WarmFont");
		internal static readonly ProfilerMarker MeasureText = new("ReactiveUI.Text.Measure");
		internal static readonly ProfilerMarker LoadTexture = new("ReactiveUI.Load.Texture");

		/// <summary>Creating a host's GameObject and components, by <see cref="HostKind"/>.</summary>
		internal static readonly ProfilerMarker[] CreateHost = PerKind("ReactiveUI.Host.Create");

		/// <summary>Applying an element's props to a host, by <see cref="HostKind"/>.</summary>
		internal static readonly ProfilerMarker[] ApplyProps = PerKind("ReactiveUI.Host.ApplyProps");

		/// <summary>Applying a computed style to a host, by <see cref="HostKind"/>.</summary>
		internal static readonly ProfilerMarker[] ApplyStyle = PerKind("ReactiveUI.Host.ApplyStyle");

		private static ProfilerMarker[] PerKind(string prefix)
		{
			var names = Enum.GetNames(typeof(HostKind));
			var markers = new ProfilerMarker[names.Length];

			for (var i = 0; i < names.Length; i++)
				markers[i] = new ProfilerMarker(prefix + "." + names[i]);

			return markers;
		}
	}

	/// <summary>The marker a component's own render is recorded under, named after the component.</summary>
	[NoAutoStaticsCleanup]
	internal static class RenderMarker<TComponent>
	{
		internal static readonly ProfilerMarker Marker = new("ReactiveUI.Render." + typeof(TComponent).Name);
	}
}

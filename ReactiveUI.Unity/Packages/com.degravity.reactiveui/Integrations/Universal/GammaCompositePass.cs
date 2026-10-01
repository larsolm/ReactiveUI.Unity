using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;

namespace ReactiveUI.Universal
{
	/// <summary>
	/// Draws the gamma canvases into a plain 8-bit target, then composites that target over the camera
	/// colour in sRGB.
	/// </summary>
	/// <remarks>
	/// The target is <c>UNorm</c> rather than <c>SRGB</c>, so the hardware blends the stored sRGB values
	/// directly, as a browser does. It ends up holding premultiplied colour and coverage, which the
	/// composite lays over the scene after encoding the scene to sRGB, then decodes the result.
	/// </remarks>
	[NoAutoStaticsCleanup]
	internal sealed class GammaCompositePass : ScriptableRenderPass
	{
		private const string DrawName = "ReactiveUI.Gamma.Draw";
		private const string CopyName = "ReactiveUI.Gamma.Copy";
		private const string CompositeName = "ReactiveUI.Gamma.Composite";

		private static readonly ShaderTagId[] s_shaderTags = { new("SRPDefaultUnlit"), new("UniversalForward"), new("Universal2D") };
		private static readonly int s_canvasId = Shader.PropertyToID("_ReactiveUIGammaCanvas");

		private readonly Material _material;

		internal LayerMask Layers { get; set; }
		internal GammaCompositePass(Material material)
		{
			_material = material;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			var resources = frameData.Get<UniversalResourceData>();
			var target = resources.activeColorTexture;

			if (!target.IsValid() || resources.isActiveTargetBackBuffer)
				return;

			var cameraData = frameData.Get<UniversalCameraData>();
			var renderingData = frameData.Get<UniversalRenderingData>();

			var canvasDescription = renderGraph.GetTextureDesc(target);
			canvasDescription.name = DrawName;
			canvasDescription.format = GraphicsFormat.R8G8B8A8_UNorm;
			canvasDescription.msaaSamples = MSAASamples.None;
			canvasDescription.clearBuffer = true;
			canvasDescription.clearColor = Color.clear;
			canvasDescription.filterMode = FilterMode.Point;
			var canvas = renderGraph.CreateTexture(canvasDescription);

			Draw(renderGraph, cameraData, renderingData, canvas);

			var sceneDescription = renderGraph.GetTextureDesc(target);
			sceneDescription.name = CopyName;
			sceneDescription.clearBuffer = false;
			var scene = renderGraph.CreateTexture(sceneDescription);

			renderGraph.AddBlitPass(target, scene, Vector2.one, Vector2.zero, passName: CopyName);

			Composite(renderGraph, scene, canvas, target);
		}

		private void Draw(RenderGraph renderGraph, UniversalCameraData cameraData, UniversalRenderingData renderingData, TextureHandle canvas)
		{
			var description = new RendererListDesc(s_shaderTags, renderingData.cullResults, cameraData.camera)
			{
				sortingCriteria = SortingCriteria.CommonTransparent,
				renderQueueRange = RenderQueueRange.all,
				layerMask = Layers,
				stateBlock = new RenderStateBlock(RenderStateMask.Depth) { depthState = new DepthState(false, CompareFunction.Always) },
			};

			var list = renderGraph.CreateRendererList(description);

			using var builder = renderGraph.AddRasterRenderPass<DrawData>(DrawName, out var data);
			data.List = list;
			data.View = cameraData.GetViewMatrix();
			data.Projection = cameraData.GetProjectionMatrix();
			builder.UseRendererList(list);
			builder.SetRenderAttachment(canvas, 0, AccessFlags.Write);
			builder.AllowGlobalStateModification(true);
			builder.SetRenderFunc(static (DrawData pass, RasterGraphContext context) =>
			{
				context.cmd.SetViewProjectionMatrices(pass.View, pass.Projection);
				context.cmd.DrawRendererList(pass.List);
			});
		}

		private void Composite(RenderGraph renderGraph, TextureHandle scene, TextureHandle canvas, TextureHandle target)
		{
			using var builder = renderGraph.AddRasterRenderPass<CompositeData>(CompositeName, out var data);
			data.Scene = scene;
			data.Canvas = canvas;
			data.Material = _material;
			builder.UseTexture(scene);
			builder.UseTexture(canvas);
			builder.SetRenderAttachment(target, 0, AccessFlags.Write);
			builder.SetRenderFunc(static (CompositeData pass, RasterGraphContext context) =>
			{
				pass.Material!.SetTexture(s_canvasId, (RTHandle)pass.Canvas);
				Blitter.BlitTexture(context.cmd, pass.Scene, new Vector4(1f, 1f, 0f, 0f), pass.Material, 0);
			});
		}

		private sealed class DrawData
		{
			public RendererListHandle List;
			public Matrix4x4 View;
			public Matrix4x4 Projection;
		}

		private sealed class CompositeData
		{
			public TextureHandle Scene;
			public TextureHandle Canvas;
			public Material? Material;
		}
	}
}

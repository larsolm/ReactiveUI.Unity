using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ReactiveUI.Universal
{
	/// <summary>
	/// Draws every <see cref="GammaCanvas"/> on a camera in gamma space and composites it over the
	/// frame the way a browser would.
	/// </summary>
	/// <remarks>
	/// The renderer's own layer mask must leave out <see cref="_layers"/>, or the canvases are drawn a
	/// second time, linearly, by the renderer itself. The composite runs after post-processing, so the
	/// UI is neither bloomed nor blurred, and only on cameras a gamma canvas is attached to.
	/// </remarks>
	[DisallowMultipleRendererFeature("ReactiveUI Gamma")]
	public sealed class ReactiveUIGammaFeature : ScriptableRendererFeature
	{
		public const string ShaderName = "Hidden/ReactiveUI/GammaComposite";

		[SerializeField]
		private Shader? _shader = null;

		[Tooltip("Layers the gamma canvases sit on. Leave these out of the renderer's own layer mask.")]
		[SerializeField]
		private LayerMask _layers = 1 << 5;

		private Material? _material;
		private GammaCompositePass? _pass;

		public override void Create()
		{
			CoreUtils.Destroy(_material);
			_material = null;

			if (_shader == null)
				_shader = Shader.Find(ShaderName);

			if (_shader == null)
				return;

			_material = CoreUtils.CreateEngineMaterial(_shader);
			_pass = new GammaCompositePass(_material)
			{
				renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
				requiresIntermediateTexture = true,
			};
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (_pass == null || !GammaCanvas.AnyFor(renderingData.cameraData.camera))
				return;

			_pass.Layers = _layers;
			renderer.EnqueuePass(_pass);
		}

		protected override void Dispose(bool disposing)
		{
			CoreUtils.Destroy(_material);
			_material = null;
		}
	}
}

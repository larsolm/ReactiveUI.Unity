using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ReactiveUI.Universal
{
	/// <summary>
	/// A URP renderer feature that draws each <see cref="GammaCanvas"/> in gamma space and composites it over the frame.
	/// </summary>
	/// <remarks>
	/// Exclude the canvas layers from the renderer's own layer mask, or the canvases are drawn twice.
	/// The composite runs after post-processing.
	/// </remarks>
	[DisallowMultipleRendererFeature("ReactiveUI Gamma")]
	public sealed class ReactiveUIGammaFeature : ScriptableRendererFeature
	{
		internal const string ShaderName = "Hidden/ReactiveUI/GammaComposite";

		[SerializeField]
		private Shader? _shader = null;

		[Tooltip("Layers the gamma canvases sit on. Leave these out of the renderer's own layer mask.")]
		[SerializeField]
		private LayerMask _layers = 1 << 5;

		private Material? _material;
		private GammaCompositePass? _pass;

		/// <inheritdoc/>
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

		/// <inheritdoc/>
		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (_pass == null || !GammaCanvas.AnyFor(renderingData.cameraData.camera))
				return;

			_pass.Layers = _layers;
			renderer.EnqueuePass(_pass);
		}

		/// <inheritdoc/>
		protected override void Dispose(bool disposing)
		{
			CoreUtils.Destroy(_material);
			_material = null;
		}
	}
}

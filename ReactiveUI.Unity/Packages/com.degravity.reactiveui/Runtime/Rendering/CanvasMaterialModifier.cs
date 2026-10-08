using UnityEngine;
using UnityEngine.UI;

namespace ReactiveUI
{
	/// <summary>
	/// Swaps its graphic's material for the twin its root canvas asks for: a projected twin under a
	/// <see cref="ProjectedCanvas"/>, a gamma twin under a <see cref="GammaCanvas"/>.
	/// </summary>
	/// <remarks>
	/// Rides uGUI's own material pipeline, so it works the same for a box, an image and a TMP run,
	/// and composes with masking: it is added after the graphic, so it receives whatever stencil
	/// material the graphic built and twins that.
	/// </remarks>
	[DisallowMultipleComponent]
	internal sealed class CanvasMaterialModifier : MonoBehaviour, IMaterialModifier
	{
		private Graphic? _graphic;

		Material IMaterialModifier.GetModifiedMaterial(Material baseMaterial)
		{
			if (_graphic == null)
				TryGetComponent(out _graphic);

			var canvas = _graphic != null ? _graphic.canvas : null;

			if (canvas == null)
				return baseMaterial;

			var root = canvas.rootCanvas;

			if (ProjectedCanvas.TryGet(root, out var projected))
				return projected.TwinFor(baseMaterial);

			return GammaCanvas.Contains(root) ? GammaMaterials.For(baseMaterial) : baseMaterial;
		}
	}
}

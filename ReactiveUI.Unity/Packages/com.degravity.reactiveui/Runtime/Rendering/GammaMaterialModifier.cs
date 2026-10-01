using UnityEngine;
using UnityEngine.UI;

namespace ReactiveUI
{
	/// <summary>
	/// Swaps its graphic's material for the gamma twin while the graphic sits under a
	/// <see cref="GammaCanvas"/>.
	/// </summary>
	/// <remarks>
	/// Rides uGUI's own material pipeline, so it works the same for a box, an image and a TMP run,
	/// and composes with masking: it is added after the graphic, so it receives whatever stencil
	/// material the graphic built and twins that.
	/// </remarks>
	[DisallowMultipleComponent]
	internal sealed class GammaMaterialModifier : MonoBehaviour, IMaterialModifier
	{
		private Graphic? _graphic;

		Material IMaterialModifier.GetModifiedMaterial(Material baseMaterial)
		{
			if (_graphic == null)
				TryGetComponent(out _graphic);

			var canvas = _graphic != null ? _graphic.canvas : null;

			return canvas != null && GammaCanvas.Contains(canvas.rootCanvas)
				? GammaMaterials.For(baseMaterial)
				: baseMaterial;
		}
	}
}

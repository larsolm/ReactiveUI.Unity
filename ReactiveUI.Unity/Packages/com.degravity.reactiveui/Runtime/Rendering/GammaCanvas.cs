using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ReactiveUI
{
	/// <summary>
	/// Blends this canvas in gamma space, the way a browser does, so CSS colours and alphas land on
	/// the same pixels they do in a web design while the rest of a linear project stays linear.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A linear project blends every translucent pixel in linear light, which is physically right for
	/// a scene and wrong for a design authored in a browser: a 3.5% wash over near-black comes out
	/// several times brighter than the mock-up. No shader can change that, because blending into an
	/// sRGB target is fixed-function. So the canvas is drawn into a plain 8-bit target instead, with
	/// materials that output sRGB values, and composited over the scene afterwards.
	/// </para>
	/// <para>
	/// The canvas becomes Screen Space - Camera on <see cref="Camera.main"/>, followed across scene
	/// loads, and sits on the configured layer. The camera's URP renderer needs
	/// <c>ReactiveUIGammaFeature</c> (from the <c>ReactiveUI.Universal</c> integration) to draw that
	/// layer, and its own layer mask must leave the layer out, or the canvas is drawn twice.
	/// </para>
	/// </remarks>
	[AutoStaticsCleanup]
	public sealed partial class GammaCanvas : MonoBehaviour
	{
		private static readonly HashSet<Canvas> s_canvases = new();

		[SerializeField]
		private Canvas _canvas = null!;

		internal static bool AnyFor(Camera camera)
		{
			foreach (var canvas in s_canvases)
			{
				if (canvas.isActiveAndEnabled && canvas.worldCamera == camera)
					return true;
			}

			return false;
		}

		internal static bool Contains(Canvas canvas) => s_canvases.Contains(canvas);

		private void OnEnable()
		{
			s_canvases.Add(_canvas);
			RefreshMaterials();
		}

		private void OnDisable()
		{
			s_canvases.Remove(_canvas);
			RefreshMaterials();
		}

		private void RefreshMaterials()
		{
			foreach (var graphic in GetComponentsInChildren<Graphic>(includeInactive: true))
				graphic.SetMaterialDirty();
		}
	}
}

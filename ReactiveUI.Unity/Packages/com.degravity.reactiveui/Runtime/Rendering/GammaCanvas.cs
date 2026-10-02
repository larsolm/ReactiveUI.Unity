using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ReactiveUI
{
	/// <summary>
	/// Renders this canvas with gamma-space blending, matching how browsers blend CSS colors, in a linear-color project.
	/// </summary>
	/// <remarks>
	/// The canvas is switched to Screen Space - Camera on <see cref="Camera.main"/> and moved to the configured layer.
	/// Requires <c>ReactiveUIGammaFeature</c> on the camera's URP renderer, with that layer excluded from the renderer's layer mask.
	/// </remarks>
	[AutoStaticsCleanup]
	public sealed partial class GammaCanvas : MonoBehaviour
	{
		private static readonly HashSet<Canvas> s_canvases = new();

		[SerializeField]
		private Canvas _canvas = null!;

		internal static bool AnyFor(Camera camera)
		{
			// The scene view sees every camera canvas where it sits in the world, so it draws them all.
			var sceneView = camera.cameraType == CameraType.SceneView;

			foreach (var canvas in s_canvases)
			{
				if (canvas.isActiveAndEnabled && (sceneView || canvas.worldCamera == camera))
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

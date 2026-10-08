using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReactiveUI
{
	/// <summary>
	/// Warms every <see cref="UiRoot"/> in a scene that loads inactive or disabled, so its recorded
	/// sheets, fonts and textures load with the scene rather than when the root is first enabled.
	/// </summary>
	/// <remarks>An active root warms itself when it is enabled.</remarks>
	internal static class UiRootWarmup
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Initialise()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			var roots = new List<UiRoot>();

			foreach (var gameObject in scene.GetRootGameObjects())
			{
				gameObject.GetComponentsInChildren(includeInactive: true, roots);

				foreach (var root in roots)
				{
					if (!root.isActiveAndEnabled)
						root.Warm();
				}
			}
		}
	}
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace ReactiveUI
{
	/// <summary>
	/// Hosts a ReactiveUI tree under a Canvas. Subclass it and implement <see cref="CreateRoot"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A root loads every stylesheet, font and texture its type recorded in
	/// <see cref="StylePreloadManifest"/> when its scene loads, whether or not it starts active. The
	/// editor records them in Play mode.
	/// </para>
	/// <para>
	/// The tree is built when the root is first enabled and kept until it is destroyed. Disabling the
	/// root pauses the tree; enabling it again resumes it as it was.
	/// </para>
	/// </remarks>
	public abstract class UiRoot : MonoBehaviour
	{
		[SerializeField]
		private RectTransform _container = null!;

		[Tooltip("Pixels per rem. Scale the entire UI by changing this.")]
		[SerializeField]
		[Min(1f)]
		private float _remSize = 32f;

		[Tooltip("Optional Vector2 action that moves focus. Empty uses arrows, d-pad and left stick.")]
		[SerializeField]
		private InputActionReference? _navigateAction = null;

		[Tooltip("Optional button action that activates the focused node. Empty uses Enter, Space and gamepad South.")]
		[SerializeField]
		private InputActionReference? _submitAction = null;

		private UiRuntime? _runtime = null;

		protected virtual void Awake()
		{
			EnsureStyleSheets();

			// Loads the sheets, fonts and textures this root recorded on earlier runs now, while the scene is
			// loading, rather than the first time a screen needs them mid-game.
			_runtime = new UiRuntime(_container, _remSize, CreateInputBindings(), null, GetType().FullName);
			_runtime.SetRoot(CreateRoot);

#if UNITY_EDITOR
			// A script reload drops the runtime reference without destroying the root, which would leave
			// its tree behind and build a second one.
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeRuntime;
#endif
		}

		protected virtual void OnDestroy()
		{
			DisposeRuntime();
		}

		protected virtual void OnDisable()
		{
			_runtime?.Pause();
		}

		protected virtual void LateUpdate()
		{
			_runtime?.Update();
		}

		private void OnValidate()
		{
			_runtime?.SetRemSize(_remSize);
		}

		private void DisposeRuntime()
		{
#if UNITY_EDITOR
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= DisposeRuntime;
#endif

			_runtime?.Dispose();
			_runtime = null;
		}

		/// <summary>
		/// Loads what this root's type recorded in <see cref="StylePreloadManifest"/>, without building
		/// its tree.
		/// </summary>
		internal void Warm()
		{
			EnsureStyleSheets();

			if (StylePreloads.Manifest?.Find(GetType().FullName) is { } entry)
				StylePreloads.Warm(entry, StyleSheets.Source);
		}

		/// <summary>
		/// Returns the root element of the tree.
		/// </summary>
		protected abstract Element CreateRoot();

		/// <summary>
		/// Sets the number of pixels per <c>rem</c> and restyles the tree.
		/// </summary>
		protected void SetRemSize(float remSize)
		{
			_remSize = remSize;
			_runtime?.SetRemSize(remSize);
		}

		/// <summary>
		/// Supplies the actions navigation runs on, from the inspector's action references.
		/// </summary>
		private UiInputBindings? CreateInputBindings()
		{
			if (_navigateAction == null && _submitAction == null)
				return null;

			return new UiInputBindings(
				_navigateAction == null ? null : _navigateAction.action,
				_submitAction == null ? null : _submitAction.action);
		}

		/// <summary>
		/// Supplies the sheets a build renders with, unless a source is already set.
		/// </summary>
		/// <remarks>
		/// In the editor a catalog watches every .css in the project, which is what makes hot reload
		/// work, and it claims the source before anything reaches here. A build has no such catalog, so
		/// it loads the manifest the editor generated from those same files — and is loud when it finds
		/// nothing, because a completely unstyled UI reads as a layout bug rather than a missing asset.
		/// </remarks>
		private void EnsureStyleSheets()
		{
			if (StyleSheets.HasSource)
				return;

			var manifest = Resources.Load<StyleSheetManifest>(StyleSheetManifest.ResourcePath);
			var sheets = manifest == null ? null : manifest.Sheets;

			if (sheets is null || sheets.Count == 0)
			{
				Debug.LogError(
					$"[ReactiveUI] {name} found no stylesheets, so nothing will be styled. The manifest at "
					+ $"Resources/{StyleSheetManifest.ResourcePath} is generated whenever a .css file is "
					+ "imported — reimport one, or press Rebuild Stylesheet Manifest on this component.");

				return;
			}

			var library = new StyleSheetLibrary();
			StyleSheets.SetSource(library);
			library.Load(sheets);
		}
	}
}

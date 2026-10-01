using UnityEngine;
using UnityEngine.InputSystem;

namespace ReactiveUI
{
	/// <summary>
	/// The scene entry point. Subclass it, return your root element, and drop it on a
	/// RectTransform under a Canvas.
	/// </summary>
	/// <remarks>
	/// Ticks in <c>LateUpdate</c> so the whole pipeline — render, layout, paint — completes before
	/// uGUI rebuilds its canvas in the same frame. That ordering is what lets text measure itself
	/// through TextMeshPro without re-entering uGUI's own layout.
	/// </remarks>
	public abstract class UiRoot : MonoBehaviour
	{
		[SerializeField]
		private RectTransform _container = null!;

		[Tooltip("Pixels per rem. Scale the entire UI by changing this.")]
		[SerializeField]
		[Min(1f)]
		protected float _remSize = 32f;

		[Tooltip("Optional Vector2 action that moves focus. Empty uses arrows, d-pad and left stick.")]
		[SerializeField]
		private InputActionReference? _navigateAction = null;

		[Tooltip("Optional button action that activates the focused node. Empty uses Enter, Space and gamepad South.")]
		[SerializeField]
		private InputActionReference? _submitAction = null;

		private UiRuntime? _runtime = null;

		private void OnEnable()
		{
			// In the editor a catalog watches every .css in the project, which is what makes hot
			// reload work; it claims the source before anything reaches here. A build has no such
			// catalog, so it loads the manifest the editor generated from those same files.
			if (!StyleSheets.HasSource)
				LoadStyleSheets();

			_runtime = new UiRuntime(_container, _remSize, CreateInputBindings());
			_runtime.SetRoot(CreateRoot);
		}

		private void OnDisable()
		{
			_runtime?.Dispose();
			_runtime = null;
		}

		private void LateUpdate()
		{
			_runtime?.Update();
		}


		private void OnValidate()
		{
			_runtime?.SetRemSize(_remSize);
		}

		/// <summary>
		/// Builds the root element.
		/// </summary>
		protected abstract Element CreateRoot();

		/// <summary>
		/// Changes the pixels per rem, restyling the live tree when there is one.
		/// </summary>
		protected void SetRemSize(float remSize)
		{
			_remSize = remSize;
			_runtime?.SetRemSize(remSize);
		}

		/// <summary>
		/// Supplies the actions navigation runs on.
		/// </summary>
		/// <remarks>
		/// Reads the inspector's action references by default. Override to hand over actions that live
		/// somewhere a serialized reference cannot reach, such as a generated actions wrapper.
		/// </remarks>
		private UiInputBindings? CreateInputBindings()
		{
			if (_navigateAction == null && _submitAction == null)
				return null;

			return new UiInputBindings(
				_navigateAction == null ? null : _navigateAction.action,
				_submitAction == null ? null : _submitAction.action);
		}

		/// <summary>
		/// Supplies the sheets a build renders with.
		/// </summary>
		/// <remarks>
		/// This path never runs in the editor — the catalog has already claimed the source by the time
		/// <c>OnEnable</c> is reached — so it is worth being loud when it finds nothing. The failure it
		/// guards against is a completely unstyled UI, which reads as a layout bug rather than a
		/// missing-asset one and is miserable to chase.
		/// </remarks>
		private void LoadStyleSheets()
		{
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

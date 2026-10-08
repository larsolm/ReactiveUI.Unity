using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ReactiveUI
{
	/// <summary>
	/// Turns a World Space root canvas about a vertical axis with true perspective, as CSS
	/// <c>perspective(…) rotateY(…)</c> does, even under an orthographic camera.
	/// </summary>
	/// <remarks>
	/// The warp happens in the vertex stage of projected twins of the canvas's shaders
	/// (<c>Hidden/ReactiveUI/Projected/&lt;shader&gt;</c>), so layout, masking and animation all run on the
	/// flat canvas and only the drawn result is turned. The canvas draws its batches in its own local space, so moving
	/// it needs no update. The axis runs vertically through the rect's pivot and the vanishing height is its vertical
	/// centre. A positive angle brings the edge across from the pivot toward the viewer, so moving the pivot to the
	/// other side mirrors the turn. Raycasts still hit the flat rect.
	/// </remarks>
	[ExecuteAlways]
	[RequireComponent(typeof(Canvas))]
	public sealed partial class ProjectedCanvas : MonoBehaviour
	{
		private const string ShaderPrefix = "Hidden/ReactiveUI/Projected/";
		private const float MaxAngle = 60f;

		[AutoStaticsCleanup]
		private static readonly Dictionary<Canvas, ProjectedCanvas> s_canvases = new();

		[AutoStaticsCleanup]
		private static readonly Dictionary<Shader, Shader?> s_shaders = new();

		private static readonly int s_params = Shader.PropertyToID("_ReactiveUIProjectParams");

		[Tooltip("Turn about the pivot, in degrees. Positive brings the edge across from the pivot closer; for a centred pivot, the right edge.")]
		[SerializeField]
		[Range(-MaxAngle, MaxAngle)]
		private float _angle = 15f;

		[Tooltip("Viewer distance in canvas units, as in CSS perspective(). Smaller is stronger.")]
		[SerializeField]
		[Min(1f)]
		private float _perspective = 1000f;

		private readonly Dictionary<Material, Material> _twins = new();

		private Vector4 _params;

		/// <summary>Turn about the pivot, in degrees. Positive brings the edge across from the pivot closer.</summary>
		public float Angle
		{
			get => _angle;
			set
			{
				_angle = Mathf.Clamp(value, -MaxAngle, MaxAngle);
				Sync();
			}
		}

		/// <summary>Viewer distance in canvas units. Smaller is stronger.</summary>
		public float Perspective
		{
			get => _perspective;
			set
			{
				_perspective = Mathf.Max(1f, value);
				Sync();
			}
		}

		internal static bool TryGet(Canvas canvas, out ProjectedCanvas projected)
		{
			return s_canvases.TryGetValue(canvas, out projected!);
		}

		internal Material TwinFor(Material source)
		{
			if (source == null)
				return source!;

			if (_twins.TryGetValue(source, out var twin) && twin != null)
				return twin;

			var shader = ShaderFor(source.shader);

			if (shader == null)
				return source;

			twin = new Material(shader)
			{
				name = $"{source.name} (Projected)",
				hideFlags = HideFlags.HideAndDontSave,
			};

			twin.CopyPropertiesFromMaterial(source);
			twin.SetVector(s_params, _params);
			_twins[source] = twin;

			return twin;
		}

		private void OnEnable()
		{
			s_canvases[GetComponent<Canvas>()] = this;
			Capture();
			RefreshMaterials();
		}

		private void OnDisable()
		{
			var canvas = GetComponent<Canvas>();

			if (s_canvases.TryGetValue(canvas, out var registered) && registered == this)
				s_canvases.Remove(canvas);

			RefreshMaterials();

			foreach (var twin in _twins.Values)
			{
				if (twin != null)
					DestroyImmediate(twin);
			}

			_twins.Clear();
		}

		private void OnValidate()
		{
			Sync();
		}

		private void OnRectTransformDimensionsChange()
		{
			Sync();
		}

		private static Shader? ShaderFor(Shader shader)
		{
			if (s_shaders.TryGetValue(shader, out var twin))
				return twin;

			twin = shader.name.StartsWith(ShaderPrefix) ? null : Shader.Find(ShaderPrefix + shader.name);
			s_shaders[shader] = twin;

			return twin;
		}

		private void Sync()
		{
			if (!isActiveAndEnabled || !Capture())
				return;

			foreach (var twin in _twins.Values)
			{
				if (twin != null)
					twin.SetVector(s_params, _params);
			}
		}

		private bool Capture()
		{
			var rect = (RectTransform)transform;
			var swing = rect.pivot.x > 0.5f ? -1f : 1f;
			var parameters = new Vector4(0f, rect.rect.center.y, swing * Mathf.Sin(_angle * Mathf.Deg2Rad), _perspective);

			if (parameters.Equals(_params))
				return false;

			_params = parameters;

			return true;
		}

		private void RefreshMaterials()
		{
			foreach (var graphic in GetComponentsInChildren<Graphic>(includeInactive: true))
				graphic.SetMaterialDirty();
		}
	}
}

using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace ReactiveUI
{
	/// <summary>
	/// Twins a uGUI material with one that outputs sRGB values, for drawing a <see cref="GammaCanvas"/>.
	/// </summary>
	/// <remarks>
	/// The twin's shader is the source's name under <see cref="ShaderPrefix"/>, so supporting another
	/// shader is a matter of shipping its gamma copy. A material whose shader has no copy is returned
	/// unchanged. Twins are cached per source material, which includes the stencil materials uGUI
	/// pools, so the cache stays as small as that pool.
	/// </remarks>
	[AutoStaticsCleanup]
	internal static partial class GammaMaterials
	{
		internal const string ShaderPrefix = "Hidden/ReactiveUI/Gamma/";

		private static readonly Dictionary<Material, Material> s_twins = new();
		private static readonly Dictionary<Shader, Shader?> s_shaders = new();

		internal static Material For(Material source)
		{
			// A TMP run with no font yet hands over a missing material; there is nothing to twin.
			if (source == null)
				return source!;

			if (s_twins.TryGetValue(source, out var twin) && twin != null)
				return twin;

			var shader = ShaderFor(source.shader);
			if (shader == null)
				return source;

			twin = new Material(shader)
			{
				name = $"{source.name} (Gamma)",
				hideFlags = HideFlags.HideAndDontSave,
			};

			twin.CopyPropertiesFromMaterial(source);
			s_twins[source] = twin;

			return twin;
		}

		private static Shader? ShaderFor(Shader shader)
		{
			if (s_shaders.TryGetValue(shader, out var twin))
				return twin;

			twin = shader.name.StartsWith(ShaderPrefix) ? null : Shader.Find(ShaderPrefix + shader.name);
			s_shaders[shader] = twin;

			return twin;
		}
	}
}

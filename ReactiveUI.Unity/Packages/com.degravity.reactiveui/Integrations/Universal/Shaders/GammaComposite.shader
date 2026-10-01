// Lays the gamma canvas (premultiplied sRGB colour and coverage) over the camera colour the way a
// browser composites: in sRGB, then decoded back to the linear frame.
Shader "Hidden/ReactiveUI/GammaComposite"
{
	SubShader
	{
		Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
		ZWrite Off
		ZTest Always
		Cull Off
		Blend Off

		Pass
		{
			Name "Composite"

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

			TEXTURE2D_X(_ReactiveUIGammaCanvas);

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
				float4 canvas = SAMPLE_TEXTURE2D_X(_ReactiveUIGammaCanvas, sampler_PointClamp, input.texcoord);

				// Untouched by the UI: pass the scene through exactly, HDR values included.
				if (canvas.a <= 0.0 && all(canvas.rgb <= 0.0))
					return scene;

				float3 blended = canvas.rgb + (1.0 - canvas.a) * LinearToSRGB(max(scene.rgb, 0.0));

				return half4(SRGBToLinear(blended), scene.a);
			}
			ENDHLSL
		}
	}
}

// UI/Default for a GammaCanvas: identical except that it outputs sRGB values, premultiplied, so the
// canvas blends in gamma space in its own 8-bit target before being composited over the scene.
Shader "Hidden/ReactiveUI/Gamma/UI/Default"
{
	Properties
	{
		[PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
		_Color ("Tint", Color) = (1,1,1,1)

		_StencilComp ("Stencil Comparison", Float) = 8
		_Stencil ("Stencil ID", Float) = 0
		_StencilOp ("Stencil Operation", Float) = 0
		_StencilWriteMask ("Stencil Write Mask", Float) = 255
		_StencilReadMask ("Stencil Read Mask", Float) = 255

		_ColorMask ("Color Mask", Float) = 15

		[Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
	}

	SubShader
	{
		Tags
		{
			"Queue"="Transparent"
			"IgnoreProjector"="True"
			"RenderType"="Transparent"
			"PreviewType"="Plane"
			"CanUseSpriteAtlas"="True"
		}

		Stencil
		{
			Ref [_Stencil]
			Comp [_StencilComp]
			Pass [_StencilOp]
			ReadMask [_StencilReadMask]
			WriteMask [_StencilWriteMask]
		}

		Cull Off
		Lighting Off
		ZWrite Off
		ZTest [unity_GUIZTestMode]
		Blend One OneMinusSrcAlpha
		ColorMask [_ColorMask]

		Pass
		{
			Name "Default"

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0

			#include "UnityCG.cginc"
			#include "UnityUI.cginc"
			#include "ReactiveUIGamma.cginc"

			#pragma multi_compile_local _ UNITY_UI_CLIP_RECT
			#pragma multi_compile_local _ UNITY_UI_ALPHACLIP

			struct appdata_t
			{
				float4 vertex : POSITION;
				float4 color : COLOR;
				float2 texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				fixed4 color : COLOR;
				float2 texcoord : TEXCOORD0;
				float4 worldPosition : TEXCOORD1;
				float4 mask : TEXCOORD2;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			sampler2D _MainTex;
			fixed4 _Color;
			fixed4 _TextureSampleAdd;
			float4 _ClipRect;
			float4 _MainTex_ST;
			float _UIMaskSoftnessX;
			float _UIMaskSoftnessY;
			int _UIVertexColorAlwaysGammaSpace;

			v2f vert(appdata_t v)
			{
				v2f OUT;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				float4 vPosition = UnityObjectToClipPos(v.vertex);
				OUT.worldPosition = v.vertex;
				OUT.vertex = vPosition;

				float2 pixelSize = vPosition.w;
				pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

				float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
				OUT.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
				OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

				// Vertex colours stay in sRGB: a gamma-space canvas hands them over untouched, any
				// other had them linearised on the CPU and they are encoded back.
				if (!_UIVertexColorAlwaysGammaSpace)
					v.color.rgb = ReactiveUiLinearToSrgb(v.color.rgb);

				OUT.color = v.color * ReactiveUiGamma(_Color);

				return OUT;
			}

			fixed4 frag(v2f IN) : SV_Target
			{
				// Round the vertex alpha to 8 bits, as UI/Default does, so a fade never lands between steps.
				const half alphaPrecision = half(0xff);
				const half invAlphaPrecision = half(1.0 / alphaPrecision);
				IN.color.a = round(IN.color.a * alphaPrecision) * invAlphaPrecision;

				// An sRGB texture is decoded on sampling, so it is encoded back to sit with the vertex colour.
				half4 texel = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
				texel.rgb = ReactiveUiLinearToSrgb(texel.rgb);

				half4 color = IN.color * texel;

				#ifdef UNITY_UI_CLIP_RECT
				half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
				color.a *= m.x * m.y;
				#endif

				#ifdef UNITY_UI_ALPHACLIP
				clip(color.a - 0.001);
				#endif

				color.rgb *= color.a;

				return color;
			}
			ENDCG
		}
	}
}

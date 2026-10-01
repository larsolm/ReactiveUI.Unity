#ifndef REACTIVEUI_GAMMA_INCLUDED
#define REACTIVEUI_GAMMA_INCLUDED

// Linear to sRGB with the exact piecewise curve rather than UnityCG's approximation, so a colour
// survives the round trip through the composite unchanged. A gamma project already works in sRGB.
inline float3 ReactiveUiLinearToSrgb(float3 color)
{
#if defined(UNITY_COLORSPACE_GAMMA)
	return color;
#else
	color = max(color, 0.0);
	float3 low = color * 12.92;
	float3 high = 1.055 * pow(color, 1.0 / 2.4) - 0.055;
	return lerp(high, low, step(color, 0.0031308));
#endif
}

inline float4 ReactiveUiGamma(float4 color)
{
	return float4(ReactiveUiLinearToSrgb(color.rgb), color.a);
}

#endif

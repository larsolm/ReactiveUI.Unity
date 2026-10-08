#ifndef REACTIVEUI_PROJECTED_INCLUDED
#define REACTIVEUI_PROJECTED_INCLUDED

// Set per canvas by ProjectedCanvas: (axis x, vanishing y, sin(angle), perspective) in canvas units. A World Space
// canvas batches its geometry in its own local space, so object space is canvas space.
float4 _ReactiveUIProjectParams;

// Clip position of an object-space vertex after turning the canvas about its vertical axis and projecting it
// back onto the canvas plane, as CSS perspective(p) rotateY(a) does.
inline float4 ReactiveUiProjectedClipPos(float4 objectPosition)
{
	float3 local = objectPosition.xyz;

	float axis = _ReactiveUIProjectParams.x;
	float vanish = _ReactiveUIProjectParams.y;
	float sine = _ReactiveUIProjectParams.z;
	float perspective = _ReactiveUIProjectParams.w;

	float offset = local.x - axis;
	float scale = perspective / max(perspective - offset * sine, 1.0);

	local.x = axis + offset * sqrt(saturate(1.0 - sine * sine)) * scale;
	local.y = vanish + (local.y - vanish) * scale;

	return mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, float4(local, 1.0)));
}

#endif

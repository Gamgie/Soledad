// Attributes / varyings of Soledad/CloudCube. The mesh is Unity's cube (+-0.5); the vertex stage grows it into a hull
// that holds the fluffy edge (forward pass) or shrinks it into the core that casts the shadow (shadow pass).
#ifndef SHADERPASS
#error Undefine_SHADERPASS
#endif

#define HAVE_MESH_MODIFICATION
#define _DISABLE_DECALS

#define ATTRIBUTES_NEED_NORMAL
#define ATTRIBUTES_NEED_TANGENT

#if (SHADERPASS == SHADERPASS_FORWARD)
#define CC_FULL_SURFACE
#define VARYINGS_NEED_POSITION_WS
#define VARYINGS_NEED_TANGENT_TO_WORLD
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"

// World size of the cube along its own axes
float3 CC_ObjectScale()
{
    float4x4 m = GetObjectToWorldMatrix();
    return float3(length(m._m00_m10_m20), length(m._m01_m11_m21), length(m._m02_m12_m22));
}

// Big cubes get proportionally bigger puffs, so the fluff still reads from far away: every length setting is
// multiplied by this (1 for a cube of the reference size)
float CC_SizeScale(float3 scale)
{
    float edge = (scale.x + scale.y + scale.z) / 3.0;
    return lerp(1.0, edge / max(_CloudReferenceSize, 1.0), saturate(_CloudScaleWithSize));
}

// Set once per pixel (forward pass) from the cube size, used by the length settings below
static float cc_k = 1.0;
#define CC_FLUFF (_CloudFluff * cc_k)
#define CC_PUFF  (_CloudNoiseSize * cc_k)
#define CC_SOFT  (_CloudSoftness * cc_k)
#define CC_ROUND (_CloudRoundness * cc_k)

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    float3 scale = max(CC_ObjectScale(), 1e-3);
#if SHADERPASS == SHADERPASS_SHADOWS
    input.positionOS *= _CloudShadowCore;
#else
    input.positionOS *= 1.0 + 2.0 * _CloudFluff * CC_SizeScale(scale) / scale;   // the hull: the cube plus the fluff margin
#endif
    return input;
}

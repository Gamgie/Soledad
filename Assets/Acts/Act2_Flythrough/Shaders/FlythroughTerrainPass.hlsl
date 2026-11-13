// Attributes / varyings of Soledad/FlythroughTerrain and its world curl, plugged into HDRP's Lit passes through the
// HAVE_MESH_MODIFICATION hook of VertMesh.hlsl (also applied to the previous frame in the motion vector pass).
//
// Mesh data (written by FlythroughTerrain.cs, in terrain space):
//   positionOS = terrain point (x, height, z), normalOS / tangentOS = terrain normal and +X tangent
//   uv0 = terrain xz (stable material coordinates, unaffected by the curl)
//   uv1 = (height, slope = 1 - terrain normal.y)
//   color = biome weights (r desert, g mountains, b valley)
#ifndef SHADERPASS
#error Undefine_SHADERPASS
#endif

#define HAVE_MESH_MODIFICATION
#define _DISABLE_DECALS

#define ATTRIBUTES_NEED_NORMAL
#define ATTRIBUTES_NEED_TANGENT
#define ATTRIBUTES_NEED_TEXCOORD0
#define ATTRIBUTES_NEED_TEXCOORD1
#define ATTRIBUTES_NEED_COLOR

#if (SHADERPASS == SHADERPASS_GBUFFER) || (SHADERPASS == SHADERPASS_FORWARD) || defined(WRITE_NORMAL_BUFFER)
#define FT_FULL_SURFACE
#define VARYINGS_NEED_POSITION_WS
#define VARYINGS_NEED_TANGENT_TO_WORLD
#define VARYINGS_NEED_TEXCOORD0
#define VARYINGS_NEED_TEXCOORD1
#define VARYINGS_NEED_COLOR
#elif SHADERPASS == SHADERPASS_MOTION_VECTORS
#define VARYINGS_NEED_POSITION_WS
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"

// Rolls a point around an axis parallel to `along`, `radius` above the origin: distance `d` across the axis becomes
// an angle, height above the origin plane shrinks the radius. Returns the angle used, to rotate the normal.
float FT_Roll(inout float across, inout float up, float invRadius)
{
    float radius = 1.0 / invRadius;
    float angle = clamp(across * invRadius, -PI, PI);   // past half a turn the two sides meet: close the tube
    float r = radius - up;
    float s, c;
    sincos(angle, s, c);
    across = r * s;
    up = radius - r * c;
    return angle;
}

float3 FT_Rotate(float3 v, float lateralAngle, float forwardAngle)
{
    float s, c;
    sincos(lateralAngle, s, c);
    v.xy = float2(v.x * c - v.y * s, v.x * s + v.y * c);
    sincos(forwardAngle, s, c);
    v.zy = float2(v.z * c - v.y * s, v.z * s + v.y * c);
    return v;
}

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    if (_CurlLateral <= 0.0 && _CurlForward <= 0.0)
        return input;

    float3 positionWS = GetAbsolutePositionWS(TransformObjectToWorld(input.positionOS));
    float3 p = positionWS - _CurlOrigin.xyz;

    float lateralAngle = 0.0, forwardAngle = 0.0;
    if (_CurlLateral > 0.0)
        lateralAngle = FT_Roll(p.x, p.y, _CurlLateral);
    if (_CurlForward > 0.0 && p.z > 0.0)                 // only the ground ahead lifts
        forwardAngle = FT_Roll(p.z, p.y, _CurlForward);

    positionWS = p + _CurlOrigin.xyz;
    input.positionOS = TransformWorldToObject(GetCameraRelativePositionWS(positionWS));

    float3 normalWS = FT_Rotate(TransformObjectToWorldNormal(input.normalOS), lateralAngle, forwardAngle);
    input.normalOS = TransformWorldToObjectNormal(normalWS);
    float3 tangentWS = FT_Rotate(TransformObjectToWorldDir(input.tangentOS.xyz), lateralAngle, forwardAngle);
    input.tangentOS.xyz = TransformWorldToObjectDir(tangentWS);
    return input;
}

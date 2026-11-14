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

float3 FT_ToFlightFrame(float3 v, float2 right, float2 forward)
{
    return float3(dot(v.xz, right), v.y, dot(v.xz, forward));
}

float3 FT_ToWorldFrame(float3 v, float2 right, float2 forward)
{
    return float3(right.x * v.x + forward.x * v.z, v.y, right.y * v.x + forward.y * v.z);
}

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    if (_CurlLateral <= 0.0 && _CurlForward <= 0.0)
        return input;

    // Work in the flight frame: x = right of the flight, y = up, z = along the flight
    float2 forward = normalize(_CurlFrame.xy + float2(0.0, 1e-6));
    float2 right = float2(forward.y, -forward.x);

    float3 positionWS = GetAbsolutePositionWS(TransformObjectToWorld(input.positionOS));
    float3 d = positionWS - _CurlOrigin.xyz;
    float3 p = float3(dot(d.xz, right), d.y, dot(d.xz, forward));

    float lateralAngle = 0.0, forwardAngle = 0.0;
    if (_CurlLateral > 0.0)
        lateralAngle = FT_Roll(p.x, p.y, _CurlLateral);
    if (_CurlForward > 0.0 && p.z > 0.0)                 // only the ground ahead lifts
        forwardAngle = FT_Roll(p.z, p.y, _CurlForward);

    d = float3(right.x * p.x + forward.x * p.z, p.y, right.y * p.x + forward.y * p.z);
    input.positionOS = TransformWorldToObject(GetCameraRelativePositionWS(d + _CurlOrigin.xyz));

    float3 normalWS = FT_ToWorldFrame(FT_Rotate(FT_ToFlightFrame(TransformObjectToWorldNormal(input.normalOS), right, forward), lateralAngle, forwardAngle), right, forward);
    input.normalOS = TransformWorldToObjectNormal(normalWS);
    float3 tangentWS = FT_ToWorldFrame(FT_Rotate(FT_ToFlightFrame(TransformObjectToWorldDir(input.tangentOS.xyz), right, forward), lateralAngle, forwardAngle), right, forward);
    input.tangentOS.xyz = TransformWorldToObjectDir(tangentWS);
    return input;
}

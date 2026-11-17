// Attributes / varyings of Soledad/HillGrass, plugged into HDRP's Lit passes through the HAVE_MESH_MODIFICATION hook
// of VertMesh.hlsl: each shell is the mesh pushed out along its normal by _ShellHeight x _GrassHeight.
//
// Mesh data (Hill.cs, HillLandscape.cs): positionOS / normalOS / tangentOS, uv0 = ground coordinates in metres.
// Written here per vertex (they vary over metres, so not per pixel of every shell):
//   uv1 = lean of the blades at their tip (comb patches + wind, m)
//   uv2 = (which of the two texture samples shows, 0 to 1; large light / dark patch, 0 to 1)
#ifndef SHADERPASS
#error Undefine_SHADERPASS
#endif

#define HAVE_MESH_MODIFICATION
#define _DISABLE_DECALS

#define ATTRIBUTES_NEED_NORMAL
#define ATTRIBUTES_NEED_TANGENT
#define ATTRIBUTES_NEED_TEXCOORD0
#define ATTRIBUTES_NEED_TEXCOORD1
#define ATTRIBUTES_NEED_TEXCOORD2

#define VARYINGS_NEED_POSITION_WS
#define VARYINGS_NEED_TEXCOORD0
#define VARYINGS_NEED_TEXCOORD1
#define VARYINGS_NEED_TEXCOORD2
#define VARYINGS_NEED_TANGENT_TO_WORLD     // every pass: the shell fade needs the normal
#if (SHADERPASS == SHADERPASS_GBUFFER) || (SHADERPASS == SHADERPASS_FORWARD) || defined(WRITE_NORMAL_BUFFER)
#define HG_FULL_SURFACE
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"

// PCG hash (exact on integer cell coordinates)
uint2 HG_Pcg2(uint2 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * 1664525u;
    v.y += v.x * 1664525u;
    v ^= v >> 16u;
    v.x += v.y * 1664525u;
    v.y += v.x * 1664525u;
    v ^= v >> 16u;
    return v;
}

float HG_Hash(float2 cell)
{
    return HG_Pcg2((uint2)(int2)cell + 65536u).x * (1.0 / 4294967296.0);
}

// Value noise, 0 to 1
float HG_Noise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = HG_Hash(i), b = HG_Hash(i + float2(1, 0)), c = HG_Hash(i + float2(0, 1)), d = HG_Hash(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float HG_Fbm(float2 p)
{
    return HG_Noise(p) * 0.6 + HG_Noise(p * 2.13 + 7.1) * 0.3 + HG_Noise(p * 4.37 + 3.3) * 0.1;
}

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    input.positionOS += input.normalOS * (_ShellHeight * _GrassHeight);

    float2 uv = input.uv0.xy;
    float combAngle = HG_Fbm(uv / max(_CombScale, 0.1) + 31.7) * 12.566;
    float2 comb = float2(cos(combAngle), sin(combAngle));
    float t = timeParameters.x * _WindSpeed;
    float2 windUV = uv / max(_WindScale, 0.1);
    float2 wind = float2(HG_Noise(windUV + float2(t, 0.37 * t)), HG_Noise(windUV + float2(5.2 - 0.41 * t, t + 1.3))) * 2.0 - 1.0;
    input.uv1 = comb * _Lean + wind * _WindStrength;

    float tileBreak = HG_Fbm(uv / max(_TileBreakScale, 0.1) + 11.3);
    float patch = HG_Fbm(uv / max(_PatchScale, 0.1) + 57.1);
    input.uv2 = float2(tileBreak, patch);
    return input;
}

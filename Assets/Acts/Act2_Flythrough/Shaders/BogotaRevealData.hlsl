// Surface of Soledad/BogotaReveal: the chunk's own map (base colour map, smoothness, like the HDRP Lit it replaces),
// cut by the reveal: Bogota appears from a centre on the map outwards, its front torn by noise, islands of map
// showing a little ahead of it, a glowing rim along the front and the map burnt just behind it.
//
// Globals, set every frame by BogotaSequence (Play mode). At their defaults (0) there is no reveal: the whole map shows,
// as in the Editor.

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

float4x4 _BogotaWorldToLocal;   // world (absolute) to Bogota's local space (x east, y up, z north)
float4   _BogotaReveal;         // x, y = centre (local x, z), z = radius of the front (m), w = width of the torn band (m)
float4   _BogotaRevealNoise;    // x = 1 / noise size (1/m), y = tearing (0 = clean circle), z = glow width (band units), w = on
float4   _BogotaRevealGlow;     // rgb = rim colour (HDR, independent of the exposure), a = how dark the burnt edge gets

float BR_Hash(float2 p)
{
    float3 q = frac(float3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float BR_ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = BR_Hash(i);
    float b = BR_Hash(i + float2(1.0, 0.0));
    float c = BR_Hash(i + float2(0.0, 1.0));
    float d = BR_Hash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Four octaves, each turned so the cells never line up: blotches with ragged edges, in [0, 1]
float BR_Fbm(float2 p)
{
    const float2x2 turn = float2x2(0.8, -0.6, 0.6, 0.8);
    float sum = 0.0, amplitude = 0.5;
    for (int i = 0; i < 4; i++)
    {
        sum += amplitude * BR_ValueNoise(p);
        p = mul(turn, p) * 2.03 + 17.1;
        amplitude *= 0.5;
    }
    return sum / 0.9375;
}

// > 0 where the map shows. glow: 1 right on the front, 0 a little behind it; burn: the same, wider
float BR_Reveal(float3 positionRWS, out float glow, out float burn)
{
    glow = 0.0;
    burn = 0.0;
    if (_BogotaRevealNoise.w < 0.5)
        return 1.0;

    float3 p = mul(_BogotaWorldToLocal, float4(GetAbsolutePositionWS(positionRWS), 1.0)).xyz;
    float distance = length(p.xz - _BogotaReveal.xy);
    float noise = BR_Fbm(p.xz * _BogotaRevealNoise.x);
    float reveal = (_BogotaReveal.z - distance) / max(_BogotaReveal.w, 1.0) + (noise - 0.5) * _BogotaRevealNoise.y;

    float width = max(_BogotaRevealNoise.z, 1e-3);
    glow = 1.0 - saturate(reveal / width);
    burn = 1.0 - saturate(reveal / (width * 4.0));
    return reveal;
}

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    float glow, burn;
    clip(BR_Reveal(input.positionRWS, glow, burn));

    ZERO_INITIALIZE(SurfaceData, surfaceData);
    surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD;
    surfaceData.specularOcclusion = 1.0;
    surfaceData.ambientOcclusion = 1.0;
    surfaceData.ior = 1.0;
    surfaceData.atDistance = 1.0;
    surfaceData.transmittanceColor = 1.0;

#ifdef BR_FULL_SURFACE
    float4 color = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, input.texCoord0.xy) * _BaseColor;
    surfaceData.baseColor = color.rgb * (1.0 - _BogotaRevealGlow.a * burn * burn);
    surfaceData.perceptualSmoothness = _Smoothness;
    surfaceData.metallic = _Metallic;

    // Double sided, like the Lit materials it replaces: the back face shades with its own normal
    float3 N = normalize(input.tangentToWorld[2]);
    if (!input.isFrontFace) N = -N;
    surfaceData.normalWS = N;
    surfaceData.geomNormalWS = N;
    surfaceData.tangentWS = Orthonormalize(input.tangentToWorld[0], N);
#else
    surfaceData.baseColor = 0.5;
    surfaceData.normalWS = float3(0.0, 1.0, 0.0);
    surfaceData.geomNormalWS = surfaceData.normalWS;
    surfaceData.tangentWS = float3(1.0, 0.0, 0.0);
#endif

#if defined(DEBUG_DISPLAY) && defined(BR_FULL_SURFACE)
    ApplyDebugToSurfaceData(input.tangentToWorld, surfaceData);
#endif

    float4 texCoord1 = 0.0, texCoord2 = 0.0;
    InitBuiltinData(posInput, 1.0, surfaceData.normalWS, -surfaceData.normalWS, texCoord1, texCoord2, builtinData);
#ifdef BR_FULL_SURFACE
    // The rim glows the same whatever the exposure (the automatic exposure changes a lot over Bogota)
    builtinData.emissiveColor = _BogotaRevealGlow.rgb * (glow * glow) * GetInverseCurrentExposureMultiplier();
#endif
    PostInitBuiltinData(V, posInput, surfaceData, builtinData);

    RAY_TRACING_OPTIONAL_ALPHA_TEST_PASS
}

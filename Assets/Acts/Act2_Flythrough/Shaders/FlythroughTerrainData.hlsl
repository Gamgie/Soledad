// Surface of Soledad/FlythroughTerrain: an HDRP Lit standard surface whose colour and fine relief are computed from
// terrain coordinates (uv0), so they stay put on the ground when the world curls. Replaces LitData.hlsl.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

// ---------------------------------------------------------------- noise

float FT_Hash(float2 p)
{
    p = frac(p * float2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return frac((p.x + p.y) * p.x);
}

float2 FT_Hash2(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Value noise in [-1, 1] with its analytic gradient (Inigo Quilez): x = value, yz = d/dp
float3 FT_NoiseD(float2 p)
{
    float2 i = floor(p);
    float2 f = p - i;
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float2 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);

    float a = FT_Hash(i);
    float b = FT_Hash(i + float2(1, 0));
    float c = FT_Hash(i + float2(0, 1));
    float d = FT_Hash(i + float2(1, 1));

    float k1 = b - a, k2 = c - a, k4 = a - b - c + d;
    float v = a + k1 * u.x + k2 * u.y + k4 * u.x * u.y;
    float2 g = du * float2(k1 + k4 * u.y, k2 + k4 * u.x);
    return float3(v * 2.0 - 1.0, g * 2.0);
}

// Fractal sum: x = value, yz = gradient in metres^-1 (p in metres, scale = size of the largest feature)
float3 FT_FbmD(float2 p, float scale, int octaves)
{
    float3 sum = 0.0;
    float amp = 0.5, freq = 1.0 / scale;
    // Rotate each octave to break the grid alignment
    const float2x2 rot = float2x2(0.8, -0.6, 0.6, 0.8);
    float2 q = p;
    for (int o = 0; o < octaves; o++)
    {
        float3 n = FT_NoiseD(q * freq);
        sum.x += amp * n.x;
        sum.yz += amp * freq * n.yz;
        amp *= 0.5;
        freq *= 2.03;
        q = mul(rot, q) + 17.1;
    }
    return sum;
}

// ---------------------------------------------------------------- surface

// Agricultural fields of the valley: irregular rectangles (jittered grid), one crop per field
float3 FT_Fields(float2 p, out float hedge)
{
    float size = max(_FieldSize, 1.0);
    // Two field orientations, swapped by a large noise, like plots following roads
    float turn = FT_NoiseD(p / (size * 9.0)).x > 0.0 ? 0.35 : -0.2;
    float s, c;
    sincos(turn, s, c);
    float2 q = float2(c * p.x - s * p.y, s * p.x + c * p.y);
    q.x *= 0.6;                                          // long plots

    float2 cell = floor(q / size);
    float2 f = frac(q / size);
    float2 jitter = FT_Hash2(cell) * 0.15;
    float2 edge = min(f - jitter * 0.5, 1.0 - f);
    hedge = 1.0 - smoothstep(0.0, 0.03, min(edge.x, edge.y));

    float crop = FT_Hash(cell + 7.7);
    float3 col = crop < 0.45 ? _GrassColor.rgb : crop < 0.75 ? _FieldColorB.rgb : crop < 0.9 ? _DryGrassColor.rgb : _SoilColor.rgb;
    // Rows inside the field
    float rows = 0.5 + 0.5 * sin(q.y * 6.2831853 / 3.0 + crop * 20.0);
    return col * lerp(0.85, 1.05, rows * (crop < 0.9 ? 1.0 : 0.3));
}

// ---------------------------------------------------------------- scanned sand layers

struct FTLayer
{
    float3 albedo;
    float2 gradient;     // height gradient in terrain x / z (from the normal map)
    float height;        // 0..1 displacement, for height blending
    float roughness;
    float ao;
};

float2 FT_NormalToGradient(float4 packedNormal, float strength)
{
    float3 n = UnpackNormalMapRGorAG(packedNormal, strength);
    n.y *= 1.0 - 2.0 * _SandNormalDirectX;
    return -n.xy / max(n.z, 0.1);
}

#define FT_SAMPLE_LAYER(prefix, uv, strength, layer)                                                         \
    layer.albedo = SAMPLE_TEXTURE2D(prefix##Albedo, sampler##prefix##Albedo, uv).rgb;                       \
    layer.gradient = FT_NormalToGradient(SAMPLE_TEXTURE2D(prefix##Normal, sampler##prefix##Normal, uv), strength); \
    layer.height = SAMPLE_TEXTURE2D(prefix##Height, sampler##prefix##Height, uv).r;                         \
    layer.roughness = SAMPLE_TEXTURE2D(prefix##Roughness, sampler##prefix##Roughness, uv).r;                \
    layer.ao = SAMPLE_TEXTURE2D(prefix##AO, sampler##prefix##AO, uv).r;

float2 FT_Rotate2(float2 v, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return float2(c * v.x - s * v.y, s * v.x + c * v.y);
}

// Desert surface from the two scans: rippled sand on the gentle windward slopes and in patches, smooth dry sand on
// the steep slip faces, mixed by their height maps. A second, larger and rotated sample breaks the tiling and keeps
// relief visible from altitude.
// Where the rippled sand goes (1) versus the dry sand (0), before the height blend. Tweakable on the material:
// coverage, preference for the upper or lower part of the dunes, slope limit, patchiness.
float FT_RippleMask(float slope, float duneLevel, float variation)
{
    float mask = 0.5 + _RippleCoverage * 0.5
               + _RippleHeightBias * (duneLevel - 0.5)
               + _RipplePatchiness * variation;
    float flatEnough = smoothstep(_RippleSlopeLimit + 0.06, _RippleSlopeLimit - 0.06, slope);
    return saturate(mask) * flatEnough;
}

FTLayer FT_Desert(float2 p, float slope, float duneLevel, float variation)
{
    float tile = max(_SandTile, 0.01);
    float2 uv = p / tile;
    float2 uvLarge = FT_Rotate2(p, 0.7) / (tile * _SandLargeScale);

    FTLayer ripple, dry, rippleLarge, dryLarge;
    FT_SAMPLE_LAYER(_RippleSand, uv, _SandNormalStrength, ripple)
    FT_SAMPLE_LAYER(_DrySand, uv * 0.83 + 0.37, _SandNormalStrength, dry)
    FT_SAMPLE_LAYER(_RippleSand, uvLarge, _SandNormalStrength, rippleLarge)
    FT_SAMPLE_LAYER(_DrySand, uvLarge * 1.3 + 0.61, _SandNormalStrength, dryLarge)

    // Placement mask, then a height blend: the sand that stands higher in its own displacement map wins at the border
    float rippleMask = FT_RippleMask(slope, duneLevel, variation);
    float sharp = max(_RippleBlendSharpness, 0.5);
    float blendRipple = saturate((rippleMask - 0.5) * sharp + (ripple.height - dry.height) * 2.5 + 0.5);
    float blendLarge = saturate((rippleMask - 0.5) * sharp + (rippleLarge.height - dryLarge.height) * 2.5 + 0.5);

    FTLayer o;
    float3 albedoSmall = lerp(dry.albedo, ripple.albedo, blendRipple);
    float3 albedoLarge = lerp(dryLarge.albedo, rippleLarge.albedo, blendLarge);
    // Large sample only changes the brightness of the small one (no blur), plus a slow colour drift
    float lumSmall = max(Luminance(albedoSmall), 1e-3);
    o.albedo = albedoSmall * lerp(1.0, Luminance(albedoLarge) / max(Luminance(lerp(dryLarge.albedo, rippleLarge.albedo, 0.5)), 1e-3), 0.5);
    o.albedo *= _SandTint.rgb * lerp(0.85, 1.12, saturate(0.5 + variation));
    o.gradient = lerp(dry.gradient, ripple.gradient, blendRipple)
               + lerp(dryLarge.gradient, rippleLarge.gradient, blendLarge) * (_SandLargeStrength / _SandLargeScale);
    o.height = lerp(dry.height, ripple.height, blendRipple);
    o.roughness = lerp(dry.roughness, ripple.roughness, blendRipple);
    o.ao = lerp(1.0, lerp(dry.ao, ripple.ao, blendRipple), _SandAOStrength);
    return o;
}

void FT_Surface(FragInputs input, out float3 baseColor, out float smoothness, out float2 detailGradient, out float ao)
{
    float2 p = input.texCoord0.xy;               // terrain xz (m)
    float height = input.texCoord1.x;
    float slope = input.texCoord1.y;
    float3 biome = input.color.rgb;
    biome /= max(biome.r + biome.g + biome.b, 1e-4);

    float3 large = FT_FbmD(p, 900.0, 3);         // patches of colour across the landscape
    float3 detail = FT_FbmD(p, max(_DetailScale, 0.01), 6);
    detailGradient = detail.yz * _DetailStrength;

    // Rock where it is steep, in every biome
    float rockMask = smoothstep(_RockSlope - 0.08, _RockSlope + 0.08, slope + large.x * 0.06 + detail.x * 0.04);
    float3 rock = lerp(_RockDarkColor.rgb, _RockColor.rgb, saturate(0.5 + large.x + detail.x * 0.6));

    // Desert: the scanned sand layers
    FTLayer desert = FT_Desert(p, slope, input.color.a, large.x);
    float3 sand = desert.albedo;
    detailGradient = lerp(detailGradient, desert.gradient, biome.r);
    ao = lerp(1.0, desert.ao, biome.r);

    // Mountains: dry grass on the gentle slopes, rock above
    float3 mountain = lerp(_DryGrassColor.rgb * (0.85 + 0.3 * large.x), rock, saturate(slope * 2.5));

    // Valley: fields with hedges
    float hedge;
    float3 fields = FT_Fields(p, hedge);
    fields = lerp(fields, _GrassColor.rgb * 0.45, hedge * 0.8);

    baseColor = sand * biome.r + mountain * biome.g + fields * biome.b;
    baseColor = lerp(baseColor, rock, rockMask * (1.0 - biome.b * 0.7));
    baseColor *= lerp(0.9 + 0.2 * detail.x, 1.0, biome.r);
    smoothness = lerp(_TerrainSmoothness, 1.0 - desert.roughness, biome.r);
}

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    ZERO_INITIALIZE(SurfaceData, surfaceData);
    surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD;
    surfaceData.specularOcclusion = 1.0;
    surfaceData.ambientOcclusion = 1.0;
    surfaceData.ior = 1.0;
    surfaceData.atDistance = 1.0;
    surfaceData.transmittanceColor = 1.0;

#ifdef FT_FULL_SURFACE
    float3 baseColor;
    float2 detailGradient;
    float smoothness, ao;
    FT_Surface(input, baseColor, smoothness, detailGradient, ao);
    surfaceData.ambientOcclusion = ao;

    // Fine relief on top of the mesh normal: tangent = terrain +X, bitangent = terrain +Z
    float3 T = input.tangentToWorld[0];
    float3 B = input.tangentToWorld[1];
    float3 N = input.tangentToWorld[2];
    float3 normalWS = normalize(N - T * detailGradient.x - B * detailGradient.y);

    surfaceData.baseColor = baseColor;
    surfaceData.perceptualSmoothness = smoothness;
    surfaceData.normalWS = normalWS;
    surfaceData.geomNormalWS = N;
    surfaceData.tangentWS = Orthonormalize(T, normalWS);
#else
    surfaceData.baseColor = 0.5;
    surfaceData.normalWS = float3(0.0, 1.0, 0.0);
    surfaceData.geomNormalWS = surfaceData.normalWS;
    surfaceData.tangentWS = float3(1.0, 0.0, 0.0);
#endif

#if defined(DEBUG_DISPLAY) && defined(FT_FULL_SURFACE)
    ApplyDebugToSurfaceData(input.tangentToWorld, surfaceData);
#endif

    float4 texCoord1 = 0.0, texCoord2 = 0.0;
    InitBuiltinData(posInput, 1.0, surfaceData.normalWS, -surfaceData.normalWS, texCoord1, texCoord2, builtinData);
    PostInitBuiltinData(V, posInput, surfaceData, builtinData);

    RAY_TRACING_OPTIONAL_ALPHA_TEST_PASS
}

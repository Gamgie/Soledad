// Surface of Soledad/HillGrass: a scanned grass set (Megascans) with real relief. The ground (shell 0) is the scan
// itself; every shell above it keeps the scan only where its height map reaches the shell (shells shifted by the lean
// of the blades), so up close the scanned blades stand up with parallax and depth, the way the scan's own preview
// shows them with displacement. Anti-tiling: a second sample of the scan, rotated and scaled, takes over by large
// patches, blended by height. With distance and when a blade gets smaller than a pixel the relief flattens into the
// ground, shaded by the height map, so the lawn keeps one tone from the insect to the horizon.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

// The two samples of the scan: as is, and rotated, scaled and shifted
void HG_TileUVs(float2 p, out float2 a, out float2 b)
{
    float size = max(_TextureSize, 0.01);
    a = p / size;
    const float s = 0.6442, c = 0.7648;     // 40 degrees
    b = float2(c * p.x - s * p.y, s * p.x + c * p.y) / (size * 1.37) + float2(0.37, 0.61);
}

// Height of the scan from 0 (ground) to 1 (tips)
float HG_Height(float raw)
{
    return saturate((raw - _HeightMin) / max(_HeightMax - _HeightMin, 1e-3));
}

// Weight of sample B: by large patches, the higher of the two scans wins at the border (no blurry cross-fade)
float HG_BlendB(float heightA, float heightB, float tileBreak)
{
    return saturate((tileBreak - 0.5) * _TileBreakSharpness + (heightB - heightA) * 3.0 + 0.5);
}

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    float2 uv = input.texCoord0.xy;
    float2 lean = input.texCoord1.xy;
    float tileBreak = input.texCoord2.x;
    float patch = input.texCoord2.y;
    float h = _ShellHeight;

    // Shell fade with distance and when a blade (~3 mm) gets smaller than a pixel. The pixel footprint is estimated
    // from the distance and the viewing angle, smooth over the whole surface (screen derivatives of the coordinates
    // are constant per triangle and would show the facets)
    float distance = length(input.positionRWS);
    float facing = max(abs(dot(input.tangentToWorld[2], V)), 0.15);
    float footprint = distance * 2.0 / (UNITY_MATRIX_P[1][1] * _ScreenSize.y) / sqrt(facing);
    float fadeDistance = saturate((distance - _FadeStart) / max(_FadeEnd - _FadeStart, 1e-3));
    float fadePixel = saturate((footprint * 300.0 - 0.25) / 0.5);
    float fade = (1.0 - max(fadeDistance, fadePixel)) * (1.0 - _GroundOnly);    // 1 = full shells, 0 = ground only

    // Soil patch (1 = bare soil): an irregular disc around the roots, the grass shortening across its edge
    float soil = 0.0;
    if (_SoilRadius > 0.0)
    {
        float2 ds = uv - _SoilCenter.xy;
        float dist = length(ds);
        if (dist < _SoilRadius + _SoilEdge + _SoilEdgeNoise)
        {
            float n = (HG_Fbm(uv * 0.9 + 3.7) * 0.65 + HG_Fbm(uv * 3.1 + 8.2) * 0.35 - 0.5) * 2.0 * _SoilEdgeNoise;
            soil = 1.0 - smoothstep(_SoilRadius - _SoilEdge, _SoilRadius + _SoilEdge, dist + n);
        }
    }
    fade *= saturate(1.0 - soil * (1.0 + _GrassInSoil));

    float2 p = uv + lean * (h * h);
    float2 uvA, uvB;
    HG_TileUVs(p, uvA, uvB);
    float heightA = HG_Height(SAMPLE_TEXTURE2D(_GrassHeightMap, sampler_trilinear_repeat_aniso8, uvA).r);
    float heightB = HG_Height(SAMPLE_TEXTURE2D(_GrassHeightMap, sampler_trilinear_repeat_aniso8, uvB).r);
    float wB = HG_BlendB(heightA, heightB, tileBreak);
    float height = lerp(heightA, heightB, wB);
    // The relief flattens as the shells fade (each pixel keeps the shells under its own height x fade): no ring where
    // a whole shell would stop
    if (h > 0.0)
        clip(height * fade - h);

    ZERO_INITIALIZE(SurfaceData, surfaceData);
    surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD;
    surfaceData.specularOcclusion = 1.0;
    surfaceData.ambientOcclusion = 1.0;
    surfaceData.ior = 1.0;
    surfaceData.atDistance = 1.0;
    surfaceData.transmittanceColor = 1.0;

#ifdef HG_FULL_SURFACE
    float3 T = input.tangentToWorld[0];
    float3 B = input.tangentToWorld[1];
    float3 N = input.tangentToWorld[2];

    float3 albedo = lerp(SAMPLE_TEXTURE2D(_GrassAlbedo, sampler_trilinear_repeat_aniso8, uvA).rgb,
                         SAMPLE_TEXTURE2D(_GrassAlbedo, sampler_trilinear_repeat_aniso8, uvB).rgb, wB);
    float3 normalTS = lerp(UnpackNormalMapRGorAG(SAMPLE_TEXTURE2D(_GrassNormal, sampler_trilinear_repeat_aniso8, uvA), 1.0),
                           UnpackNormalMapRGorAG(SAMPLE_TEXTURE2D(_GrassNormal, sampler_trilinear_repeat_aniso8, uvB), 1.0), wB);
    float roughness = lerp(SAMPLE_TEXTURE2D(_GrassRoughness, sampler_trilinear_repeat_aniso8, uvA).r,
                           SAMPLE_TEXTURE2D(_GrassRoughness, sampler_trilinear_repeat_aniso8, uvB).r, wB);
    float ao = lerp(SAMPLE_TEXTURE2D(_GrassAO, sampler_trilinear_repeat_aniso8, uvA).r,
                    SAMPLE_TEXTURE2D(_GrassAO, sampler_trilinear_repeat_aniso8, uvB).r, wB);

    // Colour: the scan, tinted, a little more or less saturated, light (drier) and dark patches over metres
    float luma = Luminance(albedo);
    albedo = lerp(albedo, luma / max(Luminance(_RecolorTarget.rgb), 1e-3) * _RecolorTarget.rgb, _Recolor);
    luma = Luminance(albedo);
    albedo = max(lerp(luma.xxx, albedo, _Saturation), 0.0) * _GrassTint.rgb;
    float lightPatch = saturate((patch - 0.5) * 2.5) * _PatchAmount;
    float darkPatch = saturate((0.5 - patch) * 2.5) * _PatchAmount;
    albedo = lerp(albedo, Luminance(albedo) / max(Luminance(_PatchColor.rgb), 1e-3) * _PatchColor.rgb, lightPatch);
    albedo *= 1.0 - 0.5 * darkPatch;

    // Depth in the grass, from the blade's own height, so the lawn has the same tone whether its shells are drawn or
    // not: a shell is lit by where it stands in the flattened relief (its top = the pixel's height), the ground by the
    // height map (it shows the tops of the blades where there are no shells, the gaps between them under the shells)
    // (h / fade is quantized by the shell count, by up to 1 / (shells x fade): weighted by fade, the quantization stays
    // below 1 / shells, or rings show where the relief flattens)
    float shellDepth = h > 0.0 ? lerp(height, saturate(h / max(fade, 1e-3)), fade) : height;
    float depthShade = lerp(1.0 - _SelfShadow, 1.0, shellDepth);
    surfaceData.ambientOcclusion = lerp(1.0, ao, _AOStrength) * depthShade;
    albedo *= lerp(1.0, depthShade, 0.5);

    if (soil > 0.0)
    {
        float2 suv = uv / max(_SoilSize, 0.01);
        float3 soilCol = SAMPLE_TEXTURE2D(_SoilAlbedo, sampler_trilinear_repeat_aniso8, suv).rgb;
        float3 litter = SAMPLE_TEXTURE2D(_LitterAlbedo, sampler_trilinear_repeat_aniso8, suv * 0.83 + 0.31).rgb;
        float lit = saturate((HG_Fbm(uv * 1.7 + 9.1) - 0.55) * 4.0) * _LitterAmount;
        soilCol = lerp(soilCol, litter, lit) * _SoilTint.rgb;
        float3 soilN = UnpackNormalMapRGorAG(SAMPLE_TEXTURE2D(_SoilNormal, sampler_trilinear_repeat_aniso8, suv), 1.0);
        // grass blades stay on top of the soil where the shells are still drawn: blend the ground only
        float w = h > 0.0 ? soil * 0.35 : soil;     // the lower blades get a little dusty too
        albedo = lerp(albedo, soilCol, w);
        normalTS = lerp(normalTS, soilN, w);
        roughness = lerp(roughness, 0.9, w);
        surfaceData.ambientOcclusion = lerp(surfaceData.ambientOcclusion, 1.0, w * 0.7);
    }

    float3 normalWS = normalize(T * normalTS.x * _NormalStrength + B * normalTS.y * _NormalStrength + N * max(normalTS.z, 0.1));

    float sheen = _Sheen * pow(1.0 - saturate(dot(N, V)), _SheenPower);
    albedo = lerp(albedo, _SheenColor.rgb, saturate(sheen));

    surfaceData.baseColor = saturate(albedo);
    surfaceData.perceptualSmoothness = 1.0 - saturate(roughness * _RoughnessScale);
    surfaceData.normalWS = normalWS;
    surfaceData.geomNormalWS = N;
    surfaceData.tangentWS = Orthonormalize(T, normalWS);
#else
    surfaceData.baseColor = 0.5;
    surfaceData.normalWS = float3(0.0, 1.0, 0.0);
    surfaceData.geomNormalWS = surfaceData.normalWS;
    surfaceData.tangentWS = float3(1.0, 0.0, 0.0);
#endif

#if defined(DEBUG_DISPLAY) && defined(HG_FULL_SURFACE)
    ApplyDebugToSurfaceData(input.tangentToWorld, surfaceData);
#endif

    float4 texCoord1 = 0.0, texCoord2 = 0.0;
    InitBuiltinData(posInput, 1.0, surfaceData.normalWS, -surfaceData.normalWS, texCoord1, texCoord2, builtinData);
    PostInitBuiltinData(V, posInput, surfaceData, builtinData);

    RAY_TRACING_OPTIONAL_ALPHA_TEST_PASS
}

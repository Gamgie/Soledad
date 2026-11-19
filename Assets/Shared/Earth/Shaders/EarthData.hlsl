// Surface of Soledad/Earth: an HDRP Lit standard surface whose colour, smoothness and emission are looked up per pixel
// from the direction on the unit sphere (object space: north pole +Y, Greenwich -X, 90 degrees east -Z), so the globe
// stays round and seamless whatever its mesh density. Replaces LitData.hlsl.
//   - Ground: day map, ocean / land smoothness from the water mask (the sun glints on the sea only).
//   - Clouds: cloud map over the ground (colour, smoothness), drifting by _CloudOffset turns.
//   - Night: city lights where the sun is below the horizon (from _EarthSunDirection), dimmed under the clouds.
//   - Atmosphere: a thin coloured rim on the day side, towards the limb.

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

void EA_Surface(FragInputs input, float3 V, out float3 baseColor, out float smoothness, out float3 emission, out float3 normalWS)
{
    float3 dirOS = normalize(TransformWorldToObject(input.positionRWS));
    normalWS = normalize(TransformObjectToWorldNormal(dirOS));

    // Equirectangular coordinates; two versions of u with their wrap on opposite meridians, the pixel keeps the one
    // that does not jump across its neighbours (no seam line where u wraps)
    float lon = atan2(-dirOS.z, -dirOS.x);
    float lat = asin(clamp(dirOS.y, -1.0, 1.0));
    float u = lon * INV_TWO_PI + 0.5;
    float v = lat * INV_PI + 0.5;
    float u1 = frac(u);
    float u2 = frac(u + 0.5) - 0.5;
    float uu = fwidth(u1) <= fwidth(u2) + 1e-6 ? u1 : u2;
    float2 uv = float2(uu, v);
    float2 cloudUV = float2(uu - _CloudOffset, v);

    float3 day = SAMPLE_TEXTURE2D(_DayMap, sampler_trilinear_repeat_aniso8, uv).rgb * _DayTint.rgb;
    float water = SAMPLE_TEXTURE2D(_WaterMask, sampler_trilinear_repeat_aniso8, uv).r;
    float cloud = saturate(SAMPLE_TEXTURE2D(_CloudMap, sampler_trilinear_repeat_aniso8, cloudUV).r * _CloudOpacity);
    float3 lights = SAMPLE_TEXTURE2D(_NightMap, sampler_trilinear_repeat_aniso8, uv).rgb;

    baseColor = lerp(day, _CloudColor.rgb, cloud);
    smoothness = lerp(lerp(_LandSmoothness, _OceanSmoothness, water), _CloudSmoothness, cloud);

    // Sun height above this point's horizon (sine): the lights come up past the terminator
    float sunHeight = dot(normalWS, normalize(_EarthSunDirection.xyz + float3(0.0, 1e-6, 0.0)));
    float night = smoothstep(_TerminatorStart, _TerminatorEnd, sunHeight);
    lights = max(lights - _CityLightsBlackLevel, 0.0) / max(1.0 - _CityLightsBlackLevel, 1e-3);
    float3 city = lights * _CityLightsColor.rgb * _CityLightsIntensity * night * (1.0 - cloud * _CityLightsUnderClouds);

    float rim = pow(1.0 - saturate(dot(normalWS, V)), _AtmospherePower);
    float3 atmosphere = _AtmosphereColor.rgb * _AtmosphereIntensity * rim * saturate(sunHeight * 2.0 + 0.3);

    emission = (city + atmosphere) * lerp(GetInverseCurrentExposureMultiplier(), 1.0, _CityLightsExposureWeight);
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
    float3 emission = 0.0;

#ifdef EARTH_FULL_SURFACE
    float3 baseColor, normalWS;
    float smoothness;
    EA_Surface(input, V, baseColor, smoothness, emission, normalWS);
    surfaceData.baseColor = baseColor;
    surfaceData.perceptualSmoothness = smoothness;
    surfaceData.normalWS = normalWS;
    surfaceData.geomNormalWS = normalWS;
    surfaceData.tangentWS = Orthonormalize(input.tangentToWorld[0], normalWS);
#else
    surfaceData.baseColor = 0.5;
    surfaceData.normalWS = float3(0.0, 1.0, 0.0);
    surfaceData.geomNormalWS = surfaceData.normalWS;
    surfaceData.tangentWS = float3(1.0, 0.0, 0.0);
#endif

#if defined(DEBUG_DISPLAY) && defined(EARTH_FULL_SURFACE)
    ApplyDebugToSurfaceData(input.tangentToWorld, surfaceData);
#endif

    float4 texCoord1 = 0.0, texCoord2 = 0.0;
    InitBuiltinData(posInput, 1.0, surfaceData.normalWS, -surfaceData.normalWS, texCoord1, texCoord2, builtinData);
    builtinData.emissiveColor = emission;
    PostInitBuiltinData(V, posInput, surfaceData, builtinData);

    RAY_TRACING_OPTIONAL_ALPHA_TEST_PASS
}

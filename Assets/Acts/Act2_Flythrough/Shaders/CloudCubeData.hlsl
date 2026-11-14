// Surface of Soledad/CloudCube: a fake volumetric cloud shaped like a cube. The pixel ray-marches the hull through a
// density field = a rounded box eroded by a 3D noise (fluffy, frayed edges), gets its opacity from the accumulated
// extinction, then hands HDRP's Lit lighting one surface: the point where the cloud becomes visible along the ray,
// with the normal of the density there. So the cloud is lit by the sun and the sky and receives shadows like a
// solid, plus a cheap forward-scattering glow (fake subsurface) when the sun is behind it.

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

float CC_Hash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Value noise in [-1, 1]
float CC_Noise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(CC_Hash(i + float3(0, 0, 0)), CC_Hash(i + float3(1, 0, 0)), f.x),
                     lerp(CC_Hash(i + float3(0, 1, 0)), CC_Hash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(CC_Hash(i + float3(0, 0, 1)), CC_Hash(i + float3(1, 0, 1)), f.x),
                     lerp(CC_Hash(i + float3(0, 1, 1)), CC_Hash(i + float3(1, 1, 1)), f.x), f.y), f.z) * 2.0 - 1.0;
}

// Billowy fractal noise in about [-1, 1]: round puffs with creases between them, like cumulus
float CC_Fbm(float3 p)
{
    float sum = 0.0, amp = 0.6;
    for (int o = 0; o < 4; o++)
    {
        float n = CC_Noise(p);
        sum += amp * lerp(n, abs(n) * 2.0 - 1.0, 0.5);    // half billow: round puffs, soft creases
        p = p * 2.07 + 11.7;
        amp *= 0.48;
    }
    return sum;
}

// Cloud density at q (metres, cube centre = 0, cube axes), 0 outside .. 1 deep inside
float CC_Density(float3 q, float3 halfSize)
{
    float r = min(CC_ROUND, min(halfSize.x, min(halfSize.y, halfSize.z)) * 0.9);
    float3 d3 = abs(q) - (halfSize - r);
    float box = length(max(d3, 0.0)) + min(max(d3.x, max(d3.y, d3.z)), 0.0) - r;
    float n = CC_Fbm(q / max(CC_PUFF, 0.01) + _CloudSeed * 13.17);
    // Mostly outward puffs (the billowy noise averages below 0: shift it up, capped to stay inside the hull), and only
    // small dents into the cube: no holes in the faces
    n = min(n + 0.4, 1.0);
    n = n > 0.0 ? n : n * _CloudDent;
    return saturate((-box + n * CC_FLUFF) / max(CC_SOFT, 0.01));
}

// Ray / box (centred) intersection: returns (tNear, tFar)
float2 CC_RayBox(float3 origin, float3 dir, float3 halfSize)
{
    float3 inv = 1.0 / (abs(dir) > 1e-6 ? dir : 1e-6);
    float3 t0 = (-halfSize - origin) * inv;
    float3 t1 = (halfSize - origin) * inv;
    float3 tMin = min(t0, t1), tMax = max(t0, t1);
    return float2(max(tMin.x, max(tMin.y, tMin.z)), min(tMax.x, min(tMax.y, tMax.z)));
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
    surfaceData.baseColor = _CloudColor.rgb;
    surfaceData.perceptualSmoothness = _CloudSmoothness;
    float alpha = 1.0;
    float3 emissive = 0.0;

#ifdef CC_FULL_SURFACE
    // Work in the cube's metric space: its axes, metres, centre at 0
    float3 scale = max(CC_ObjectScale(), 1e-3);
    float3 halfSize = 0.5 * scale;
    cc_k = CC_SizeScale(scale);
    float3x3 rotation = float3x3(normalize(GetObjectToWorldMatrix()._m00_m10_m20),
                                 normalize(GetObjectToWorldMatrix()._m01_m11_m21),
                                 normalize(GetObjectToWorldMatrix()._m02_m12_m22));   // rows = cube axes in world
    float3 camera = TransformWorldToObject(float3(0.0, 0.0, 0.0)) * scale;              // camera-relative: camera at 0
    float3 pixel = TransformWorldToObject(posInput.positionWS) * scale;
    float3 dir = normalize(pixel - camera);

    float2 t = CC_RayBox(camera, dir, halfSize + CC_FLUFF);
    t.x = max(t.x, 0.0);
    t.y = max(t.y, t.x);

    int steps = clamp((int)_CloudSteps, 4, 96);
    float dt = (t.y - t.x) / steps;
    float jitter = CC_Hash(float3(posInput.positionSS.xy, _CloudSeed));    // breaks the step banding
    float transmittance = 1.0, thickness = 0.0;
    float3 surface = camera + dir * t.y;
    float found = 0.0;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 q = camera + dir * (t.x + (i + jitter) * dt);
        float density = CC_Density(q, halfSize);
        float step = exp(-density * _CloudDensity * dt);
        // First point where the cloud starts to show: lit as its surface. Placed inside the step where the opacity
        // crosses the threshold, so it does not jump from sample to sample (no bands on the lit side)
        float before = 1.0 - transmittance, after = 1.0 - transmittance * step;
        float first = (found < 0.5 && after > 0.03) ? 1.0 : 0.0;
        float within = saturate((0.03 - before) / max(after - before, 1e-5));
        surface = lerp(surface, q - dir * dt * (1.0 - within), first);
        found = max(found, first);
        transmittance *= step;
        thickness += density * dt;
    }
    clip(found - 0.5);
    alpha = 1.0 - transmittance;

    // Normal of the density where the cloud becomes visible, measured wide so creases do not turn black, then
    // blended with the smooth normal of the rounded cube (soft, cloud-like shading)
    float e = max(CC_SOFT * 1.5 + CC_PUFF * 0.15, 0.5);
    float3 gradient = float3(
        CC_Density(surface + float3(e, 0, 0), halfSize) - CC_Density(surface - float3(e, 0, 0), halfSize),
        CC_Density(surface + float3(0, e, 0), halfSize) - CC_Density(surface - float3(0, e, 0), halfSize),
        CC_Density(surface + float3(0, 0, e), halfSize) - CC_Density(surface - float3(0, 0, e), halfSize));
    float3 normalCube = dot(gradient, gradient) > 1e-8 ? -normalize(gradient) : -dir;
    // For shading only, the cube is much rounder than it looks: light turns gradually around its edges
    float minHalf = min(halfSize.x, min(halfSize.y, halfSize.z));
    float r = max(min(CC_ROUND + CC_FLUFF, minHalf), minHalf * _CloudShadeRounding);
    float3 boxNormal = normalize(surface - clamp(surface, -(halfSize - r), halfSize - r) + 1e-4);
    normalCube = normalize(lerp(boxNormal, normalCube, _CloudPuffShading));
    float3 normalWS = normalize(mul(normalCube, rotation));

    // Light the cloud where it is seen. The shadow lookup is pushed out along the normal, out of the cube's own
    // shadow-casting core, which otherwise speckles the lit side with dark dots
    float3 shading = surface + normalCube * (CC_FLUFF * 0.5 + CC_SOFT);
    posInput.positionWS = TransformObjectToWorld(shading / scale);

    surfaceData.normalWS = normalWS;
    surfaceData.geomNormalWS = normalWS;
    surfaceData.tangentWS = Orthonormalize(input.tangentToWorld[0], normalWS);
    // Deeper inside, less sky light reaches
    surfaceData.ambientOcclusion = lerp(1.0, 0.65, saturate(thickness / max(length(scale) * 0.5, 1.0)));

    // Fake subsurface: forward scattering of the sun through the cloud (Henyey-Greenstein, normalised to 1 at g = 0)
    #ifdef HAS_LIGHTLOOP
    if (_DirectionalLightCount > 0)
    {
        DirectionalLightData sun = _DirectionalLightDatas[0];
        float3 viewDir = normalize(mul(dir, rotation));
        float cosAngle = dot(viewDir, -sun.forward);
        float g = saturate(_CloudScatterPhase) * 0.95;
        float phase = (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * cosAngle, 1e-4), 1.5);
        emissive = sun.color * _CloudColor.rgb * (_CloudScatter * phase / PI) * alpha * exp(-thickness * _CloudDensity * 0.25);
    }
    #endif
#else
    surfaceData.normalWS = float3(0.0, 1.0, 0.0);
    surfaceData.geomNormalWS = surfaceData.normalWS;
    surfaceData.tangentWS = float3(1.0, 0.0, 0.0);
#endif

    float4 texCoord1 = 0.0, texCoord2 = 0.0;
    InitBuiltinData(posInput, alpha, surfaceData.normalWS, -surfaceData.normalWS, texCoord1, texCoord2, builtinData);
    builtinData.emissiveColor = emissive;
    PostInitBuiltinData(V, posInput, surfaceData, builtinData);

    RAY_TRACING_OPTIONAL_ALPHA_TEST_PASS
}

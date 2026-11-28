// Surface of Soledad/CloudCluster: the whole cluster of cubic clouds as one fake volume. Same idea and settings as
// Soledad/CloudCube (CloudCubeData.hlsl), but the density is the smooth union of all the cubes, so overlapping cubes
// melt into one cloud instead of cutting through each other. Each sample finds the nearest cube; the puffs come from
// that cube's own noise (its axes, its seed), so they move and turn with it.

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

float CC_Hash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

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

// Half billowy fractal noise: round puffs, soft creases
float CC_Fbm(float3 p)
{
    float sum = 0.0, amp = 0.6;
    for (int o = 0; o < 4; o++)
    {
        float n = CC_Noise(p);
        sum += amp * lerp(n, abs(n) * 2.0 - 1.0, 0.5);
        p = p * 2.07 + 11.7;
        amp *= 0.48;
    }
    return sum;
}

float CC_RoundBox(float3 q, float3 halfSize, float r)
{
    float3 d3 = abs(q) - (halfSize - r);
    return length(max(d3, 0.0)) + min(max(d3.x, max(d3.y, d3.z)), 0.0) - r;
}

// Smooth minimum: merges the cubes where they meet
float CC_SMin(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / max(k, 1e-3));
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// Union distance of the cubes at p (absolute world), and the nearest cube (index, local position)
float CC_Union(float3 p, out int nearest, out float3 nearestLocal)
{
    float d = 1e9, best = 1e9;
    nearest = 0;
    nearestLocal = 0.0;
    int count = (int)_CubeCount;
    [loop]
    for (int i = 0; i < count; i++)
    {
        float3 q = mul(_CubeWorldToLocal[i], float4(p, 1.0)).xyz;
        float k = _CubeHalf[i].w;
        float3 halfSize = _CubeHalf[i].xyz;
        float r = min(_CloudRoundness * k, min(halfSize.x, min(halfSize.y, halfSize.z)) * 0.9);
        float di = CC_RoundBox(q, halfSize, r);
        if (di < best)
        {
            best = di;
            nearest = i;
            nearestLocal = q;
        }
        d = CC_SMin(d, di, _CloudSoftness * k * 2.0);
    }
    return d;
}

// Cloud density at p (absolute world): 0 outside .. 1 inside. `gap` = distance (m) that is certainly empty around p,
// used to skip empty space while marching
float CC_Density(float3 p, out float gap)
{
    int i;
    float3 q;
    float d = CC_Union(p, i, q);
    gap = max(d - _CloudFluff * _CubeHalf[i].w * 1.6, 0.0);
    float k = _CubeHalf[i].w;
    float fluff = _CloudFluff * k;
    if (d > fluff) return 0.0;                                      // far outside every cube: skip the noise
    float n = CC_Fbm(q / max(_CloudNoiseSize * k, 0.01) + _CubeSeed[i].x * 13.17);
    n = min(n + 0.4, 1.0);                                          // mostly outward puffs
    n = n > 0.0 ? n : n * _CloudDent;                              // only small dents
    return saturate((-d + n * fluff) / max(_CloudSoftness * k, 0.01));
}

float CC_Density(float3 p)
{
    float gap;
    return CC_Density(p, gap);
}

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
    // Ray through the volume box (this object), in its own axes and metres
    float4x4 m = GetObjectToWorldMatrix();
    float3 scale = max(float3(length(m._m00_m10_m20), length(m._m01_m11_m21), length(m._m02_m12_m22)), 1e-3);
    float3x3 rotation = float3x3(normalize(m._m00_m10_m20), normalize(m._m01_m11_m21), normalize(m._m02_m12_m22));
    float3 camera = TransformWorldToObject(float3(0.0, 0.0, 0.0)) * scale;
    float3 pixel = TransformWorldToObject(posInput.positionWS) * scale;
    float3 dirBox = normalize(pixel - camera);
    float3 dirWS = normalize(mul(dirBox, rotation));
    float3 cameraWS = GetAbsolutePositionWS(float3(0.0, 0.0, 0.0));

    float2 t = CC_RayBox(camera, dirBox, 0.5 * scale);
    t.x = max(t.x, 0.0);
    t.y = max(t.y, t.x);

    // Steps inside the cloud are a fraction of the edge softness; empty space between and around the cubes is
    // skipped in one go (distance to the nearest cube). Samples = the most steps a pixel can take.
    int maxSteps = clamp((int)_CloudSteps, 4, 128);
    float k0 = _CubeHalf[0].w;
    float dt = max(_CloudSoftness * k0 * 0.75, 1.0);
    float jitter = CC_Hash(float3(posInput.positionSS.xy, 0.37));
    float transmittance = 1.0, thickness = 0.0;
    float found = 0.0;
    float travel = t.x + jitter * dt;
    float previous = t.x;
    float2 bracket = float2(t.y, t.y);       // the cloud becomes visible between these two distances along the ray
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        if (travel > t.y || transmittance < 0.01) break;
        float3 p = cameraWS + dirWS * travel;
        float gap;
        float density = CC_Density(p, gap);
        // Empty space is skipped, minus a per-pixel jitter: otherwise every pixel lands on the cloud at the same
        // distances and the steps show as contour lines
        float stride = density > 0.0 ? dt : max(dt, gap - jitter * dt);
        float step = exp(-density * _CloudDensity * stride);
        float first = (found < 0.5 && 1.0 - transmittance * step > 0.03) ? 1.0 : 0.0;
        bracket = lerp(bracket, float2(previous, travel), first);
        found = max(found, first);
        transmittance *= step;
        thickness += density * stride;
        previous = travel;
        travel += stride;
    }
    clip(found - 0.5);
    alpha = 1.0 - transmittance;

    // The surface, found exactly inside its step (bisection on the density), so its shading is smooth instead of
    // stepping in terraces
    [loop]
    for (int b = 0; b < 6; b++)
    {
        float middle = 0.5 * (bracket.x + bracket.y);
        float inside = CC_Density(cameraWS + dirWS * middle) > 0.12 ? 1.0 : 0.0;
        bracket = lerp(float2(middle, bracket.y), float2(bracket.x, middle), inside);
    }
    float3 surface = cameraWS + dirWS * (0.5 * (bracket.x + bracket.y));

    // Normal: density gradient measured wide, blended with the nearest cube's (very round) box normal
    int nearest;
    float3 q;
    CC_Union(surface, nearest, q);
    float k = _CubeHalf[nearest].w;
    float3 halfSize = _CubeHalf[nearest].xyz;
    float e = max(_CloudSoftness * k * 1.5 + _CloudNoiseSize * k * 0.15, 0.5);
    float3 gradient = float3(
        CC_Density(surface + float3(e, 0, 0)) - CC_Density(surface - float3(e, 0, 0)),
        CC_Density(surface + float3(0, e, 0)) - CC_Density(surface - float3(0, e, 0)),
        CC_Density(surface + float3(0, 0, e)) - CC_Density(surface - float3(0, 0, e)));
    float3 normalWS = dot(gradient, gradient) > 1e-8 ? -normalize(gradient) : -dirWS;
    float minHalf = min(halfSize.x, min(halfSize.y, halfSize.z));
    float r = max(min((_CloudRoundness + _CloudFluff) * k, minHalf), minHalf * _CloudShadeRounding);
    float3 boxNormalLocal = normalize(q - clamp(q, -(halfSize - r), halfSize - r) + 1e-4);
    float3 boxNormalWS = normalize(mul(boxNormalLocal, (float3x3)_CubeWorldToLocal[nearest]));   // local -> world
    normalWS = normalize(lerp(boxNormalWS, normalWS, _CloudPuffShading));

    // Light it where it is seen, the shadow lookup pushed out of the cubes' shadow-casting cores
    float3 shading = surface + normalWS * (_CloudFluff * k * 0.5 + _CloudSoftness * k);
    posInput.positionWS = GetCameraRelativePositionWS(shading);

    surfaceData.normalWS = normalWS;
    surfaceData.geomNormalWS = normalWS;
    surfaceData.tangentWS = Orthonormalize(input.tangentToWorld[0], normalWS);
    surfaceData.ambientOcclusion = lerp(1.0, 0.65, saturate(thickness / max(minHalf * 2.0, 1.0)));

    #ifdef HAS_LIGHTLOOP
    if (_DirectionalLightCount > 0)
    {
        DirectionalLightData sun = _DirectionalLightDatas[0];
        float cosAngle = dot(dirWS, -sun.forward);
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

    // Faded towards the draw distance, so a far cluster comes in softly instead of popping on the horizon
    alpha *= saturate(_CloudFade);
    emissive *= saturate(_CloudFade);

    float4 texCoord1 = 0.0, texCoord2 = 0.0;
    InitBuiltinData(posInput, alpha, surfaceData.normalWS, -surfaceData.normalWS, texCoord1, texCoord2, builtinData);
    builtinData.emissiveColor = emissive;
    PostInitBuiltinData(V, posInput, surfaceData, builtinData);

    RAY_TRACING_OPTIONAL_ALPHA_TEST_PASS
}

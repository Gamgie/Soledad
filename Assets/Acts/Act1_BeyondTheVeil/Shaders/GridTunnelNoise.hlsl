#ifndef SOLEDAD_GRID_TUNNEL_NOISE
#define SOLEDAD_GRID_TUNNEL_NOISE

// PCG 3D hash (Jarzynski & Olano, "Hash Functions for GPU Rendering", 2020)
uint3 GT_Pcg3d(uint3 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    return v;
}

float3 GT_Hash3(uint3 v)
{
    return float3(GT_Pcg3d(v) >> 8) * (1.0 / 16777216.0);
}

float3 GT_Gradient(int3 cell)
{
    return normalize(GT_Hash3(asuint(cell)) * 2.0 - 1.0 + 1e-5);
}

// 3D gradient noise, about -1 to 1
float GT_GradientNoise(float3 p)
{
    float3 fi = floor(p);
    float3 f = p - fi;
    int3 i = (int3)fi;
    float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

    float n000 = dot(GT_Gradient(i + int3(0, 0, 0)), f - float3(0, 0, 0));
    float n100 = dot(GT_Gradient(i + int3(1, 0, 0)), f - float3(1, 0, 0));
    float n010 = dot(GT_Gradient(i + int3(0, 1, 0)), f - float3(0, 1, 0));
    float n110 = dot(GT_Gradient(i + int3(1, 1, 0)), f - float3(1, 1, 0));
    float n001 = dot(GT_Gradient(i + int3(0, 0, 1)), f - float3(0, 0, 1));
    float n101 = dot(GT_Gradient(i + int3(1, 0, 1)), f - float3(1, 0, 1));
    float n011 = dot(GT_Gradient(i + int3(0, 1, 1)), f - float3(0, 1, 1));
    float n111 = dot(GT_Gradient(i + int3(1, 1, 1)), f - float3(1, 1, 1));

    float nx00 = lerp(n000, n100, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx11 = lerp(n011, n111, u.x);
    return lerp(lerp(nx00, nx10, u.y), lerp(nx01, nx11, u.y), u.z) * 1.4;
}

// Fractal sum, normalized so the amplitude stays about -1 to 1 whatever the number of harmonics
float GT_Fbm(float3 p, int octaves, float roughness, float lacunarity)
{
    float sum = 0.0;
    float amp = 1.0;
    float norm = 0.0;
    [loop]
    for (int o = 0; o < octaves; o++)
    {
        sum += GT_GradientNoise(p) * amp;
        norm += amp;
        amp *= roughness;
        p = p * lacunarity + float3(19.19, 7.77, 3.33);
    }
    return sum / max(norm, 1e-4);
}

// Displacement of a vertex, from its position in tunnel space only, so the vertices shared by two walls
// (the corners) move together and the tunnel never cracks.
float3 GT_NoiseDisplacement(float3 positionOS)
{
    if (_NoiseAmplitude == 0.0)
        return 0.0;

    float3 pt = positionOS + float3(0.0, 0.0, _TunnelOffset.x);
    float3 q = pt / max(_NoisePeriod, 1e-3);
    q.z /= max(_NoiseStretch, 1e-3);
    q += _NoiseSeed * float3(17.13, 31.71, 7.37) + _NoiseFlow.xyz * _NoiseTime;

    int octaves = clamp((int)round(_NoiseOctaves), 1, 8);
    float rough = _NoiseRoughness;
    float lac = _NoiseLacunarity;

    float3 inward = -normalize(float3(positionOS.xy, 0.0) + float3(1e-5, 0.0, 0.0));
    float3 disp = inward * GT_Fbm(q, octaves, rough, lac) * _NoiseRadial;

    [branch]
    if (_NoiseRadial < 0.999)
    {
        float3 free = float3(
            GT_Fbm(q + float3(31.4, 0.0, 0.0), octaves, rough, lac),
            GT_Fbm(q + float3(0.0, 47.2, 0.0), octaves, rough, lac),
            GT_Fbm(q + float3(0.0, 0.0, 63.1), octaves, rough, lac));
        disp += free * (1.0 - _NoiseRadial);
    }
    return disp * _NoiseAmplitude;
}

#endif

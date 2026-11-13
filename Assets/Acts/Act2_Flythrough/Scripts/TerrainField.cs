using System;
using Unity.Mathematics;
using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// Shape of the Act 2 landscape, laid out along the route (terrain +Z, in metres): desert, then mountains with a
    /// winding pass along the route, then the valley (a field plain between two ridges). Changing it rebuilds the
    /// terrain, so it is not animated.
    /// </summary>
    [Serializable]
    public struct TerrainShape
    {
        [Tooltip("Moves the whole landscape pattern.")]
        public float seed;

        [Header("Route (along terrain +Z)")]
        [Tooltip("Route distance (m) where the desert gives way to the mountains.")]
        public float desertEnd;
        [Tooltip("Route distance (m) where the mountains open onto the valley.")]
        public float mountainEnd;
        [Tooltip("Length of each landscape transition (m).")]
        public float transition;
        [Tooltip("Irregularity of the transition borders (m).")]
        public float borderWarp;
        [Tooltip("Side-to-side swing of the route (m). The mountain pass and the valley follow it.")]
        public float meanderAmplitude;
        [Tooltip("Length of one route swing (m).")]
        public float meanderWavelength;

        [Header("Desert")]
        public float duneHeight;
        [Tooltip("Distance between dune crests (m).")]
        public float duneWavelength;
        [Tooltip("Wind direction (degrees from the route).")]
        public float duneDirection;
        [Tooltip("Large rolling relief under the dunes (m).")]
        public float desertRelief;

        [Header("Mountains")]
        public float mountainHeight;
        [Tooltip("Size of the main mountain features (m).")]
        public float mountainScale;
        [Tooltip("Half-width of the pass carved along the route (m). 0 = no pass.")]
        public float passWidth;

        [Header("Valley")]
        [Tooltip("Width of the flat valley floor (m).")]
        public float valleyWidth;
        [Tooltip("Height of the ridges around the valley (m).")]
        public float valleyRidgeHeight;

        public static TerrainShape Default => new TerrainShape
        {
            seed = 0f,
            desertEnd = 3000f,
            mountainEnd = 7500f,
            transition = 1200f,
            borderWarp = 600f,
            meanderAmplitude = 500f,
            meanderWavelength = 6000f,
            duneHeight = 35f,
            duneWavelength = 170f,
            duneDirection = 25f,
            desertRelief = 60f,
            mountainHeight = 1100f,
            mountainScale = 2600f,
            passWidth = 700f,
            valleyWidth = 9000f,
            valleyRidgeHeight = 700f,
        };
    }

    /// <summary>Height and biome of the Act 2 landscape. Pure functions, usable from Burst jobs and from managed code.</summary>
    public static class TerrainField
    {
        /// <summary>X of the route centre line at route distance z.</summary>
        public static float RouteX(float z, in TerrainShape s)
        {
            float k = 2f * math.PI / math.max(s.meanderWavelength, 1f);
            return s.meanderAmplitude * (math.sin(z * k) + 0.35f * math.sin(z * k * 2.3f + 1.7f));
        }

        /// <summary>Slope dx/dz of the route centre line, to steer along it.</summary>
        public static float RouteSlope(float z, in TerrainShape s)
        {
            float k = 2f * math.PI / math.max(s.meanderWavelength, 1f);
            return s.meanderAmplitude * k * (math.cos(z * k) + 0.35f * 2.3f * math.cos(z * k * 2.3f + 1.7f));
        }

        /// <summary>Weights of desert, mountains and valley at a terrain point (sum = 1).</summary>
        public static float3 Biome(float2 p, in TerrainShape s)
        {
            float2 q = p + s.seed * 1013f;
            float u = p.y + s.borderWarp * Fbm(q / 4000f, 3);
            float half = math.max(s.transition, 1f) * 0.5f;
            float desert = 1f - math.smoothstep(s.desertEnd - half, s.desertEnd + half, u);
            float valley = math.smoothstep(s.mountainEnd - half, s.mountainEnd + half, u);
            float mountains = math.max(0f, 1f - desert - valley);
            return new float3(desert, mountains, valley);
        }

        /// <summary>Ground height (m) at a terrain point.</summary>
        public static float Height(float2 p, in TerrainShape s, out float3 biome)
        {
            biome = Biome(p, s);
            float2 q = p + s.seed * 1013f;
            float h = 0f;
            if (biome.x > 0f) h += biome.x * Desert(q, s);
            if (biome.y > 0f) h += biome.y * Mountains(p, q, s);
            if (biome.z > 0f) h += biome.z * Valley(p, q, s);
            return h;
        }

        public static float Height(float2 p, in TerrainShape s) => Height(p, s, out _);

        static float Desert(float2 q, in TerrainShape s)
        {
            math.sincos(math.radians(s.duneDirection), out float sin, out float cos);
            float2 wind = new float2(sin, cos);
            float lambda = math.max(s.duneWavelength, 1f);

            // Transverse dunes: long gentle windward slope, short steep slip face, crests bent by a noise
            float d = math.dot(q, wind) + lambda * 0.6f * Fbm(q / (lambda * 4f), 3);
            float t = d / lambda - math.floor(d / lambda);
            float profile = t < 0.78f ? math.smoothstep(0f, 1f, t / 0.78f) : 1f - math.smoothstep(0f, 1f, (t - 0.78f) / 0.22f);
            float amplitude = 0.55f + 0.45f * Fbm(q / (lambda * 9f), 2);

            // Smaller dunes across the big ones
            float2 cross = new float2(wind.y, -wind.x);
            float d2 = math.dot(q, cross) * 1.7f + lambda * 0.3f * Fbm(q / (lambda * 2f) + 31f, 2);
            float t2 = d2 / (lambda * 0.37f);
            float small = 0.5f + 0.5f * math.sin(t2 * 2f * math.PI);

            return s.duneHeight * (profile * amplitude + 0.18f * small * amplitude) + s.desertRelief * Fbm(q / 5000f, 3);
        }

        static float Mountains(float2 p, float2 q, in TerrainShape s)
        {
            float scale = math.max(s.mountainScale, 1f);
            float ridges = Ridged(q / scale, 8);
            float h = s.mountainHeight * (ridges * 0.75f - 0.15f + 0.25f * Fbm(q / (scale * 3f), 3));

            // Winding pass along the route, so the flight can run between the peaks
            if (s.passWidth > 0f)
            {
                float side = math.abs(p.x - RouteX(p.y, s));
                float open = math.smoothstep(s.passWidth, s.passWidth * 3f, side);
                h = math.lerp(h * 0.12f + 20f * Fbm(q / 400f, 3), h, open);
            }
            return h;
        }

        static float Valley(float2 p, float2 q, in TerrainShape s)
        {
            float side = math.abs(p.x - RouteX(p.y, s));
            float half = math.max(s.valleyWidth, 1f) * 0.5f;
            float ridge = math.smoothstep(half, half + 5000f, side);
            float floorRelief = 4f * Fbm(q / 900f, 3);
            return floorRelief + ridge * s.valleyRidgeHeight * (0.4f + Ridged(q / 2500f, 6));
        }

        /// <summary>Fractal simplex noise, roughly in [-1, 1].</summary>
        public static float Fbm(float2 p, int octaves)
        {
            float sum = 0f, amp = 0.5f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * noise.snoise(p);
                p = new float2(p.x * 1.6f - p.y * 1.2f, p.x * 1.2f + p.y * 1.6f) + 13.7f;   // x2, rotated
                amp *= 0.5f;
            }
            return sum;
        }

        /// <summary>Ridged multifractal, roughly in [0, 1.3]: sharp crests, smooth valleys.</summary>
        public static float Ridged(float2 p, int octaves)
        {
            float sum = 0f, amp = 0.6f, weight = 1f;
            for (int o = 0; o < octaves; o++)
            {
                float n = 1f - math.abs(noise.snoise(p));
                n *= n * weight;
                weight = math.saturate(n * 2f);
                sum += n * amp;
                p = new float2(p.x * 1.6f - p.y * 1.2f, p.x * 1.2f + p.y * 1.6f) + 7.3f;
                amp *= 0.5f;
            }
            return sum;
        }
    }
}

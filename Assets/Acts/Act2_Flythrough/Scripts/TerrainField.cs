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

        /// <summary>
        /// Ground height (m) at a terrain point, its biome weights, and in the desert where the point sits on its dune
        /// (0 = trough, 1 = crest), used by the shader to place the sand layers.
        /// </summary>
        public static float Height(float2 p, in TerrainShape s, out float3 biome, out float duneLevel)
        {
            biome = Biome(p, s);
            float2 q = p + s.seed * 1013f;
            float h = 0f;
            duneLevel = 0.5f;
            if (biome.x > 0f) h += biome.x * Desert(q, s, out duneLevel);
            if (biome.y > 0f) h += biome.y * Mountains(p, q, s);
            if (biome.z > 0f) h += biome.z * Valley(p, q, s);
            return h;
        }

        public static float Height(float2 p, in TerrainShape s, out float3 biome) => Height(p, s, out biome, out _);

        public static float Height(float2 p, in TerrainShape s) => Height(p, s, out _, out _);

        static float Desert(float2 q, in TerrainShape s, out float duneLevel)
        {
            float lambda = math.max(s.duneWavelength, 1f);
            float direction = math.radians(s.duneDirection);

            // Two wind regimes: the dune fields change orientation from one region to the next
            float regime = math.smoothstep(-0.2f, 0.2f, Fbm(q / 9000f + 71f, 2));

            // Dune size changes across the desert: fields of small dunes, medium dunes, big dune seas
            float sizeNoise = Fbm(q / 5500f + 19f, 2) * 2.2f;
            float small = math.saturate(-sizeNoise);
            float large = math.saturate(sizeNoise);
            float medium = math.saturate(1f - small - large);

            // x = height (scaled by wavelength), y = profile (0 trough .. 1 crest)
            float2 h = 0f;
            if (small > 0f)
                h += small * math.lerp(Dunes(q, direction, lambda * 0.45f, 0f), Dunes(q, direction + 0.9f, lambda * 0.4f, 11f), regime);
            if (medium > 0f)
                h += medium * math.lerp(Dunes(q, direction, lambda, 23f), Dunes(q, direction + 0.9f, lambda * 0.85f, 37f), regime);
            if (large > 0f)
                h += large * math.lerp(Dunes(q, direction, lambda * 2.3f, 41f), Dunes(q, direction + 0.9f, lambda * 2f, 53f), regime);

            // Draa: very long mega-dunes the smaller ones ride on
            float2 draa = Dunes(q, direction + 0.25f, lambda * 9f, 67f);

            duneLevel = math.saturate(h.y * 0.85f + draa.y * 0.15f);
            return s.duneHeight / lambda * (h.x + 0.25f * draa.x) + s.desertRelief * Fbm(q / 5000f, 3);
        }

        // One field of transverse dunes, height relative to a dune of the base wavelength: a long windward slope, a
        // sharp crest and a steep slip face whose position varies; crests bend and break into segments.
        static float2 Dunes(float2 q, float direction, float lambda, float seed)
        {
            math.sincos(direction, out float sin, out float cos);
            float2 wind = new float2(sin, cos);
            float2 along = new float2(cos, -sin);

            float d = math.dot(q, wind) + lambda * 0.7f * Fbm(q / (lambda * 3.5f) + seed, 3);
            float t = d / lambda - math.floor(d / lambda);
            float crest = 0.72f + 0.12f * Fbm(q / (lambda * 2f) + seed + 5f, 2);
            float windward = math.pow(math.saturate(t / crest), 1.6f);                    // concave, steepening
            float slip = math.pow(math.saturate(1f - (t - crest) / (1f - crest)), 0.75f); // steep slip face
            // Crest rounded over a few percent of the wavelength: sharp, but never thinner than the mesh can follow
            float profile = math.lerp(windward, slip, math.smoothstep(crest - 0.03f, crest + 0.03f, t));

            // Crest height varies along the dune: segments, gaps, barchan-like horns
            float segments = Fbm(new float2(math.dot(q, along) / (lambda * 2.2f), d / (lambda * 6f)) + seed + 9f, 3);
            float amplitude = math.saturate(0.55f + 0.9f * segments);
            return new float2(profile * amplitude * lambda, profile);     // taller when longer
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

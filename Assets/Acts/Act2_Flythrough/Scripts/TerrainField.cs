using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// Shape of the Act 2 landscape (terrain space, metres): desert everywhere, with mountain ranges where
    /// <see cref="MountainRange"/> splines are drawn (their footprint comes in through a <see cref="RangeField"/>).
    /// Changing it rebuilds the terrain, so it is not animated.
    /// </summary>
    [Serializable]
    public struct TerrainShape
    {
        [Tooltip("Moves the whole landscape pattern.")]
        public float seed;

        [Header("Route (along terrain +Z)")]
        [Tooltip("Side-to-side swing of the route centre line (m), along which monoliths and clouds are spread.")]
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

        [Header("Mountains (along the Mountain Range splines)")]
        [Tooltip("Size of the main mountain features (m): peaks and side valleys along a range.")]
        public float mountainScale;
        [Tooltip("How much of a range's height the ridged noise carves away (0 = smooth domes, 1 = deep side valleys).")]
        [Range(0f, 1f)] public float mountainRoughness;

        public static TerrainShape Default => new TerrainShape
        {
            seed = 0f,
            meanderAmplitude = 500f,
            meanderWavelength = 6000f,
            duneHeight = 35f,
            duneWavelength = 170f,
            duneDirection = 25f,
            desertRelief = 60f,
            mountainScale = 2600f,
            mountainRoughness = 0.6f,
        };
    }

    /// <summary>
    /// The mountain ranges rasterised on a grid (built by FlythroughTerrain from the MountainRange splines): per cell,
    /// x = range weight (0 outside, 1 on a crest) and y = envelope height (m). Sampled bilinearly, 0 outside the grid.
    /// Default (not created) = no ranges.
    /// </summary>
    public struct RangeField
    {
        [ReadOnly] public NativeArray<float2> cells;
        public float2 origin;
        public float cellSize;
        public int2 count;

        // Cubic B-spline over 4 x 4 cells: smooth slopes (bilinear left creases along the cell edges, which the
        // lighting showed as squares on the hills)
        public float2 Sample(float2 p)
        {
            if (!cells.IsCreated || cellSize <= 0f) return 0f;
            float2 g = (p - origin) / cellSize;
            int2 i = (int2)math.floor(g);
            if (math.any(i < 1) || math.any(i >= count - 2)) return 0f;
            float2 f = g - i;
            float2 f2 = f * f, f3 = f2 * f;
            float2 w0 = (1f - f) * (1f - f) * (1f - f) / 6f;
            float2 w1 = (3f * f3 - 6f * f2 + 4f) / 6f;
            float2 w2 = (-3f * f3 + 3f * f2 + 3f * f + 1f) / 6f;
            float2 w3 = f3 / 6f;
            float2 sum = 0f;
            for (int y = 0; y < 4; y++)
            {
                float wy = y == 0 ? w0.y : y == 1 ? w1.y : y == 2 ? w2.y : w3.y;
                int row = (i.y - 1 + y) * count.x + i.x - 1;
                float2 r = cells[row] * w0.x + cells[row + 1] * w1.x + cells[row + 2] * w2.x + cells[row + 3] * w3.x;
                sum += r * wy;
            }
            return sum;
        }
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

        /// <summary>
        /// Ground height (m) at a terrain point, its mountain weight (0 = desert, 1 = mountain ground, for the shader),
        /// and in the desert where the point sits on its dune (0 = trough, 1 = crest), used to place the sand layers.
        /// </summary>
        public static float Height(float2 p, in TerrainShape s, in RangeField ranges, out float mountain, out float duneLevel)
        {
            float2 q = p + s.seed * 1013f;
            float desert = Desert(q, s, out duneLevel);
            float2 range = ranges.Sample(p);
            mountain = 0f;
            if (range.y <= 0f) return desert;

            // The envelope (highest on the crest line, down to 0 at the range's edge) carved by ridged noise: peaks,
            // saddles and side valleys along the range
            float scale = math.max(s.mountainScale, 1f);
            float ridges = math.saturate(Ridged(q / scale, 7) * 0.8f + 0.1f * Fbm(q / (scale * 0.3f), 3));
            float carve = math.lerp(1f, ridges, math.saturate(s.mountainRoughness));
            float h = range.y * carve;
            // Dunes die out under the mountains, the range rises from the sand
            float under = math.smoothstep(0.05f, 0.45f, range.x);
            mountain = math.smoothstep(0.1f, 0.5f, range.x);
            return math.lerp(desert, desert * 0.3f, under) + h;
        }

        public static float Height(float2 p, in TerrainShape s, in RangeField ranges) => Height(p, s, ranges, out _, out _);

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

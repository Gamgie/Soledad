using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// The monolith manager of Act 2: spreads <see cref="count"/> <see cref="Monolith"/>s (copies of
    /// <see cref="monolithPrefab"/>) over the landscape, standing on the ground, away from the flight line, all from
    /// the same base settings, each varied at random by up to <see cref="variation"/> and turned at random.
    ///
    /// Lives under the act's World object, at its origin: the monoliths are placed in terrain space (the landscape
    /// of <see cref="terrain"/>), so they move with the floating origin. They are generated (deterministic for a given
    /// seed), never saved, and follow every change made here.
    ///
    /// Each monolith has its own reflection probe, but refreshing one costs about a full low-resolution render per
    /// six frames: in Play mode only the <see cref="liveReflections"/> nearest grown monoliths keep a live
    /// reflection, the others keep their last one.
    /// </summary>
    [ExecuteAlways]
    public class MonolithField : MonoBehaviour
    {
        [Header("Spread (monoliths regenerated on change)")]
        [Range(0, 200)] public int count = 24;
        [Tooltip("Along the route (terrain +Z, m): from, to.")]
        public Vector2 routeRange = new Vector2(500f, 16000f);
        [Tooltip("How dense the monoliths are along the route: left = start of the range, right = end. Only the " +
                 "shape counts (relative density): rising = few at first, more and more towards the end.")]
        public AnimationCurve density = new AnimationCurve(new Keyframe(0f, 0.05f, 0f, 0f), new Keyframe(1f, 1f, 2f, 0f));
        [Tooltip("Width of the band (m) across the route centre line.")]
        public float width = 6000f;
        [Tooltip("No monolith closer than this (m) to the route centre line, so the bird flies between them.")]
        public float routeClearance = 200f;
        [Tooltip("Random variation of every monolith value around the values below (0.2 = +-20%).")]
        [Range(0f, 0.5f)] public float variation = 0.2f;
        public int seed = 1;

        [Header("Shape")]
        [Tooltip("Side (m) of the triangle.")]
        public float side = 30f;
        [Tooltip("Height (m).")]
        public float height = 120f;
        [Tooltip("Radius (m) of the long vertical edges.")]
        public float cornerRadius = 2.5f;
        [Tooltip("Radius (m) of the edges of the top and bottom faces.")]
        public float edgeRadius = 1.5f;
        [Tooltip("Depth (m) the foot goes into the ground.")]
        public float sink = 4f;

        [Header("Growth")]
        public Monolith.GrowthStyle growthStyle = Monolith.GrowthStyle.Scale;
        [Tooltip("Depth (m) ahead of the camera along the route (Z) where a monolith starts to grow.")]
        public float growStart = 3500f;
        [Tooltip("Depth (m) ahead along the route (Z) where it is fully grown (0 = when the camera passes it).")]
        public float growEnd = 600f;
        [Tooltip("Once grown a monolith stays grown, even if the camera goes back. Off: it follows the depth both ways.")]
        public bool keepGrown = true;
        [Tooltip("Growth shown in Edit mode (0 = nothing, 1 = full size).")]
        [Range(0f, 1f)] public float editGrowth = 1f;

        [Header("Reflections (Play mode)")]
        [Tooltip("How many of the nearest grown monoliths have a reflection probe at all. The others reflect the sky " +
                 "(the same from far away). HDRP draws at most 32 probes on screen.")]
        [Range(0, 16)] public int reflectionProbes = 6;
        [Tooltip("Of those, how many of the nearest keep a live reflection (each costs about 1 ms of GPU); the others keep their last one.")]
        [Range(0, 6)] public int liveReflections = 2;

        [Header("References")]
        [Tooltip("The landscape (its shape gives the ground height). Empty = found in the scene.")]
        [OSCQuery.DoNotExpose] public FlythroughTerrain terrain;
        [OSCQuery.DoNotExpose] public GameObject monolithPrefab;

        readonly List<Monolith> _monoliths = new List<Monolith>();
        Transform _container;
        int _spreadHash;
        bool _dirty;
        // Nearest grown monoliths this frame (indices), kept sorted by distance
        readonly int[] _nearest = new int[16];

        void OnEnable() => Generate();

        void OnDisable() => Clear();

        void OnValidate() => _dirty = true;

        void Update()
        {
            if (_dirty)
            {
                _dirty = false;
                if (SpreadHash() != _spreadHash || _monoliths.Count != count) Generate();
                else for (int i = 0; i < _monoliths.Count; i++) if (_monoliths[i] != null) Apply(_monoliths[i], i);
            }
            if (Application.isPlaying) ChooseLiveReflections();
        }

        // Only the few nearest grown monoliths have a probe, and the very nearest of them refresh it
        void ChooseLiveReflections()
        {
            int probes = Mathf.Min(reflectionProbes, _nearest.Length), found = 0;
            int live = Mathf.Min(liveReflections, probes);
            for (int i = 0; i < _monoliths.Count; i++)
            {
                Monolith m = _monoliths[i];
                if (m == null || m.growth <= 0.01f || probes == 0) continue;
                float d = m.Distance;
                int slot = found < probes ? found++ : probes;
                // Insertion into the sorted list of the nearest
                while (slot > 0 && _monoliths[_nearest[slot - 1]].Distance > d)
                {
                    if (slot < probes) _nearest[slot] = _nearest[slot - 1];
                    slot--;
                }
                if (slot < probes) _nearest[slot] = i;
            }
            for (int i = 0; i < _monoliths.Count; i++)
            {
                Monolith m = _monoliths[i];
                if (m == null) continue;
                int state = Monolith.ReflectionOff;
                for (int k = 0; k < found; k++)
                    if (_nearest[k] == i) { state = k < live ? Monolith.ReflectionLive : Monolith.ReflectionFrozen; break; }
                m.liveReflection = state;
            }
        }

        int SpreadHash()
        {
            unchecked
            {
                int h = count;
                h = h * 31 + routeRange.GetHashCode();
                if (density != null)
                    foreach (var key in density.keys)
                        h = h * 31 + key.time.GetHashCode() * 7 + key.value.GetHashCode() * 3 + key.inTangent.GetHashCode() + key.outTangent.GetHashCode() * 5;
                h = h * 31 + width.GetHashCode();
                h = h * 31 + routeClearance.GetHashCode();
                h = h * 31 + variation.GetHashCode();
                h = h * 31 + seed;
                h = h * 31 + side.GetHashCode();
                h = h * 31 + sink.GetHashCode();
                h = h * 31 + (terrain != null ? terrain.GetInstanceID() : 0);
                h = h * 31 + (monolithPrefab != null ? monolithPrefab.GetInstanceID() : 0);
                return h;
            }
        }

        /// <summary>Spreads the monoliths again from the settings.</summary>
        [ContextMenu("Regenerate Monoliths")]
        public void Generate()
        {
            Clear();
            _spreadHash = SpreadHash();
            if (monolithPrefab == null) return;
            if (terrain == null) terrain = FindFirstObjectByType<FlythroughTerrain>();
            TerrainShape shape = terrain != null ? terrain.shape : TerrainShape.Default;

            // Built inactive, so each monolith wakes up with its own values
            var container = new GameObject("Monoliths (generated)") { hideFlags = HideFlags.DontSave };
            container.SetActive(false);
            container.transform.SetParent(transform, false);
            _container = container.transform;

            // Density along the route: its cumulative sum, to draw positions that follow the curve
            BuildDensityTable();

            // Best-candidate sampling: each new monolith takes, of a few random places drawn along the density, the
            // one farthest from the others, the distances measured relative to the local spacing (so the dense end
            // stays dense instead of being evened out)
            var random = new System.Random(seed);
            var placed = new List<Vector2>();
            for (int i = 0; i < count; i++)
            {
                Vector2 best = Vector2.zero;
                float bestDistance = -1f;
                for (int candidate = 0; candidate < 12; candidate++)
                {
                    float along = SampleAlong((float)random.NextDouble());
                    float z = Mathf.Lerp(routeRange.x, routeRange.y, along);
                    float across = ((float)random.NextDouble() - 0.5f) * width;
                    across += Mathf.Sign(across) * routeClearance;
                    var p = new Vector2(TerrainField.RouteX(z, shape) + across, z);
                    float weight = DensityAt(along);
                    float nearest = float.MaxValue;
                    foreach (var other in placed) nearest = Mathf.Min(nearest, (other - p).sqrMagnitude * weight);
                    if (nearest > bestDistance) { bestDistance = nearest; best = p; }
                }
                placed.Add(best);

                var go = Instantiate(monolithPrefab, _container, false);
                go.name = $"Monolith {i + 1}";
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
                var monolith = go.GetComponent<Monolith>();
                Apply(monolith, i);

                // On the lowest ground under its foot, so it never floats on a slope (the sink hides the rest)
                float reach = side * 0.6f;
                float ground = float.MaxValue;
                for (int s = 0; s < 4; s++)
                {
                    float angle = s * Mathf.PI * 0.5f;
                    Vector2 offset = s == 0 ? Vector2.zero : new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
                    ground = Mathf.Min(ground, TerrainField.Height(new float2(best.x + offset.x, best.y + offset.y), shape));
                }
                go.transform.localPosition = new Vector3(best.x, ground, best.y);
                go.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                _monoliths.Add(monolith);
            }
            container.SetActive(true);
        }

        const int DensitySteps = 128;
        readonly float[] _cumulative = new float[DensitySteps + 1];

        // Relative density (> 0) at a fraction of the route range
        float DensityAt(float along)
        {
            float d = density != null && density.length > 0 ? density.Evaluate(Mathf.Clamp01(along)) : 1f;
            return Mathf.Max(d, 1e-3f);
        }

        void BuildDensityTable()
        {
            _cumulative[0] = 0f;
            for (int i = 0; i < DensitySteps; i++)
                _cumulative[i + 1] = _cumulative[i] + DensityAt((i + 0.5f) / DensitySteps);
        }

        // A fraction of the route range drawn from the density (inverse of the cumulative sum)
        float SampleAlong(float u)
        {
            float target = u * _cumulative[DensitySteps];
            int lo = 0, hi = DensitySteps;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_cumulative[mid] < target) lo = mid; else hi = mid;
            }
            float span = Mathf.Max(_cumulative[hi] - _cumulative[lo], 1e-6f);
            return (lo + (target - _cumulative[lo]) / span) / DensitySteps;
        }

        // The base settings, each varied by up to +-variation, the same for a given index and seed
        void Apply(Monolith m, int index)
        {
            if (m == null) return;
            var random = new System.Random(seed * 7919 + index * 104729);
            float V() => 1f + variation * ((float)random.NextDouble() * 2f - 1f);

            float scale = V();
            m.side = side * scale;
            m.height = height * V();
            m.cornerRadius = cornerRadius * scale;
            m.edgeRadius = edgeRadius * scale;
            m.sink = sink;
            m.growthStyle = growthStyle;
            m.growStart = growStart * V();
            m.growEnd = growEnd * V();
            m.editGrowth = editGrowth;
            m.keepGrown = keepGrown;
        }

        void Clear()
        {
            _monoliths.Clear();
            if (_container == null) _container = transform.Find("Monoliths (generated)");
            if (_container == null) return;
            if (Application.isPlaying) Destroy(_container.gameObject);
            else DestroyImmediate(_container.gameObject);
            _container = null;
        }
    }
}

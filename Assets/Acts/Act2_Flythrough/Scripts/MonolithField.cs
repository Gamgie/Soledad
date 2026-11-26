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
    /// of <see cref="terrain"/>). The spread runs only on demand (Spread Monoliths, Edit mode): the monoliths are then
    /// ordinary scene objects (prefab instances under the child "Monoliths"), saved with the act scene, moved,
    /// duplicated or deleted by hand like any object, and never placed again by this component (not on Play, not on
    /// load). The Shape and Growth settings still apply at once to the monoliths there (without moving them).
    ///
    /// Each monolith has its own reflection probe, but refreshing one costs about a full low-resolution render per
    /// six frames: in Play mode only the <see cref="liveReflections"/> nearest grown monoliths keep a live
    /// reflection, the others keep their last one.
    /// </summary>
    [ExecuteAlways]
    public class MonolithField : MonoBehaviour
    {
        public const string ContainerName = "Monoliths";

        [Header("Spread (only with Spread Monoliths: replaces the monoliths)")]
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

        [Header("Shape (applied to the monoliths there)")]
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

        [Header("Growth (applied to the monoliths there)")]
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
        int _childCount = -1;
        bool _dirty;
        // Nearest grown monoliths this frame (indices), kept sorted by distance
        readonly int[] _nearest = new int[16];

        /// <summary>The monoliths there (children of "Monoliths", in order).</summary>
        public IReadOnlyList<Monolith> Monoliths => _monoliths;

        void OnEnable()
        {
            Collect();
            _dirty = true;
        }

        void OnValidate() => _dirty = true;

        void Update()
        {
            // Monoliths added, duplicated or deleted by hand
            if (_container == null || _container.childCount != _childCount) Collect();
            if (_dirty)
            {
                _dirty = false;
                for (int i = 0; i < _monoliths.Count; i++) Apply(_monoliths[i], i);
            }
            if (Application.isPlaying) ChooseLiveReflections();
        }

        // The monoliths there, as saved in the scene
        void Collect()
        {
            _monoliths.Clear();
            _container = transform.Find(ContainerName);
            _childCount = _container != null ? _container.childCount : 0;
            if (_container == null) return;
            for (int i = 0; i < _container.childCount; i++)
            {
                var m = _container.GetChild(i).GetComponent<Monolith>();
                if (m != null) _monoliths.Add(m);
            }
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

#if UNITY_EDITOR
        /// <summary>Spreads the monoliths again from the Spread settings (Edit mode), replacing those there, hand moves
        /// included. The same settings and seed give the same places.</summary>
        public void Spread()
        {
            if (Application.isPlaying || monolithPrefab == null) return;
            if (terrain == null) terrain = FindFirstObjectByType<FlythroughTerrain>();
            TerrainShape shape = terrain != null ? terrain.shape : TerrainShape.Default;

            var old = transform.Find(ContainerName);
            if (old != null) UnityEditor.Undo.DestroyObjectImmediate(old.gameObject);
            var container = new GameObject(ContainerName);
            UnityEditor.Undo.RegisterCreatedObjectUndo(container, "Spread Monoliths");
            container.transform.SetParent(transform, false);

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

                var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(monolithPrefab, container.transform);
                go.name = $"Monolith {i + 1}";
                // On the lowest ground under its foot, so it never floats on a slope (the sink hides the rest)
                go.transform.localPosition = new Vector3(best.x, Ground(best, shape), best.y);
                go.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                Apply(go.GetComponent<Monolith>(), i);
            }
            Collect();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif

        // Lowest ground under a monolith's foot (terrain space)
        float Ground(Vector2 p, TerrainShape shape)
        {
            float reach = side * 0.6f;
            float ground = float.MaxValue;
            for (int s = 0; s < 4; s++)
            {
                float angle = s * Mathf.PI * 0.5f;
                Vector2 offset = s == 0 ? Vector2.zero : new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
                var at = new float2(p.x + offset.x, p.y + offset.y);
                ground = Mathf.Min(ground, terrain != null ? terrain.TerrainHeight(at) : TerrainField.Height(at, shape, default));
            }
            return ground;
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

        // The base settings, each varied by up to +-variation, the same for a given index and seed. Only shape and
        // growth: a monolith's place is never touched here
        void Apply(Monolith m, int index)
        {
            if (m == null) return;
            var random = new System.Random(seed * 7919 + index * 104729);
            float V() => 1f + variation * ((float)random.NextDouble() * 2f - 1f);

            float scale = V();
            float s = side * scale, h = height * V(), cr = cornerRadius * scale, er = edgeRadius * scale;
            float gs = growStart * V(), ge = growEnd * V();
            if (m.side == s && m.height == h && m.cornerRadius == cr && m.edgeRadius == er && m.sink == sink &&
                m.growthStyle == growthStyle && m.growStart == gs && m.growEnd == ge && m.editGrowth == editGrowth &&
                m.keepGrown == keepGrown) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.RecordObject(m, "Monolith settings");
#endif
            m.side = s;
            m.height = h;
            m.cornerRadius = cr;
            m.edgeRadius = er;
            m.sink = sink;
            m.growthStyle = growthStyle;
            m.growStart = gs;
            m.growEnd = ge;
            m.editGrowth = editGrowth;
            m.keepGrown = keepGrown;
        }
    }
}

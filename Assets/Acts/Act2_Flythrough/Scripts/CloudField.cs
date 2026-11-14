using System.Collections.Generic;
using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// The cluster manager of the Act 2 cubic clouds: spreads <see cref="clusterCount"/> <see cref="CloudCluster"/>s
    /// over the whole landscape, at about <see cref="altitude"/> above the ground, and gives them all the same base
    /// settings (cubes, motion, look), each one varied at random by up to <see cref="variation"/> around them.
    ///
    /// Lives under the act's World object, at its origin: the clusters are placed in terrain space (the landscape of
    /// <see cref="terrain"/>), so they move with the floating origin. The clusters are generated (deterministic for a
    /// given seed), never saved, and follow every change made here. The look values replace the material's Fluff,
    /// Puff Size, Density and Edge Softness; the rest of the look (colour, scattering, samples, shadow core) stays on
    /// the material.
    /// </summary>
    [ExecuteAlways]
    public class CloudField : MonoBehaviour
    {
        [Header("Spread (clusters regenerated on change)")]
        [Range(0, 200)] public int clusterCount = 40;
        [Tooltip("Along the route (terrain +Z, m): from, to.")]
        public Vector2 routeRange = new Vector2(-2000f, 16000f);
        [Tooltip("Width of the band (m) across the route centre line.")]
        public float width = 14000f;
        [Tooltip("Height of the clusters above the ground below them (m).")]
        public float altitude = 800f;
        [Tooltip("Random variation of every cluster value around the values below (0.2 = +-20%).")]
        [Range(0f, 0.5f)] public float variation = 0.2f;
        public int seed = 1;

        [Header("Cubes")]
        [Range(1, 40)] public int cubeCount = 14;
        [Tooltip("Edge of the smallest and largest cubes (m).")]
        public Vector2 cubeSize = new Vector2(60f, 130f);
        [Range(0f, 0.6f)] public float boxiness = 0.3f;
        [Tooltip("The cubes spawn at random inside this ellipsoid: its radii (m) across, up, along.")]
        public Vector3 ellipse = new Vector3(160f, 40f, 110f);
        public bool alignRotation = true;

        [Header("Motion (Play mode)")]
        [Tooltip("Pull of each cube back to its spawn point (m/s2).")]
        public float attraction = 2f;
        public float damping = 0.25f;
        [Tooltip("Random push of the Brownian wander (m/s2).")]
        public float turbulence = 1.5f;
        public float turbulenceFrequency = 0.05f;
        [Tooltip("Wind (m/s): same direction for all clusters, speed varied.")]
        public Vector3 drift = new Vector3(0f, 0f, 1.5f);

        [Header("Look")]
        [Tooltip("Fluff (m), replaces the material's.")]
        public float fluff = 12f;
        [Tooltip("Puff Size (m), replaces the material's.")]
        public float puffSize = 14f;
        [Tooltip("Density, replaces the material's.")]
        public float density = 0.06f;
        [Tooltip("Edge Softness (m), replaces the material's.")]
        public float softness = 4f;

        [Header("Distance (Play mode)")]
        [Tooltip("Beyond this distance (m) a cloud takes fewer samples per pixel.")]
        public float lodDistance = 2500f;
        [Tooltip("Beyond this distance (m) a cloud and its shadow are not drawn.")]
        public float drawDistance = 14000f;
        [Range(0.1f, 1f)] public float farSamples = 0.35f;

        [Header("References")]
        [Tooltip("The landscape (its shape gives the ground height). Empty = found in the scene.")]
        [OSCQuery.DoNotExpose] public FlythroughTerrain terrain;
        [OSCQuery.DoNotExpose] public Material cloudMaterial;
        [OSCQuery.DoNotExpose] public Shader volumeShader;

        readonly List<CloudCluster> _clusters = new List<CloudCluster>();
        Transform _container;
        int _spreadHash;
        bool _dirty;

        void OnEnable() => Generate();

        void OnDisable() => Clear();

        void OnValidate() => _dirty = true;

        void Update()
        {
            if (!_dirty) return;
            _dirty = false;
            if (SpreadHash() != _spreadHash || _clusters.Count != clusterCount) { Generate(); return; }
            for (int i = 0; i < _clusters.Count; i++)
            {
                CloudCluster cluster = _clusters[i];
                if (cluster == null) continue;
                Apply(cluster, i);
                if (cluster.NeedsRebuild) cluster.Rebuild();
            }
        }

        int SpreadHash()
        {
            unchecked
            {
                int h = clusterCount;
                h = h * 31 + routeRange.GetHashCode();
                h = h * 31 + width.GetHashCode();
                h = h * 31 + altitude.GetHashCode();
                h = h * 31 + variation.GetHashCode();
                h = h * 31 + seed;
                h = h * 31 + (terrain != null ? terrain.GetInstanceID() : 0);
                return h;
            }
        }

        /// <summary>Spreads the clusters again from the settings.</summary>
        [ContextMenu("Regenerate Clusters")]
        public void Generate()
        {
            Clear();
            _spreadHash = SpreadHash();
            if (terrain == null) terrain = FindFirstObjectByType<FlythroughTerrain>();
            TerrainShape shape = terrain != null ? terrain.shape : TerrainShape.Default;

            var container = new GameObject("Clusters (generated)") { hideFlags = HideFlags.DontSave };
            container.transform.SetParent(transform, false);
            _container = container.transform;

            // Best-candidate sampling: each new cluster takes the farthest of a few random places from the others, so
            // they spread evenly without a grid
            var random = new System.Random(seed);
            var placed = new List<Vector2>();
            for (int i = 0; i < clusterCount; i++)
            {
                Vector2 best = Vector2.zero;
                float bestDistance = -1f;
                for (int candidate = 0; candidate < 12; candidate++)
                {
                    float z = Mathf.Lerp(routeRange.x, routeRange.y, (float)random.NextDouble());
                    float x = TerrainField.RouteX(z, shape) + ((float)random.NextDouble() - 0.5f) * width;
                    var p = new Vector2(x, z);
                    float nearest = float.MaxValue;
                    foreach (var other in placed) nearest = Mathf.Min(nearest, (other - p).sqrMagnitude);
                    if (nearest > bestDistance) { bestDistance = nearest; best = p; }
                }
                placed.Add(best);

                var go = new GameObject($"Cloud Cluster {i + 1}") { hideFlags = HideFlags.DontSave };
                go.SetActive(false);
                go.transform.SetParent(_container, false);
                var cluster = go.AddComponent<CloudCluster>();
                Apply(cluster, i);

                // Above the highest ground under the cluster, so it never sinks into a mountain
                float reach = Mathf.Max(ellipse.x, ellipse.z) + cubeSize.y;
                float ground = 0f;
                for (int s = 0; s < 5; s++)
                {
                    Vector2 offset = s == 0 ? Vector2.zero : new Vector2(s == 1 ? reach : s == 2 ? -reach : 0f, s == 3 ? reach : s == 4 ? -reach : 0f);
                    ground = Mathf.Max(ground, TerrainField.Height(best + offset, shape));
                }
                float height = altitude * (1f + variation * ((float)random.NextDouble() * 2f - 1f));
                go.transform.localPosition = new Vector3(best.x, ground + height, best.y);
                go.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                _clusters.Add(cluster);
                go.SetActive(true);
            }
        }

        // The base settings, each varied by up to +-variation, the same for a given cluster index and seed
        void Apply(CloudCluster c, int index)
        {
            var random = new System.Random(seed * 7919 + index * 104729);
            float V() => 1f + variation * ((float)random.NextDouble() * 2f - 1f);

            c.cubeCount = Mathf.Clamp(Mathf.RoundToInt(cubeCount * V()), 1, 40);
            float scale = V();
            c.cubeSize = cubeSize * scale;
            c.boxiness = Mathf.Clamp(boxiness * V(), 0f, 0.6f);
            c.alignRotation = alignRotation;
            c.seed = random.Next(1, 100000);
            c.collisions = false;

            c.ellipse = new Vector3(ellipse.x * V(), ellipse.y * V(), ellipse.z * V()) * scale;
            c.attraction = attraction * V();
            c.damping = damping * V();
            c.turbulence = turbulence * V();
            c.turbulenceFrequency = turbulenceFrequency * V();
            c.drift = drift * V();

            c.overrideLook = true;
            c.fluff = fluff * V();
            c.puffSize = puffSize * V();
            c.density = density * V();
            c.softness = softness * V();

            c.lodDistance = lodDistance;
            c.drawDistance = drawDistance;
            c.farSamples = farSamples;
            c.cloudMaterial = cloudMaterial;
            c.volumeShader = volumeShader;
        }

        void Clear()
        {
            _clusters.Clear();
            if (_container == null) _container = transform.Find("Clusters (generated)");
            if (_container == null) return;
            if (Application.isPlaying) Destroy(_container.gameObject);
            else DestroyImmediate(_container.gameObject);
            _container = null;
        }
    }
}

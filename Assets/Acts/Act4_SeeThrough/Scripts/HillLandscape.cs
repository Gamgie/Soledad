using UnityEngine;
using UnityEngine.Rendering;

namespace Soledad
{
    /// <summary>
    /// Act 4: the landscape around the hill, so the horizon is never a line: a plain of rolling grassy mounds (domes of
    /// random size and height, growing with the distance, joined by a smooth maximum so they overlap like real hills),
    /// rising to rounded mountains in the distance. The hill (a perfect sphere, <see cref="Hill"/>) emerges from it with
    /// a smooth fillet at its foot. Beyond the hill's foot the whole landscape falls away (<see cref="fallSlope"/>), so
    /// that from the top of the hill, around the tree, it stays under the hill's own horizon: only sky behind. Same grass material as the hill, ground only (no shells). The mesh is a polar grid
    /// centred on the hill (dense at its foot, coarse at the horizon), never saved, uv0 = world xz in metres, in two
    /// parts: "Landscape Mesh" up to <see cref="shadowRadius"/> casts shadows (mounds shade each other and the hill, as
    /// in the references), "Landscape Far Mesh" beyond does not (a caster kilometres long gets clamped in the sun's
    /// shadow map and throws false shadows on the hill; it is beyond the shadow distance anyway).
    /// </summary>
    [ExecuteAlways]
    public class HillLandscape : MonoBehaviour
    {
        [Tooltip("The hill in the middle: the landscape joins it with a fillet.")]
        public Hill hill;
        public Material material;

        [Header("Mesh")]
        [Tooltip("Radius of the landscape (m).")]
        public float outerRadius = 6000f;
        [Range(32, 512)] public int rings = 260;
        [Range(64, 2048)] public int segments = 720;
        [Tooltip("The landscape casts shadows up to this distance from the hill (m), not beyond.")]
        public float shadowRadius = 2000f;

        [Header("Plain")]
        [Tooltip("Height of the plain around the hill (m, the hill top is at 0).")]
        public float plainHeight = -16f;
        [Tooltip("Gentle waves of the plain (m).")]
        public float undulation = 3f;
        public float undulationScale = 140f;
        [Tooltip("Width of the smooth foot where the hill meets the plain (m).")]
        public float hillFillet = 14f;

        [Header("Fall Away (hidden from the hilltop)")]
        [Tooltip("The whole landscape slopes down from this distance to the hill's axis (m)...")]
        public float fallStart = 60f;
        [Tooltip("...by this much per metre (0 = flat), so that seen from the top of the hill it stays under the hill's own horizon: only sky behind the tree.")]
        [Range(0f, 1f)] public float fallSlope = 0.3f;
        [Tooltip("Distance over which the slope sets in (m): a rounded shoulder, not a crease.")]
        public float fallSoftness = 120f;

        [Header("Rolling Mounds")]
        public int seed = 3;
        public int moundCount = 170;
        [Tooltip("No mound closer than this to the hill (m).")]
        public float moundMinDistance = 85f;
        public float moundMaxDistance = 2200f;
        [Tooltip("Radius of the mounds near the hill and at the far end (m).")]
        public Vector2 moundSize = new Vector2(35f, 280f);
        [Tooltip("Height / radius of a mound (min, max).")]
        public Vector2 moundSteepness = new Vector2(0.3f, 0.6f);
        [Tooltip("Longest / shortest axis of a mound (max): long ridges or round domes.")]
        [Range(1f, 4f)] public float moundElongation = 2.2f;
        [Tooltip("How softly overlapping mounds merge (fraction of their radius).")]
        [Range(0f, 1f)] public float moundBlend = 0.3f;

        [Header("Mountains")]
        public float mountainStart = 1800f;
        public float mountainFull = 4000f;
        public float mountainHeight = 520f;
        [Tooltip("Size of the mountain shapes (m).")]
        public float mountainScale = 1500f;

        // A dome; (ax, az) is its long axis, scaled by its aspect (long / short)
        struct Mound { public float x, z, radius, height, blend, ax, az, aspect; }

        readonly MeshFilter[] _filters = new MeshFilter[2];
        readonly MeshRenderer[] _renderers = new MeshRenderer[2];
        readonly Mesh[] _meshes = new Mesh[2];
        static readonly string[] PartNames = { "Landscape Mesh", "Landscape Far Mesh" };
        Mound[] _mounds;
        int _builtHash;

        void OnEnable()
        {
            FindParts();
            Build();
        }

        void OnDisable()
        {
            foreach (var filter in _filters)
                if (filter != null) filter.sharedMesh = null;
        }

        void OnDestroy()
        {
            foreach (var mesh in _meshes)
            {
                if (mesh == null) continue;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        }

        void Update()
        {
            if (SettingsHash() != _builtHash) Build();
            foreach (var r in _renderers)
                if (r != null && r.sharedMaterial != material) r.sharedMaterial = material;
        }

        int SettingsHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + outerRadius.GetHashCode(); h = h * 31 + rings; h = h * 31 + segments;
                h = h * 31 + shadowRadius.GetHashCode();
                h = h * 31 + fallStart.GetHashCode(); h = h * 31 + fallSlope.GetHashCode(); h = h * 31 + fallSoftness.GetHashCode();
                h = h * 31 + plainHeight.GetHashCode(); h = h * 31 + undulation.GetHashCode();
                h = h * 31 + undulationScale.GetHashCode(); h = h * 31 + hillFillet.GetHashCode();
                h = h * 31 + seed; h = h * 31 + moundCount; h = h * 31 + moundMinDistance.GetHashCode();
                h = h * 31 + moundMaxDistance.GetHashCode(); h = h * 31 + moundSize.GetHashCode();
                h = h * 31 + moundSteepness.GetHashCode(); h = h * 31 + moundBlend.GetHashCode();
                h = h * 31 + moundElongation.GetHashCode();
                h = h * 31 + mountainStart.GetHashCode(); h = h * 31 + mountainFull.GetHashCode();
                h = h * 31 + mountainHeight.GetHashCode(); h = h * 31 + mountainScale.GetHashCode();
                if (hill != null) { h = h * 31 + hill.radius.GetHashCode(); }
                return h;
            }
        }

        void FindParts()
        {
            for (int k = 0; k < 2; k++)
            {
                var child = transform.Find(PartNames[k]);
                if (child == null)
                {
                    var go = new GameObject(PartNames[k]);
                    go.layer = gameObject.layer;
                    child = go.transform;
                    child.SetParent(transform, false);
                }
                _filters[k] = child.GetComponent<MeshFilter>();
                if (_filters[k] == null) _filters[k] = child.gameObject.AddComponent<MeshFilter>();
                _renderers[k] = child.GetComponent<MeshRenderer>();
                if (_renderers[k] == null) _renderers[k] = child.gameObject.AddComponent<MeshRenderer>();
                _renderers[k].sharedMaterial = material;
                _renderers[k].shadowCastingMode = k == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        // Smooth value noise, 0 to 1 (Mathf.PerlinNoise has steps of several metres here: thin strips where it jumps)
        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            float ux = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            float uy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        static float SmoothMax(float a, float b, float k)
        {
            if (k <= 1e-4f) return Mathf.Max(a, b);
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Max(a, b) + h * h * k * 0.25f;
        }

        float HillRadius => hill != null ? hill.radius : 50f;

        // Height of the hill's sphere at distance d from its axis (top at 0), very low outside it
        float SphereHeight(float d)
        {
            float r = HillRadius;
            return d < r ? Mathf.Sqrt(r * r - d * d) - r : -1e6f;
        }

        void MakeMounds()
        {
            var random = new System.Random(seed);
            _mounds = new Mound[Mathf.Max(moundCount, 0)];
            for (int i = 0; i < _mounds.Length; i++)
            {
                // More mounds near the hill, bigger ones far away
                float t = Mathf.Pow((float)random.NextDouble(), 1.4f);
                float distance = Mathf.Lerp(moundMinDistance, moundMaxDistance, t);
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = Mathf.Lerp(moundSize.x, moundSize.y, t) * Mathf.Lerp(0.7f, 1.3f, (float)random.NextDouble());
                // Keep the mounds clear of the hill
                distance = Mathf.Max(distance, moundMinDistance + radius * 0.5f);
                float steepness = Mathf.Lerp(moundSteepness.x, moundSteepness.y, (float)random.NextDouble());
                float axis = (float)random.NextDouble() * Mathf.PI;
                float aspect = Mathf.Lerp(1f, moundElongation, (float)random.NextDouble());
                _mounds[i] = new Mound
                {
                    x = Mathf.Cos(angle) * distance,
                    z = Mathf.Sin(angle) * distance,
                    radius = radius,
                    height = radius * steepness,
                    blend = radius * moundBlend,
                    ax = Mathf.Cos(axis),
                    az = Mathf.Sin(axis),
                    aspect = aspect,
                };
            }
        }

        /// <summary>Landscape height (m, hill top = 0) at local (x, z), the hill's fillet included.</summary>
        public float Height(float x, float z)
        {
            float d = Mathf.Sqrt(x * x + z * z);
            float plain = plainHeight + undulation * (Noise(x / undulationScale + 31.7f, z / undulationScale + 7.3f) - 0.5f) * 2f;
            float h = plain;
            for (int i = 0; i < _mounds.Length; i++)
            {
                ref Mound m = ref _mounds[i];
                float dx = x - m.x, dz = z - m.z;
                // Elliptic: the radius along the long axis, radius / aspect across it
                float along = dx * m.ax + dz * m.az, across = (dz * m.ax - dx * m.az) * m.aspect;
                float dd2 = (along * along + across * across) / (m.radius * m.radius);
                if (dd2 >= 1.44f) continue;
                float dd = Mathf.Sqrt(dd2);
                // Rounded dome on the plain, reaching the plain smoothly at 1.2 radius (smooth top, wide foot)
                float q = 1f - dd / 1.2f;
                float dome = q * q * q * (q * (q * 6f - 15f) + 10f);
                // Starts a blend width under the plain, so the smooth maximum adds nothing where the dome ends
                // (else a step at its edge)
                h = SmoothMax(h, plain - m.blend + (m.height + m.blend) * dome, m.blend);
            }

            // Mountains: large smooth shapes, faded in with the distance
            float mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mountainStart, mountainFull, d));
            if (mask > 0f)
            {
                float n = Noise(x / mountainScale + 101.3f, z / mountainScale + 53.9f) * 0.65f
                        + Noise(x / (mountainScale * 0.43f) + 17.1f, z / (mountainScale * 0.43f) + 91.4f) * 0.35f;
                h += mask * mountainHeight * Mathf.SmoothStep(0f, 1f, n);
            }

            h -= Fall(d);

            float sphere = SphereHeight(d);
            h = SmoothMax(h, sphere, hillFillet);
            // Where the smooth maximum has become the sphere itself, the landscape would lie exactly on the hill's
            // mesh (two tessellations: z-fighting rings): sink it under the sphere there, so the hill alone shows
            if (sphere > -1e5f)
                h -= 0.3f * (1f - Mathf.Clamp01((h - sphere) / 1f));
            return h;
        }

        // How far the landscape has fallen at distance d from the hill's axis: 0 until fallStart, then a quadratic
        // shoulder over fallSoftness, then fallSlope per metre
        float Fall(float d)
        {
            float x = d - fallStart;
            if (x <= 0f || fallSlope <= 0f) return 0f;
            float w = Mathf.Max(fallSoftness, 1e-3f);
            return x < w ? fallSlope * x * x / (2f * w) : fallSlope * (x - w * 0.5f);
        }

        // First ring: inside the point where the fillet starts all around, so the landscape's inner edge lies exactly on
        // the sphere (just under the hill's own mesh, which shows from there up)
        float InnerRadius()
        {
            float r = HillRadius;
            float target = plainHeight + Mathf.Abs(undulation) + hillFillet;   // the highest the plain gets near the hill
            if (target >= 0f) return 1f;
            return Mathf.Sqrt(Mathf.Max(r * r - (target + r) * (target + r), 1f));
        }

        void Build()
        {
            _builtHash = SettingsHash();
            MakeMounds();

            int ringCount = Mathf.Clamp(rings, 8, 1024);
            int seg = Mathf.Clamp(segments, 16, 4096);
            float inner = InnerRadius();
            float outer = Mathf.Max(outerRadius, inner * 2f);
            int vertexCount = ringCount * seg;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            int v = 0;
            for (int j = 0; j < ringCount; j++)
            {
                float radius = inner * Mathf.Pow(outer / inner, j / (ringCount - 1f));
                float eps = Mathf.Max(0.25f, radius * 0.004f);
                for (int i = 0; i < seg; i++)
                {
                    float phi = 2f * Mathf.PI * i / seg;
                    float x = Mathf.Cos(phi) * radius, z = Mathf.Sin(phi) * radius;
                    float y = Height(x, z);
                    // The inner edge sits a little under the sphere, hidden by the hill's own mesh
                    if (j == 0) y -= 0.05f;
                    vertices[v] = new Vector3(x, y, z);
                    float dx = Height(x + eps, z) - Height(x - eps, z);
                    float dz = Height(x, z + eps) - Height(x, z - eps);
                    normals[v] = new Vector3(-dx, 2f * eps, -dz).normalized;
                    uvs[v] = new Vector2(x, z);
                    v++;
                }
            }

            // Near part up to the first ring beyond the shadow radius, far part from there (sharing that ring)
            int split = ringCount - 1;
            for (int j = 1; j < ringCount; j++)
                if (inner * Mathf.Pow(outer / inner, j / (ringCount - 1f)) >= shadowRadius) { split = j; break; }
            MakePart(0, 0, split, seg, vertices, normals, uvs);
            MakePart(1, split, ringCount - 1, seg, vertices, normals, uvs);
        }

        void MakePart(int part, int firstRing, int lastRing, int seg, Vector3[] vertices, Vector3[] normals, Vector2[] uvs)
        {
            var mesh = _meshes[part];
            if (mesh == null) mesh = _meshes[part] = new Mesh { name = PartNames[part] + " (generated)", hideFlags = HideFlags.DontSave };
            mesh.Clear();
            if (_filters[part] != null) _filters[part].sharedMesh = mesh;
            int ringCount = lastRing - firstRing + 1;
            if (ringCount < 2) return;

            int count = ringCount * seg, offset = firstRing * seg;
            var v = new Vector3[count];
            var n = new Vector3[count];
            var uv = new Vector2[count];
            System.Array.Copy(vertices, offset, v, 0, count);
            System.Array.Copy(normals, offset, n, 0, count);
            System.Array.Copy(uvs, offset, uv, 0, count);

            // Same winding as the hill's mesh: front faces up
            var indices = new int[(ringCount - 1) * seg * 6];
            int t = 0;
            for (int j = 0; j < ringCount - 1; j++)
            {
                int row = j * seg, next = row + seg;
                for (int i = 0; i < seg; i++)
                {
                    int i1 = (i + 1) % seg;
                    int a = row + i, b = row + i1, c = next + i, d = next + i1;
                    indices[t++] = a; indices[t++] = b; indices[t++] = c;
                    indices[t++] = b; indices[t++] = d; indices[t++] = c;
                }
            }

            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = v;
            mesh.normals = n;
            mesh.uv = uv;
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, true);
            mesh.RecalculateTangents();
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;

namespace Soledad
{
    /// <summary>
    /// Act 4: the hill, a perfect sphere of <see cref="radius"/> whose top is at this object's origin (centre at
    /// (0, -radius, 0) locally), drawn from the top down to <see cref="capAngle"/> degrees (the rest is never seen).
    /// The mesh is built on the child "Hill Mesh" and never saved. Its UVs are the hill's surface coordinates in
    /// metres (direction from the top times the distance along the surface), seamless, so the ground material tiles
    /// evenly everywhere; set the repeat on the material. Faces look outward and are culled from inside, so the
    /// ground vanishes when the camera goes below the surface. <see cref="Surface"/> gives the same mapping to the
    /// vegetation.
    /// </summary>
    [ExecuteAlways]
    public class Hill : MonoBehaviour
    {
        [Tooltip("Sphere radius (m). The hill is twice as wide.")]
        public float radius = 50f;
        [Tooltip("How far down the sphere goes, in degrees from the top (90 = hemisphere).")]
        [Range(10f, 180f)] public float capAngle = 100f;
        [Tooltip("Segments around the vertical axis.")]
        [Range(16, 1024)] public int segments = 384;
        [Tooltip("Rings from the top down to the edge of the cap.")]
        [Range(8, 512)] public int rings = 160;
        [Tooltip("Ground material (UVs in metres along the surface).")]
        public Material material;

        MeshFilter _filter;
        MeshRenderer _renderer;
        Mesh _mesh;
        float _builtRadius, _builtCap;
        int _builtSegments, _builtRings;

        /// <summary>The generated mesh (null until built).</summary>
        public Mesh GeneratedMesh => _mesh;

        /// <summary>Local to world matrix of the generated mesh's renderer.</summary>
        public Matrix4x4 MeshToWorld => _filter != null ? _filter.transform.localToWorldMatrix : transform.localToWorldMatrix;

        /// <summary>Sphere centre in world space.</summary>
        public Vector3 CenterWorld => transform.TransformPoint(new Vector3(0f, -radius, 0f));

        /// <summary>Distance along the surface from the top to the edge of the cap (m).</summary>
        public float MaxArc => radius * capAngle * Mathf.Deg2Rad;

        /// <summary>
        /// Local position and normal of the surface point at surface coordinates (x, z): the direction (x, z) from
        /// the top, at the distance |(x, z)| along the surface.
        /// </summary>
        public void Surface(float x, float z, out Vector3 position, out Vector3 normal)
        {
            float s = Mathf.Sqrt(x * x + z * z);
            float theta = s / Mathf.Max(radius, 1e-3f);
            float sinT = Mathf.Sin(theta), cosT = Mathf.Cos(theta);
            float dx = s > 1e-6f ? x / s : 0f, dz = s > 1e-6f ? z / s : 0f;
            normal = new Vector3(sinT * dx, cosT, sinT * dz);
            position = new Vector3(0f, -radius, 0f) + normal * radius;
        }

        /// <summary>Sphere area per unit of surface-coordinate area at distance s from the top (1 at the top).</summary>
        public float AreaRatio(float s)
        {
            float theta = s / Mathf.Max(radius, 1e-3f);
            return theta < 1e-4f ? 1f : Mathf.Sin(theta) / theta;
        }

        void OnEnable()
        {
            FindParts();
            Build();
        }

        void OnDisable()
        {
            if (_filter != null) _filter.sharedMesh = null;
        }

        void OnValidate()
        {
            if (_renderer != null && _renderer.sharedMaterial != material) _renderer.sharedMaterial = material;
        }

        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }

        void Update()
        {
            if (radius != _builtRadius || capAngle != _builtCap || segments != _builtSegments || rings != _builtRings)
                Build();
            if (_renderer != null && _renderer.sharedMaterial != material)
                _renderer.sharedMaterial = material;
        }

        void FindParts()
        {
            var child = transform.Find("Hill Mesh");
            if (child == null)
            {
                var go = new GameObject("Hill Mesh");
                go.layer = gameObject.layer;
                child = go.transform;
                child.SetParent(transform, false);
            }
            _filter = child.GetComponent<MeshFilter>();
            if (_filter == null) _filter = child.gameObject.AddComponent<MeshFilter>();
            _renderer = child.GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = child.gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            // Casts on the landscape around it (and receives the mounds' shadows)
            _renderer.shadowCastingMode = ShadowCastingMode.On;
        }

        void Build()
        {
            _builtRadius = radius;
            _builtCap = capAngle;
            _builtSegments = segments;
            _builtRings = rings;

            int seg = Mathf.Clamp(segments, 16, 1024);
            int ringCount = Mathf.Clamp(rings, 8, 512);
            float r = Mathf.Max(radius, 1e-3f);
            float cap = Mathf.Clamp(capAngle, 1f, 180f) * Mathf.Deg2Rad;

            // One vertex at the top, then seg vertices per ring (the UVs are surface coordinates, so no seam)
            int vertexCount = 1 + ringCount * seg;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var indices = new int[(seg + (ringCount - 1) * seg * 2) * 3];

            vertices[0] = Vector3.zero;
            normals[0] = Vector3.up;
            uvs[0] = Vector2.zero;
            int v = 1;
            for (int j = 1; j <= ringCount; j++)
            {
                float theta = cap * j / ringCount;
                float s = theta * r;
                for (int i = 0; i < seg; i++)
                {
                    float phi = 2f * Mathf.PI * i / seg;
                    float x = Mathf.Cos(phi) * s, z = Mathf.Sin(phi) * s;
                    Surface(x, z, out vertices[v], out normals[v]);
                    uvs[v] = new Vector2(x, z);
                    v++;
                }
            }

            // Clockwise seen from outside (Unity front faces)
            int t = 0;
            for (int i = 0; i < seg; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % seg;
                indices[t++] = 0; indices[t++] = b; indices[t++] = a;
            }
            for (int j = 0; j < ringCount - 1; j++)
            {
                int row = 1 + j * seg, next = row + seg;
                for (int i = 0; i < seg; i++)
                {
                    int i1 = (i + 1) % seg;
                    int a = row + i, b = row + i1, c = next + i, d = next + i1;
                    indices[t++] = a; indices[t++] = b; indices[t++] = c;
                    indices[t++] = b; indices[t++] = d; indices[t++] = c;
                }
            }

            if (_mesh == null) _mesh = new Mesh { name = "Hill (generated)", hideFlags = HideFlags.DontSave };
            _mesh.Clear();
            _mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.vertices = vertices;
            _mesh.normals = normals;
            _mesh.uv = uvs;
            _mesh.SetIndices(indices, MeshTopology.Triangles, 0, true);
            _mesh.RecalculateTangents();
            if (_filter != null) _filter.sharedMesh = _mesh;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 0.4f, 0.5f);
            Gizmos.DrawWireSphere(CenterWorld, radius * transform.lossyScale.y);
        }
    }
}

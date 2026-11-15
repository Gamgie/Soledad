using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Soledad
{
    /// <summary>
    /// A soft mirror monolith (Act 2): a standing triangular prism (like an aluminium triangle bar) with rounded edges, in a blurred
    /// metal finish, growing slowly as the bird comes nearer along the route. Its pivot is at the foot; the mesh of the child "Slab" is
    /// generated from the shape settings (never saved) and grows from the foot (<see cref="growthStyle"/>).
    ///
    /// Its reflection comes from its own realtime reflection probe (child "Reflection"), rendered at low resolution
    /// (the finish blurs it anyway), one cube face per frame while the camera is near, frozen when far. The probe sits
    /// inside the prism, where every face of the slab is a back face (culled), so it sees the landscape, the sky and
    /// the other monoliths around it. The slab stays on the Default layer: the dome cameras only render a few layers.
    /// </summary>
    [ExecuteAlways]
    public class Monolith : MonoBehaviour
    {
        public enum GrowthStyle { Scale, Rise }

        [Header("Shape (standing triangular prism)")]
        [Tooltip("Side (m) of the triangle (the cross-section, equilateral) when fully grown.")]
        public float side = 30f;
        [Tooltip("Height (m) when fully grown.")]
        public float height = 120f;
        [Tooltip("Radius (m) of the three long vertical edges.")]
        public float cornerRadius = 2.5f;
        [Tooltip("Radius (m) of the edges around the top and bottom faces (at most the corner radius).")]
        public float edgeRadius = 1.5f;
        [Tooltip("Smoothness of the rounding: segments per corner and per edge.")]
        [Range(2, 24)] public int roundSegments = 10;
        [Tooltip("Depth (m) the foot goes into the ground, so it stands on slopes.")]
        public float sink = 4f;

        [Header("Growth (Play mode)")]
        [Tooltip("Scale: grows from its foot, the whole shape. Rise: comes up out of the ground at full size.")]
        public GrowthStyle growthStyle = GrowthStyle.Scale;
        [Tooltip("Depth (m) ahead of the camera along the route (Z) where it starts to grow, wherever it is across.")]
        public float growStart = 3500f;
        [Tooltip("Depth (m) ahead of the camera along the route (Z) where it is fully grown (0 = when the camera passes it).")]
        public float growEnd = 600f;
        [Tooltip("Once grown it stays grown, even if the camera goes back. Off: it follows the depth both ways.")]
        public bool keepGrown = true;
        [Tooltip("Growth shown in Edit mode (0 = nothing, 1 = full size).")]
        [Range(0f, 1f)] public float editGrowth = 1f;
        [Tooltip("Current growth (read only).")]
        [Range(0f, 1f)] public float growth = 1f;

        [Header("Reflection")]
        [Tooltip("Distance (m) within which the reflection is refreshed (one cube face per frame); farther it is frozen.")]
        public float reflectionDistance = 4000f;

        public const int ReflectionByDistance = -1, ReflectionFrozen = 0, ReflectionLive = 1, ReflectionOff = 2;

        /// <summary>Set by <see cref="MonolithField"/>, which gives probes only to the nearest monoliths:
        /// ReflectionByDistance = this monolith decides by its distance, Frozen = keeps its last reflection,
        /// Live = refreshed, Off = no probe (reflects the sky; HDRP draws at most 32 probes on screen).</summary>
        [System.NonSerialized] public int liveReflection = ReflectionByDistance;

        /// <summary>Horizontal distance (m) to the camera, measured in the last update (Play mode).</summary>
        public float Distance { get; private set; } = float.MaxValue;

        Transform _slab;
        HDAdditionalReflectionData _probe;
        Mesh _mesh;
        int _builtHash;
        float _reached;      // highest growth reached since enabled (Play mode)

        void OnEnable()
        {
            _slab = transform.Find("Slab");
            var reflection = transform.Find("Reflection");
            _probe = reflection != null ? reflection.GetComponent<HDAdditionalReflectionData>() : null;
            _builtHash = 0;
            _reached = 0f;
            Apply();
            if (_probe != null) _probe.RequestRenderNextUpdate();
        }

        void OnDisable()
        {
            if (_mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); }
            _mesh = null;
        }

        // The mesh is rebuilt from Update (meshes cannot be assigned from OnValidate)
        void OnValidate()
        {
            if (_probe != null) _probe.RequestRenderNextUpdate();
        }

        void Update()
        {
            if (Application.isPlaying)
            {
                Transform viewer = MainCamera.Find();
                if (viewer != null)
                {
                    Vector3 d = viewer.position - transform.position;
                    float distance = new Vector2(d.x, d.z).magnitude;
                    Distance = distance;
                    // Growth by depth: how far ahead along the route (Z) it stands, not by distance, so a monolith far
                    // to the side grows at the same time as one on the flight line
                    float depth = transform.position.z - viewer.position.z;
                    float t = Mathf.InverseLerp(growStart, growEnd, depth);
                    float target = t * t * (3f - 2f * t);
                    // Once risen it stays up: the camera flying away does not send it back underground
                    growth = keepGrown ? Mathf.Max(_reached, target) : target;
                    _reached = growth;
                    // Near: refreshed continuously, one cube face per frame (time slicing: an even cost, no
                    // spikes). Far: frozen
                    if (_probe != null)
                    {
                        bool on = liveReflection != ReflectionOff;
                        if (_probe.enabled != on)
                        {
                            _probe.enabled = on;
                            if (on) _probe.RequestRenderNextUpdate();    // a fresh reflection when it gets one back
                        }
                        bool live = on && growth > 0.01f
                                    && (liveReflection == ReflectionByDistance ? distance < reflectionDistance : liveReflection == ReflectionLive);
                        var mode = live ? ProbeSettings.RealtimeMode.EveryFrame : ProbeSettings.RealtimeMode.OnDemand;
                        if (_probe.realtimeMode != mode) _probe.realtimeMode = mode;
                        if (!_probe.timeSlicing) _probe.timeSlicing = true;
                    }
                }
            }
            else growth = editGrowth;
            Apply();
        }

        void Apply()
        {
            if (_slab == null) return;
            int hash = ShapeHash();
            if (hash != _builtHash || _mesh == null) BuildMesh(hash);

            float g = Mathf.Clamp01(growth);
            if (growthStyle == GrowthStyle.Scale)
            {
                _slab.localScale = Vector3.one * Mathf.Max(g, 0.001f);
                _slab.localPosition = new Vector3(0f, -sink, 0f);
            }
            else
            {
                _slab.localScale = Vector3.one;
                _slab.localPosition = new Vector3(0f, -sink - (1f - g) * height, 0f);
            }
            _slab.localRotation = Quaternion.identity;
            _slab.gameObject.SetActive(g > 0.001f);

            if (_probe != null)
            {
                // The reflection covers the slab and a margin around it, captured from its middle
                float h = growthStyle == GrowthStyle.Scale ? height * g : height;
                float w = growthStyle == GrowthStyle.Scale ? side * g : side;
                float top = growthStyle == GrowthStyle.Scale ? h - sink : g * height - sink;
                _probe.transform.localPosition = new Vector3(0f, Mathf.Max(top * 0.5f, 1f), 0f);
                Vector3 box = new Vector3(w, Mathf.Max(h, 1f), w * 0.866f) + Vector3.one * 4f;
                if (_probe.influenceVolume.boxSize != box) _probe.influenceVolume.boxSize = box;
            }
        }

        int ShapeHash()
        {
            unchecked
            {
                int h = side.GetHashCode() * 7 + height.GetHashCode();
                h = h * 31 + cornerRadius.GetHashCode();
                h = h * 31 + edgeRadius.GetHashCode();
                h = h * 31 + roundSegments;
                return h == 0 ? 1 : h;
            }
        }

        // ---------------------------------------------------------------- rounded triangular prism

        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<int> _triangles = new List<int>();

        /// <summary>
        /// The prism is built lying along z, then stood up: its cross-section (an equilateral triangle with rounded
        /// corners, in xy) is swept along the height with a quarter round at both ends, so the long edges are round
        /// (corner radius) and so are the edges of the top and bottom faces (edge radius). Built as the "core"
        /// outline (the triangle inset by the edge radius) with a ring of the edge profile around it, plus the two
        /// flat end faces filling the core.
        /// </summary>
        void BuildMesh(int hash)
        {
            _builtHash = hash;
            float width = Mathf.Max(side, 0.1f), depth = width * 0.8660254f, thickness = Mathf.Max(height, 0.1f);
            int seg = Mathf.Max(2, roundSegments);

            // Equilateral triangle centred on its centroid, and its incircle
            Vector2 a = new Vector2(-width * 0.5f, -depth / 3f), b = new Vector2(width * 0.5f, -depth / 3f), c = new Vector2(0f, depth * 2f / 3f);
            float la = (b - c).magnitude, lb = (c - a).magnitude, lc = (a - b).magnitude;
            Vector2 incentre = (a * la + b * lb + c * lc) / (la + lb + lc);
            float s = (la + lb + lc) * 0.5f;
            float inradius = Mathf.Sqrt(Mathf.Max(s * (s - la) * (s - lb) * (s - lc), 1e-6f)) / s;

            // Corners rounded with cornerRadius on the silhouette: the core triangle is inset by cornerRadius, its
            // corners are arcs of radius (cornerRadius - edge radius), the edge ring adds the edge radius back
            float corner = Mathf.Clamp(cornerRadius, 0.01f, inradius * 0.95f);
            float re = Mathf.Clamp(edgeRadius, 0f, Mathf.Min(corner, thickness * 0.5f));
            float scale = (inradius - corner) / inradius;
            Vector2[] inner = { incentre + (a - incentre) * scale, incentre + (b - incentre) * scale, incentre + (c - incentre) * scale };
            float arc = corner - re;

            // Core outline: arcs around the three inner corners, with outward normals (counter-clockwise)
            var outline = new List<Vector2>();
            var outlineNormals = new List<Vector2>();
            for (int i = 0; i < 3; i++)
            {
                Vector2 prev = inner[(i + 2) % 3], here = inner[i], next = inner[(i + 1) % 3];
                Vector2 n0 = EdgeNormal(prev, here), n1 = EdgeNormal(here, next);
                float a0 = Mathf.Atan2(n0.y, n0.x), a1 = Mathf.Atan2(n1.y, n1.x);
                while (a1 < a0) a1 += Mathf.PI * 2f;
                for (int k = 0; k <= seg; k++)
                {
                    float angle = Mathf.Lerp(a0, a1, k / (float)seg);
                    var n = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    outline.Add(here + n * arc);
                    outlineNormals.Add(n);
                }
            }

            _vertices.Clear(); _normals.Clear(); _triangles.Clear();
            int count = outline.Count;
            float halfCore = thickness * 0.5f - re;

            // Edge ring, from the back face to the front face: a quarter round at the back, the flat side (when the
            // slab is thicker than two edge radii), a quarter round at the front
            int ringStart = _vertices.Count;
            int rows = 2 * (seg + 1);
            for (int k = 0; k < rows; k++)
            {
                bool front = k > seg;
                float phi = front ? Mathf.Lerp(0f, Mathf.PI * 0.5f, (k - seg - 1) / (float)seg)
                                  : Mathf.Lerp(-Mathf.PI * 0.5f, 0f, k / (float)seg);
                float cos = Mathf.Cos(phi), sin = Mathf.Sin(phi);
                float z = (front ? halfCore : -halfCore) + re * sin;
                for (int j = 0; j < count; j++)
                {
                    Vector2 p = outline[j] + outlineNormals[j] * (re * cos);
                    _vertices.Add(new Vector3(p.x, p.y, z));
                    _normals.Add(new Vector3(outlineNormals[j].x * cos, outlineNormals[j].y * cos, sin));
                }
            }
            for (int k = 0; k < rows - 1; k++)
                for (int j = 0; j < count; j++)
                {
                    int j1 = (j + 1) % count;
                    int v00 = ringStart + k * count + j, v01 = ringStart + k * count + j1;
                    int v10 = ringStart + (k + 1) * count + j, v11 = ringStart + (k + 1) * count + j1;
                    AddTriangle(v00, v10, v11);
                    AddTriangle(v00, v11, v01);
                }

            // Flat faces: fans over the (convex) core outline
            Vector2 centre = (inner[0] + inner[1] + inner[2]) / 3f;
            for (int face = -1; face <= 1; face += 2)
            {
                float z = face * thickness * 0.5f;
                int middle = _vertices.Count;
                _vertices.Add(new Vector3(centre.x, centre.y, z));
                _normals.Add(new Vector3(0f, 0f, face));
                int first = _vertices.Count;
                for (int j = 0; j < count; j++)
                {
                    _vertices.Add(new Vector3(outline[j].x, outline[j].y, z));
                    _normals.Add(new Vector3(0f, 0f, face));
                }
                for (int j = 0; j < count; j++) AddTriangle(middle, first + j, first + (j + 1) % count);
            }

            // Rounding the corners shrinks the cross-section: stretch it back to the exact side, then stand the
            // prism up (its length along y, foot at 0). Swapping y and z mirrors it, so the triangles are flipped
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var v in _vertices)
            {
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            float sx = width / Mathf.Max(maxX - minX, 1e-3f), sy = depth / Mathf.Max(maxY - minY, 1e-3f);
            for (int i = 0; i < _vertices.Count; i++)
            {
                Vector3 v = _vertices[i];
                _vertices[i] = new Vector3(v.x * sx, v.z + thickness * 0.5f, (v.y - minY) * sy - depth / 3f);
                Vector3 n = _normals[i];
                _normals[i] = new Vector3(n.x / sx, n.z, n.y / sy).normalized;
            }
            for (int i = 0; i < _triangles.Count; i += 3)
            {
                int t = _triangles[i + 1];
                _triangles[i + 1] = _triangles[i + 2];
                _triangles[i + 2] = t;
            }

            if (_mesh == null) _mesh = new Mesh { name = "Monolith (generated)", hideFlags = HideFlags.DontSave };
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetNormals(_normals);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateTangents();
            _mesh.RecalculateBounds();
            var filter = _slab.GetComponent<MeshFilter>();
            if (filter != null) filter.sharedMesh = _mesh;
        }

        // Outward normal of the edge from p to q of a counter-clockwise outline
        static Vector2 EdgeNormal(Vector2 p, Vector2 q)
        {
            Vector2 d = (q - p).normalized;
            return new Vector2(d.y, -d.x);
        }

        // Adds the triangle facing the way its vertex normals point
        void AddTriangle(int i0, int i1, int i2)
        {
            Vector3 n = _normals[i0] + _normals[i1] + _normals[i2];
            Vector3 face = Vector3.Cross(_vertices[i1] - _vertices[i0], _vertices[i2] - _vertices[i0]);
            // Unity's front faces: Cross(b - a, c - a) points out of them
            if (Vector3.Dot(face, n) < 0f) { int t = i1; i1 = i2; i2 = t; }
            _triangles.Add(i0); _triangles.Add(i1); _triangles.Add(i2);
        }
    }

    /// <summary>The camera named "Main Camera" (Oxipital Cameras prefab), searched at most once a second.</summary>
    public static class MainCamera
    {
        static Transform s_found;
        static float s_nextSearch;

        public static Transform Find()
        {
            if (s_found != null) return s_found;
            if (Time.realtimeSinceStartup < s_nextSearch) return null;
            s_nextSearch = Time.realtimeSinceStartup + 1f;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (cam.name == "Main Camera") { s_found = cam.transform; break; }
            return s_found;
        }
    }
}

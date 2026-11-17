using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soledad
{
    /// <summary>
    /// Act 4: scatters vegetation layers (grass, flowers...) over the <see cref="Hill"/> and draws them with GPU
    /// instancing (Graphics.RenderMeshInstanced), no GameObjects. Each layer takes a prefab (LOD 0 and LOD 1 of its
    /// LODGroup, or its MeshFilter) or a mesh and its materials. Instances are placed once, deterministic for a seed,
    /// standing along the sphere normal (<c>alignToGround</c>) with random yaw, lean and size, in patches
    /// (<c>patchiness</c>), kept out of <see cref="clearRadius"/> around the top (the tree). They are grouped in
    /// cells of <see cref="cellSize"/>; for each camera, cells behind the horizon of the hill or beyond a layer's
    /// <c>maxDistance</c> are skipped, and the density falls to <c>farDensity</c> with the distance (instances are
    /// in random order in a cell, so the first ones are drawn). Nothing is drawn while the camera is below the
    /// surface: from under the hill only the roots are seen. Every camera is handled in its own begin-rendering
    /// callback (the dome cameras too, and the Scene view in edit mode); cameras at the same position share the
    /// culling. Nothing is allocated per frame. The instances are never saved.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Hill))]
    public class HillVegetation : MonoBehaviour
    {
        [Serializable]
        public class Layer
        {
            public string name = "Grass";
            public bool enabled = true;
            [Tooltip("Prefab to scatter: its LODGroup (LOD 0, and LOD 1 beyond lodDistance) or its MeshFilter give the mesh and materials.")]
            public GameObject prefab;
            [Tooltip("Used when there is no prefab.")]
            public Mesh mesh;
            [Tooltip("Used when there is no prefab, one per sub-mesh.")]
            public Material[] materials;

            [Header("Placement")]
            [Tooltip("Instances per square metre at full density.")]
            public float density = 4f;
            [Tooltip("Random size, min and max.")]
            public Vector2 scale = new Vector2(0.8f, 1.2f);
            [Tooltip("0 = stands vertical, 1 = stands along the hill's normal.")]
            [Range(0f, 1f)] public float alignToGround = 1f;
            [Tooltip("Random lean (degrees).")]
            [Range(0f, 45f)] public float lean = 6f;
            [Tooltip("0 = even, 1 = only in patches.")]
            [Range(0f, 1f)] public float patchiness = 0.4f;
            [Tooltip("Size of the patches (m).")]
            public float patchSize = 12f;
            public int seed = 0;

            [Header("Distance")]
            [Tooltip("Full density up to this distance from the camera (m).")]
            public float fullDensityDistance = 15f;
            [Tooltip("Nothing beyond this distance (m).")]
            public float maxDistance = 70f;
            [Tooltip("Share of the instances drawn at maxDistance.")]
            [Range(0f, 1f)] public float farDensity = 0.15f;
            [Tooltip("Beyond this distance LOD 1 of the prefab is drawn, when it has one (m).")]
            public float lodDistance = 25f;
            public ShadowCastingMode shadows = ShadowCastingMode.Off;

            // Built at run time
            [NonSerialized] internal Matrix4x4[] local, world0, world1, draw0, draw1;
            [NonSerialized] internal int[] cellStart, cellCount;
            [NonSerialized] internal int count0, count1;
            [NonSerialized] internal Mesh mesh0, mesh1;
            [NonSerialized] internal RenderParams[] params0, params1;
            [NonSerialized] internal Matrix4x4 offset0, offset1;
        }

        [Tooltip("Size of the culling cells (m).")]
        public float cellSize = 4f;
        [Tooltip("No vegetation within this distance of the top (m): the tree's ground.")]
        public float clearRadius = 1.9f;
        [Tooltip("Distance over which the vegetation comes in beyond clearRadius (m).")]
        public float clearFade = 1.5f;
        [Tooltip("Hidden when the camera is this far below the surface (m).")]
        public float undergroundMargin = 0.05f;
        public bool drawInEditMode = true;
        public int seed = 1;
        public Layer[] layers = { new Layer() };

        Hill _hill;
        bool _dirty = true;
        Vector3[] _cellLocal, _cellNormalLocal, _cellWorld, _cellNormalWorld;
        int _cellCount;
        float _cellRadius;
        Matrix4x4 _builtMatrix;
        float _builtRadius, _builtCap;
        Vector3 _culledEye = new Vector3(float.NaN, 0f, 0f);
        Bounds _worldBounds;

        public int InstanceCount
        {
            get
            {
                int n = 0;
                if (layers != null)
                    foreach (var layer in layers)
                        if (layer != null && layer.local != null) n += layer.local.Length;
                return n;
            }
        }

        void OnEnable()
        {
            _hill = GetComponent<Hill>();
            _dirty = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        }

        void OnValidate() => _dirty = true;

        /// <summary>Places the instances again (after changing the hill or a layer from a script).</summary>
        public void Rebuild() => _dirty = true;

        void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            if (!Application.isPlaying && !drawInEditMode) return;
            if (_hill == null) return;

            if (_dirty || _hill.radius != _builtRadius || _hill.capAngle != _builtCap) Build();
            if (transform.localToWorldMatrix != _builtMatrix) UpdateWorld();

            Vector3 eye = cam.transform.position;
            if (eye != _culledEye) Cull(eye);

            if (layers == null) return;
            foreach (var layer in layers)
            {
                if (layer == null || !layer.enabled || layer.local == null) continue;
                Draw(layer.mesh0, layer.params0, layer.draw0, layer.count0, cam);
                Draw(layer.mesh1, layer.params1, layer.draw1, layer.count1, cam);
            }
        }

        static void Draw(Mesh mesh, RenderParams[] renderParams, Matrix4x4[] instances, int count, Camera cam)
        {
            if (mesh == null || renderParams == null || count <= 0) return;
            for (int i = 0; i < renderParams.Length; i++)
            {
                if (renderParams[i].material == null) continue;
                renderParams[i].camera = cam;
                Graphics.RenderMeshInstanced(renderParams[i], mesh, i, instances, count);
            }
        }

        void Cull(Vector3 eye)
        {
            _culledEye = eye;
            float r = _hill.radius * transform.lossyScale.y;
            bool below = (eye - _hill.CenterWorld).sqrMagnitude < (r - undergroundMargin) * (r - undergroundMargin);

            foreach (var layer in layers)
            {
                if (layer == null || layer.local == null) continue;
                layer.count0 = 0;
                layer.count1 = 0;
                if (below || !layer.enabled) continue;

                float maxD = layer.maxDistance + _cellRadius;
                float fullD = Mathf.Min(layer.fullDensityDistance, layer.maxDistance - 1e-3f);
                bool hasLod1 = layer.mesh1 != null;
                for (int c = 0; c < _cellCount; c++)
                {
                    int n = layer.cellCount[c];
                    if (n == 0) continue;
                    Vector3 toEye = eye - _cellWorld[c];
                    float d = toEye.magnitude;
                    if (d > maxD) continue;
                    // Behind the horizon of the hill
                    if (Vector3.Dot(_cellNormalWorld[c], toEye) < -_cellRadius) continue;

                    float f = Mathf.Lerp(1f, layer.farDensity, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fullD, layer.maxDistance, d - _cellRadius)));
                    int take = Mathf.Min(n, Mathf.CeilToInt(n * f));
                    if (hasLod1 && d - _cellRadius > layer.lodDistance)
                    {
                        Array.Copy(layer.world1, layer.cellStart[c], layer.draw1, layer.count1, take);
                        layer.count1 += take;
                    }
                    else
                    {
                        Array.Copy(layer.world0, layer.cellStart[c], layer.draw0, layer.count0, take);
                        layer.count0 += take;
                    }
                }
            }
        }

        void UpdateWorld()
        {
            Matrix4x4 m = transform.localToWorldMatrix;
            _builtMatrix = m;
            for (int c = 0; c < _cellCount; c++)
            {
                _cellWorld[c] = m.MultiplyPoint3x4(_cellLocal[c]);
                _cellNormalWorld[c] = m.MultiplyVector(_cellNormalLocal[c]).normalized;
            }
            foreach (var layer in layers)
            {
                if (layer == null || layer.local == null) continue;
                for (int i = 0; i < layer.local.Length; i++)
                {
                    Matrix4x4 w = m * layer.local[i];
                    layer.world0[i] = w * layer.offset0;
                    if (layer.world1 != null) layer.world1[i] = w * layer.offset1;
                }
            }
            float r = _hill.radius * transform.lossyScale.y;
            _worldBounds = new Bounds(_hill.CenterWorld, Vector3.one * (2f * r + 20f));
            SetBounds();
            _culledEye = new Vector3(float.NaN, 0f, 0f);
        }

        void SetBounds()
        {
            foreach (var layer in layers)
            {
                if (layer == null) continue;
                SetBounds(layer.params0);
                SetBounds(layer.params1);
            }
        }

        void SetBounds(RenderParams[] renderParams)
        {
            if (renderParams == null) return;
            for (int i = 0; i < renderParams.Length; i++) renderParams[i].worldBounds = _worldBounds;
        }

        void Build()
        {
            _dirty = false;
            _builtRadius = _hill.radius;
            _builtCap = _hill.capAngle;
            float size = Mathf.Max(cellSize, 0.5f);
            float maxArc = _hill.MaxArc;
            _cellRadius = size * 0.7072f;

            // Cells on the hill's surface coordinates (see Hill.Surface)
            int half = Mathf.CeilToInt(maxArc / size);
            var cells = new List<Vector2>();
            for (int gz = -half; gz < half; gz++)
                for (int gx = -half; gx < half; gx++)
                {
                    var c = new Vector2((gx + 0.5f) * size, (gz + 0.5f) * size);
                    if (c.magnitude - _cellRadius <= maxArc) cells.Add(c);
                }
            _cellCount = cells.Count;
            _cellLocal = new Vector3[_cellCount];
            _cellNormalLocal = new Vector3[_cellCount];
            _cellWorld = new Vector3[_cellCount];
            _cellNormalWorld = new Vector3[_cellCount];
            for (int c = 0; c < _cellCount; c++)
                _hill.Surface(cells[c].x, cells[c].y, out _cellLocal[c], out _cellNormalLocal[c]);

            var instances = new List<Matrix4x4>();
            for (int l = 0; l < layers.Length; l++)
            {
                var layer = layers[l];
                if (layer == null) continue;
                layer.local = null;
                if (!ResolveMeshes(layer)) continue;

                var rng = new System.Random(unchecked(seed * 7919 + layer.seed * 104729 + l * 31));
                float patchX = (float)rng.NextDouble() * 1000f, patchZ = (float)rng.NextDouble() * 1000f;
                float patch = Mathf.Max(layer.patchSize, 0.1f);
                instances.Clear();
                layer.cellStart = new int[_cellCount];
                layer.cellCount = new int[_cellCount];

                for (int c = 0; c < _cellCount; c++)
                {
                    layer.cellStart[c] = instances.Count;
                    float expected = Mathf.Max(layer.density, 0f) * size * size;
                    int tries = (int)expected + (rng.NextDouble() < expected - (int)expected ? 1 : 0);
                    for (int k = 0; k < tries; k++)
                    {
                        float x = cells[c].x + ((float)rng.NextDouble() - 0.5f) * size;
                        float z = cells[c].y + ((float)rng.NextDouble() - 0.5f) * size;
                        float keepRoll = (float)rng.NextDouble();
                        float yaw = (float)rng.NextDouble() * 360f;
                        float leanAngle = (float)rng.NextDouble() * layer.lean;
                        float leanDir = (float)rng.NextDouble() * 360f;
                        float scale = Mathf.Lerp(layer.scale.x, layer.scale.y, (float)rng.NextDouble());

                        float s = Mathf.Sqrt(x * x + z * z);
                        if (s > maxArc) continue;
                        float keep = _hill.AreaRatio(s);
                        keep *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(clearRadius, clearRadius + clearFade, s));
                        float noise = Mathf.PerlinNoise(x / patch + patchX, z / patch + patchZ);
                        keep *= Mathf.Lerp(1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, noise)), layer.patchiness);
                        if (keepRoll >= keep) continue;

                        _hill.Surface(x, z, out Vector3 position, out Vector3 normal);
                        Vector3 up = Vector3.Slerp(Vector3.up, normal, layer.alignToGround);
                        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up)
                            * Quaternion.AngleAxis(leanAngle, Quaternion.Euler(0f, leanDir, 0f) * Vector3.right)
                            * Quaternion.Euler(0f, yaw, 0f);
                        instances.Add(Matrix4x4.TRS(position, rotation, Vector3.one * scale));
                    }
                    layer.cellCount[c] = instances.Count - layer.cellStart[c];
                }

                layer.local = instances.ToArray();
                int n = layer.local.Length;
                layer.world0 = new Matrix4x4[n];
                layer.draw0 = new Matrix4x4[n];
                layer.world1 = layer.mesh1 != null ? new Matrix4x4[n] : null;
                layer.draw1 = layer.mesh1 != null ? new Matrix4x4[n] : null;
                layer.count0 = layer.count1 = 0;
            }
            UpdateWorld();
        }

        // Mesh, materials and placement offset of LOD 0 and 1 from the prefab, or the layer's own mesh
        bool ResolveMeshes(Layer layer)
        {
            layer.mesh0 = layer.mesh1 = null;
            layer.params0 = layer.params1 = null;
            layer.offset0 = layer.offset1 = Matrix4x4.identity;

            if (layer.prefab != null)
            {
                Renderer r0 = null, r1 = null;
                var lodGroup = layer.prefab.GetComponentInChildren<LODGroup>();
                if (lodGroup != null)
                {
                    var lods = lodGroup.GetLODs();
                    if (lods.Length > 0) r0 = FirstMeshRenderer(lods[0].renderers);
                    if (lods.Length > 1) r1 = FirstMeshRenderer(lods[1].renderers);
                }
                if (r0 == null) r0 = layer.prefab.GetComponentInChildren<MeshRenderer>();
                if (r0 == null) return false;

                Matrix4x4 root = layer.prefab.transform.worldToLocalMatrix;
                layer.mesh0 = r0.GetComponent<MeshFilter>().sharedMesh;
                layer.params0 = MakeParams(r0.sharedMaterials, layer.shadows);
                layer.offset0 = root * r0.transform.localToWorldMatrix;
                if (r1 != null)
                {
                    layer.mesh1 = r1.GetComponent<MeshFilter>().sharedMesh;
                    layer.params1 = MakeParams(r1.sharedMaterials, layer.shadows);
                    layer.offset1 = root * r1.transform.localToWorldMatrix;
                }
            }
            else
            {
                layer.mesh0 = layer.mesh;
                layer.params0 = MakeParams(layer.materials, layer.shadows);
            }
            return layer.mesh0 != null && layer.params0 != null;
        }

        static Renderer FirstMeshRenderer(Renderer[] renderers)
        {
            if (renderers == null) return null;
            foreach (var r in renderers)
                if (r is MeshRenderer && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null)
                    return r;
            return null;
        }

        RenderParams[] MakeParams(Material[] materials, ShadowCastingMode shadows)
        {
            if (materials == null || materials.Length == 0) return null;
            var result = new RenderParams[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material != null && !material.enableInstancing) material.enableInstancing = true;
                result[i] = new RenderParams(material)
                {
                    layer = gameObject.layer,
                    shadowCastingMode = shadows,
                    receiveShadows = true,
                    motionVectorMode = MotionVectorGenerationMode.Camera,
                };
            }
            return result;
        }
    }
}

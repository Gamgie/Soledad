using System.Collections.Generic;
using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// A cluster of cubic clouds (Act 2): the cubes spawn at random inside an ellipsoid around the centre, then in
    /// Play mode each one wanders around its spawn point (Brownian motion, pulled gently back home). Without collisions the
    /// motion is integrated here (cheap); with collisions each cube gets a Rigidbody and collider covering its fluffy
    /// edge (Unity physics, much more costly). They are drawn with Soledad/CloudCube, each with
    /// its own noise seed, and cast shadows on the ground.
    ///
    /// The prefab only stores the settings: the cubes are generated from them (deterministic for a given seed) in the
    /// child "Cubes (generated)", never saved, and rebuilt as soon as a cube setting, the material or its Fluff
    /// changes. Physics only runs in Play mode, at the fixed time step, so renders at a fixed frame rate repeat.
    /// </summary>
    [ExecuteAlways]
    public class CloudCluster : MonoBehaviour
    {
        [Header("Cubes (rebuilt on change)")]
        [Range(1, 40)] public int cubeCount = 9;
        [Tooltip("Edge of the smallest and largest cubes (m).")]
        public Vector2 cubeSize = new Vector2(25f, 70f);
        [Tooltip("How far each side can differ from a perfect cube: 0 = cubes, 0.4 = boxes from 0.6x to 1.4x.")]
        [Range(0f, 0.6f)] public float boxiness = 0.3f;
        [Tooltip("The cubes spawn at random inside this ellipsoid around the centre: its radii (m) across, up, along.")]
        public Vector3 ellipse = new Vector3(120f, 30f, 80f);
        [Tooltip("All cubes keep the same orientation (the cluster's), like the references. Off: each cube has its own rotation and spins.")]
        public bool alignRotation = true;
        public int seed = 1;

        [Header("Motion (Play mode)")]
        [Tooltip("Pull of each cube back to its spawn point (m/s2): the higher, the less it wanders away.")]
        public float attraction = 2f;
        [Tooltip("Cubes bump into each other instead of overlapping (Unity physics). Off: they pass through each other.")]
        public bool collisions = false;
        [Tooltip("Slows the cubes down (higher = calmer, slower). Top speed is about pull / damping.")]
        public float damping = 0.25f;
        [Tooltip("Random push, different for each cube and slowly changing (m/s2): the Brownian wander around the spawn point. 0 = the cubes stay still.")]
        public float turbulence = 1.5f;
        [Tooltip("How fast the turbulence changes direction (cycles per second).")]
        public float turbulenceFrequency = 0.05f;
        [Tooltip("Spin of each cube around itself (degrees per second), between these two.")]
        public Vector2 spinSpeed = new Vector2(2f, 8f);
        [Tooltip("How fast each cube gets back to its own spin after a collision.")]
        public float spinResponse = 0.2f;
        [Tooltip("Slow drift of the whole cluster (m/s), like wind.")]
        public Vector3 drift = new Vector3(0f, 0f, 1.5f);

        [Header("Look (optional, set by CloudField)")]
        [Tooltip("Use the values below instead of the material's. Everything else (colour, scattering, samples, shadow core) still comes from the material.")]
        public bool overrideLook = false;
        [Tooltip("Fluff (m), like the material's.")]
        public float fluff = 12f;
        [Tooltip("Puff Size (m), like the material's.")]
        public float puffSize = 14f;
        public float density = 0.06f;
        [Tooltip("Edge Softness (m), like the material's.")]
        public float softness = 4f;

        [Header("Distance (Play mode)")]
        [Tooltip("Beyond this distance (m) the cloud takes fewer samples per pixel, down to Far Samples at the draw distance.")]
        public float lodDistance = 2500f;
        [Tooltip("Beyond this distance (m) the cloud and its shadow are not drawn (hidden in the fog).")]
        public float drawDistance = 14000f;
        [Tooltip("Fraction of the material's Samples kept at the draw distance.")]
        [Range(0.1f, 1f)] public float farSamples = 0.35f;

        [Header("References")]
        [Tooltip("The cloud settings (CloudCube material). Tweak them there: the cluster volume copies them live.")]
        [OSCQuery.DoNotExpose] public Material cloudMaterial;
        [Tooltip("Soledad/CloudCluster: draws the whole cluster as one volume. Empty = each cube drawn alone (they cut through each other where they overlap).")]
        [OSCQuery.DoNotExpose] public Shader volumeShader;

        static readonly int SeedId = Shader.PropertyToID("_CloudSeed");
        static readonly int FluffId = Shader.PropertyToID("_CloudFluff");
        static readonly int ScaleWithSizeId = Shader.PropertyToID("_CloudScaleWithSize");
        static readonly int ReferenceSizeId = Shader.PropertyToID("_CloudReferenceSize");
        static readonly int NoiseSizeId = Shader.PropertyToID("_CloudNoiseSize");
        static readonly int DensityId = Shader.PropertyToID("_CloudDensity");
        static readonly int SoftnessId = Shader.PropertyToID("_CloudSoftness");
        static readonly int StepsId = Shader.PropertyToID("_CloudSteps");

        readonly List<Rigidbody> _bodies = new List<Rigidbody>();
        readonly List<Vector3> _velocities = new List<Vector3>();
        readonly List<Vector3> _spins = new List<Vector3>();
        readonly List<Vector3> _homes = new List<Vector3>();
        MaterialPropertyBlock _block;
        Transform _container;
        int _builtHash;
        float _time;
        bool _rebuildQueued;

        // ---------------------------------------------------------------- one volume for the whole cluster

        const int MaxCubes = 40;
        static readonly int CubeCountId = Shader.PropertyToID("_CubeCount");
        static readonly int CubeWorldToLocalId = Shader.PropertyToID("_CubeWorldToLocal");
        static readonly int CubeHalfId = Shader.PropertyToID("_CubeHalf");
        static readonly int CubeSeedId = Shader.PropertyToID("_CubeSeed");
        readonly Matrix4x4[] _worldToLocal = new Matrix4x4[MaxCubes];
        readonly Vector4[] _halves = new Vector4[MaxCubes];
        readonly Vector4[] _seeds = new Vector4[MaxCubes];
        readonly List<Transform> _cubes = new List<Transform>();
        readonly List<float> _sizeScales = new List<float>();
        readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        MeshRenderer _volumeRenderer;
        bool _shown = true;
        MaterialPropertyBlock _volumeBlock;
        Transform _volume;

        // The cubes only cast the shadows; the cloud itself is one volume through all of them, fitted around the
        // cluster every frame, so overlapping cubes merge instead of cutting through each other
        void LateUpdate() => UpdateVolume();

        void UpdateVolume()
        {
            if (_volume == null || _volumeRenderer == null || _cubes.Count == 0) return;
            _volumeRenderer.sharedMaterial = SharedVolumeMaterial(volumeShader, cloudMaterial);

            float fluffSize = Fluff();
            Quaternion inverse = Quaternion.Inverse(transform.rotation);
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            int count = Mathf.Min(_cubes.Count, MaxCubes);
            for (int i = 0; i < count; i++)
            {
                Transform cube = _cubes[i];
                if (cube == null) continue;
                Vector3 size = cube.localScale;
                float k = _sizeScales[i];
                _worldToLocal[i] = Matrix4x4.TRS(cube.position, cube.rotation, Vector3.one).inverse;
                _halves[i] = new Vector4(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f, k);
                _seeds[i] = new Vector4(i * 1.618f + seed * 0.37f, 0f, 0f, 0f);

                // Extent of the cube and its fluff, in the cluster's axes
                float reach = 0.5f * size.magnitude + fluffSize * k;
                Vector3 local = inverse * (cube.position - transform.position);
                min = Vector3.Min(min, local - Vector3.one * reach);
                max = Vector3.Max(max, local + Vector3.one * reach);
            }

            _volume.SetPositionAndRotation(transform.position + transform.rotation * ((min + max) * 0.5f), transform.rotation);
            _volume.localScale = max - min;

            // Far clouds: fewer samples, then not drawn at all (nor their shadow) beyond the draw distance
            float steps = cloudMaterial != null && cloudMaterial.HasProperty(StepsId) ? cloudMaterial.GetFloat(StepsId) : 40f;
            if (Application.isPlaying)
            {
                Transform viewer = Viewer();
                if (viewer != null)
                {
                    float distance = Mathf.Max(0f, Vector3.Distance(viewer.position, _volume.position) - 0.5f * _volume.localScale.magnitude);
                    Show(distance < drawDistance);
                    float far = Mathf.Clamp01((distance - lodDistance) / Mathf.Max(drawDistance - lodDistance, 1f));
                    steps = Mathf.Max(4f, steps * Mathf.Lerp(1f, farSamples, far));
                }
            }
            if (!_shown) return;

            // This cluster's own values on top of the shared material
            _volumeBlock ??= new MaterialPropertyBlock();
            _volumeBlock.Clear();
            _volumeBlock.SetFloat(StepsId, steps);
            if (overrideLook)
            {
                _volumeBlock.SetFloat(FluffId, fluff);
                _volumeBlock.SetFloat(NoiseSizeId, puffSize);
                _volumeBlock.SetFloat(DensityId, density);
                _volumeBlock.SetFloat(SoftnessId, softness);
            }
            _volumeBlock.SetFloat(CubeCountId, count);
            _volumeBlock.SetMatrixArray(CubeWorldToLocalId, _worldToLocal);
            _volumeBlock.SetVectorArray(CubeHalfId, _halves);
            _volumeBlock.SetVectorArray(CubeSeedId, _seeds);
            _volumeRenderer.SetPropertyBlock(_volumeBlock);
        }

        void Show(bool shown)
        {
            if (shown == _shown) return;
            _shown = shown;
            if (_volumeRenderer != null) _volumeRenderer.enabled = shown;
            foreach (var r in _renderers) if (r != null) r.enabled = shown;
        }

        // One volume material for all clusters, copied from the cloud material once per frame (every update in edit
        // mode, so its Inspector is followed live); each cluster's own values go through its property block
        static Material s_volumeMaterial;
        static Material s_volumeSource;
        static int s_copiedFrame = -1;

        static Material SharedVolumeMaterial(Shader shader, Material source)
        {
            if (shader == null) return null;
            if (s_volumeMaterial == null || s_volumeMaterial.shader != shader)
            {
                s_volumeMaterial = new Material(shader) { name = "Cloud Volume (runtime)", hideFlags = HideFlags.DontSave };
                s_copiedFrame = -1;
            }
            if (source != null && (!Application.isPlaying || s_copiedFrame != Time.frameCount || s_volumeSource != source))
            {
                s_volumeMaterial.CopyPropertiesFromMaterial(source);
                s_volumeMaterial.renderQueue = 3000;
                s_copiedFrame = Time.frameCount;
                s_volumeSource = source;
            }
            return s_volumeMaterial;
        }

        // The camera named "Main Camera" (Oxipital Cameras prefab), shared by all clusters, searched at most once a second
        static Transform s_viewer;
        static float s_nextViewerSearch;

        static Transform Viewer()
        {
            if (s_viewer != null) return s_viewer;
            if (Time.realtimeSinceStartup < s_nextViewerSearch) return null;
            s_nextViewerSearch = Time.realtimeSinceStartup + 1f;
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (cam.name == "Main Camera") { s_viewer = cam.transform; break; }
            return s_viewer;
        }

        void CreateVolume()
        {
            if (volumeShader == null) return;
            var volume = GameObject.CreatePrimitive(PrimitiveType.Cube);
            volume.name = "Cloud Volume";
            volume.hideFlags = HideFlags.DontSave;
            Object.DestroyImmediate(volume.GetComponent<BoxCollider>());
            volume.transform.SetParent(_container, false);
            _volume = volume.transform;
            var renderer = volume.GetComponent<MeshRenderer>();
            _volumeRenderer = renderer;
            renderer.sharedMaterial = SharedVolumeMaterial(volumeShader, cloudMaterial);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        void OnEnable() => Rebuild();

        void OnDisable()
        {
            ClearCubes();
            _builtHash = 0;
        }

        void OnValidate()
        {
            // Objects cannot be created in OnValidate: rebuild right after, if a cube setting changed
            if (_rebuildQueued) return;
            _rebuildQueued = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                _rebuildQueued = false;
                if (this != null && isActiveAndEnabled && BuildHash() != _builtHash) Rebuild();
                if (this != null) foreach (var body in _bodies) if (body != null) body.linearDamping = damping;
            };
#endif
        }

        int BuildHash()
        {
            float fluffSize = Fluff();
            unchecked
            {
                int h = cubeCount;
                h = h * 31 + cubeSize.GetHashCode();
                h = h * 31 + boxiness.GetHashCode();
                h = h * 31 + ellipse.GetHashCode();
                h = h * 31 + seed;
                h = h * 31 + fluffSize.GetHashCode();
                h = h * 31 + (alignRotation ? 1 : 0);
                h = h * 31 + (collisions ? 1 : 0);
                h = h * 31 + (volumeShader != null ? 1 : 0);
                if (cloudMaterial != null && cloudMaterial.HasProperty(ScaleWithSizeId))
                    h = h * 31 + cloudMaterial.GetFloat(ScaleWithSizeId).GetHashCode() * 7 + cloudMaterial.GetFloat(ReferenceSizeId).GetHashCode();
                h = h * 31 + (cloudMaterial != null ? cloudMaterial.GetInstanceID() : 0);
                return h == 0 ? 1 : h;
            }
        }

        /// <summary>A cube setting changed since the cubes were built.</summary>
        public bool NeedsRebuild => BuildHash() != _builtHash;

        float Fluff() => overrideLook ? fluff : cloudMaterial != null && cloudMaterial.HasProperty(FluffId) ? cloudMaterial.GetFloat(FluffId) : 0f;

        // Same as CC_SizeScale in the shader: big cubes get proportionally more fluff
        float SizeScale(Vector3 size)
        {
            if (cloudMaterial == null || !cloudMaterial.HasProperty(ScaleWithSizeId)) return 1f;
            float edge = (size.x + size.y + size.z) / 3f;
            float reference = Mathf.Max(cloudMaterial.GetFloat(ReferenceSizeId), 1f);
            return Mathf.Lerp(1f, edge / reference, Mathf.Clamp01(cloudMaterial.GetFloat(ScaleWithSizeId)));
        }

        void Update()
        {
            // The material's Fluff can change from its own Inspector: follow it
            if (!Application.isPlaying && BuildHash() != _builtHash) Rebuild();
            if (!Application.isPlaying) UpdateVolume();
            if (Application.isPlaying && drift != Vector3.zero) transform.position += drift * Time.deltaTime;
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying) return;
            _time += Time.fixedDeltaTime;
            float dt = Time.fixedDeltaTime;
            float drag = 1f / (1f + damping * dt);
            for (int i = 0; i < _cubes.Count; i++)
            {
                Transform cube = _cubes[i];
                if (cube == null) continue;
                Rigidbody body = _bodies[i];
                // Everything in the cluster's own space (the cubes' container sits at the cluster, unrotated in it)
                Vector3 position = body != null ? transform.InverseTransformPoint(body.position) : cube.localPosition;
                // Brownian wander around its spawn point: a random push of its own, changing slowly (deterministic,
                // from the fixed-step clock), and a gentle pull back home so it never drifts away
                Vector3 toHome = _homes[i] - position;
                float distance = toHome.magnitude;
                Vector3 acceleration = distance > 1e-3f ? toHome / distance * (attraction * Mathf.Clamp01(distance / 30f)) : Vector3.zero;
                if (turbulence > 0f)
                {
                    float t = _time * turbulenceFrequency;
                    float vertical = ellipse.y / Mathf.Max(Mathf.Max(ellipse.x, ellipse.z), 1f);   // flatter cluster, flatter wander
                    acceleration += turbulence * new Vector3(
                        Mathf.PerlinNoise(t, i * 7.31f + 0.5f) * 2f - 1f,
                        (Mathf.PerlinNoise(t + 31.7f, i * 3.17f + 0.5f) * 2f - 1f) * vertical,
                        Mathf.PerlinNoise(t + 63.1f, i * 5.13f + 0.5f) * 2f - 1f);
                }

                if (body != null)
                {
                    // Collisions: Unity physics moves the cube
                    body.AddForce(transform.rotation * acceleration, ForceMode.Acceleration);
                    body.AddTorque((_spins[i] - body.angularVelocity) * spinResponse, ForceMode.Acceleration);
                }
                else
                {
                    // No collisions: moved here, much cheaper than hundreds of rigidbodies (and nothing to resync
                    // when the floating origin moves the world)
                    Vector3 velocity = (_velocities[i] + acceleration * dt) * drag;
                    _velocities[i] = velocity;
                    cube.localPosition = position + velocity * dt;
                    if (!alignRotation) cube.localRotation = Quaternion.Euler(_spins[i] * (Mathf.Rad2Deg * dt)) * cube.localRotation;
                }
            }
        }

        /// <summary>Builds the cubes from the settings: random sizes, proportions and rotations, packed around the
        /// centre without overlap, each with its own spin and noise seed.</summary>
        [ContextMenu("Rebuild Cubes")]
        public void Rebuild()
        {
            ClearCubes();
            _block ??= new MaterialPropertyBlock();
            _builtHash = BuildHash();

            var container = new GameObject("Cubes (generated)") { hideFlags = HideFlags.DontSave };
            container.transform.SetParent(transform, false);
            _container = container.transform;
            CreateVolume();

            float fluffSize = Fluff();
            var random = new System.Random(seed);
            var placed = new List<(Vector3 position, float radius)>();

            for (int i = 0; i < cubeCount; i++)
            {
                float edge = Mathf.Lerp(cubeSize.x, cubeSize.y, (float)random.NextDouble());
                var size = new Vector3(
                    edge * (1f + boxiness * ((float)random.NextDouble() * 2f - 1f)),
                    edge * (1f + boxiness * ((float)random.NextDouble() * 2f - 1f)),
                    edge * (1f + boxiness * ((float)random.NextDouble() * 2f - 1f)));
                // The fluff grows with the cube like in the shader (Scale With Size)
                float cubeFluff = fluffSize * SizeScale(size);
                // Random place inside the ellipsoid: of a few tries, the one that overlaps the other cubes least
                float radius = 0.5f * size.magnitude * 0.75f + cubeFluff;
                Vector3 position = Vector3.zero;
                float bestClearance = float.MinValue;
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    Vector3 candidate = Vector3.Scale(RandomOnSphere(random) * Mathf.Pow((float)random.NextDouble(), 1f / 3f), ellipse);
                    float clearance = float.MaxValue;
                    foreach (var other in placed)
                        clearance = Mathf.Min(clearance, (other.position - candidate).magnitude - other.radius - radius);
                    if (clearance > bestClearance) { bestClearance = clearance; position = candidate; }
                    if (clearance >= 0f) break;
                }
                placed.Add((position, radius));

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Cloud Cube {i + 1}";
                cube.hideFlags = HideFlags.DontSave;
                cube.transform.SetParent(_container, false);
                cube.transform.localPosition = position;
                var randomRotation = Quaternion.Euler((float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f);
                cube.transform.localRotation = alignRotation ? Quaternion.identity : randomRotation;
                cube.transform.localScale = size;

                // The collider and the culling bounds cover the fluffy edge too, so clouds never overlap
                var hull = new Vector3(1f + 2f * cubeFluff / size.x, 1f + 2f * cubeFluff / size.y, 1f + 2f * cubeFluff / size.z);
                var collider = cube.GetComponent<BoxCollider>();
                if (collisions) collider.size = hull;
                else Object.DestroyImmediate(collider);

                var renderer = cube.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = cloudMaterial;
                // With a cluster volume the cube only casts the shadow; the volume draws the cloud
                renderer.shadowCastingMode = volumeShader != null ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.localBounds = new Bounds(Vector3.zero, hull);
                // Each cube its own puffs. With a cluster volume the cubes only cast shadows: no property block, so
                // their shadows can be drawn instanced (GPU instancing on the material)
                if (volumeShader == null)
                {
                    _block.SetFloat(SeedId, i * 1.618f + seed * 0.37f);
                    renderer.SetPropertyBlock(_block);
                }
                _renderers.Add(renderer);

                // Rigidbodies only when the cubes collide
                Rigidbody body = null;
                if (collisions)
                {
                    body = cube.AddComponent<Rigidbody>();
                    body.useGravity = false;
                    body.isKinematic = !Application.isPlaying;
                    body.mass = size.x * size.y * size.z * 0.001f;
                    body.linearDamping = damping;
                    body.angularDamping = 0.05f;
                    body.collisionDetectionMode = CollisionDetectionMode.Continuous;
                    // Aligned: all cubes keep the cluster's orientation, collisions only push them
                    if (alignRotation) body.constraints = RigidbodyConstraints.FreezeRotation;
                }

                // Its own slow spin, around its own axis (when not aligned)
                Vector3 axis = RandomOnSphere(random);
                Vector3 spin = alignRotation ? Vector3.zero : axis * (Mathf.Lerp(spinSpeed.x, spinSpeed.y, (float)random.NextDouble()) * Mathf.Deg2Rad);
                _bodies.Add(body);
                _velocities.Add(Vector3.zero);
                _spins.Add(spin);
                // Its spawn point: it wanders around it
                _homes.Add(position);
                _cubes.Add(cube.transform);
                _sizeScales.Add(SizeScale(size));
                if (Application.isPlaying && body != null) body.angularVelocity = spin;
            }
            UpdateVolume();
        }

        void ClearCubes()
        {
            _bodies.Clear();
            _velocities.Clear();
            _spins.Clear();
            _cubes.Clear();
            _homes.Clear();
            _sizeScales.Clear();
            _renderers.Clear();
            _volumeRenderer = null;
            _shown = true;
            _volume = null;
            if (_container == null) _container = transform.Find("Cubes (generated)");
            if (_container == null) return;
            if (Application.isPlaying) Destroy(_container.gameObject);
            else DestroyImmediate(_container.gameObject);
            _container = null;
        }

        static Vector3 RandomOnSphere(System.Random random)
        {
            float z = (float)random.NextDouble() * 2f - 1f;
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }
    }
}

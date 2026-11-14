using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soledad
{
    /// <summary>
    /// Act 2 (Flythrough): a bird's flight over an endless landscape, desert, then mountains, then the valley, laid out
    /// along a route (TerrainShape). The landscape is fixed in the space of <see cref="world"/>; the bird flies along
    /// <see cref="flightPath"/> (a spline in that space) at <see cref="altitude"/> metres above the ground.
    ///
    /// Floating origin: the camera rig (Oxipital, in Soledad_Main) stays at this object's position, and the act only
    /// translates <see cref="world"/> so the bird sits there. No rotation, so the sun stays fixed relative to the ground,
    /// and float precision stays good however far the flight goes. The camera can follow the path's heading
    /// (<see cref="cameraFollowsHeading"/>): the turn is added to the Orbital and Spaceship rigs, the gamepad on top.
    ///
    /// The terrain is a quadtree of tiles around the camera named "Main Camera" (dense near it, coarse towards the
    /// horizon). Tiles are built in terrain space by a Burst job (TerrainField), with skirts hiding the seams between
    /// levels, and drawn with Soledad/FlythroughTerrain, which also rolls the world up (<see cref="curlLateral"/>,
    /// <see cref="curlForward"/>). Tiles and meshes are never saved in the scene.
    ///
    /// Time is accumulated from Time.deltaTime, so a Recorder capture at a fixed frame rate is deterministic.
    /// </summary>
    [ExecuteAlways]
    public class FlythroughTerrain : MonoBehaviour
    {
        [Header("Flight")]
        [Animate] [Tooltip("Forward speed (m/s).")]
        public float speed = 60f;
        [Animate] [Tooltip("Height above the ground (m). Smoothed, and anticipated so the flight rises before a mountain.")]
        public float altitude = 140f;
        [Animate] [Tooltip("Sideways offset from the flight path (m), positive = right.")]
        public float pathOffset = 0f;
        [Animate] [Tooltip("Turn the camera with the path (added to the Orbital and Spaceship rigs, the gamepad stays on top).")]
        public bool cameraFollowsHeading = true;
        [Tooltip("Seconds for the flight height to settle on the ground below.")]
        public float altitudeSmoothing = 2.5f;
        [Tooltip("How far ahead (m) the ground is anticipated.")]
        public float lookAhead = 600f;
        [Tooltip("Distance along the path (m) where the flight starts (ResetFlight).")]
        public float startDistance = 0f;
        [Tooltip("At the end of the path: start again from the beginning. Off: stop there.")]
        public bool loopPath = false;

        [Header("World curl")]
        [Animate] [Tooltip("Rolls the ground up into a tube around the flight axis. 0 = flat, 1 = closed tube of radius curlRadius.")]
        [Range(0f, 1f)] public float curlLateral = 0f;
        [Tooltip("Tube radius (m) at lateral curl 1.")]
        public float curlRadius = 2500f;
        [Animate] [Tooltip("Lifts the ground ahead into a wall. 0 = flat, 1 = bend of radius bendRadius.")]
        [Range(0f, 1f)] public float curlForward = 0f;
        [Tooltip("Bend radius (m) at forward curl 1.")]
        public float bendRadius = 4000f;
        [Tooltip("While curled, thin the fog of the act Volume (Play mode): far curled ground otherwise fades into the sky.")]
        [Range(0f, 1f)] public float curlClearsFog = 1f;
        [Tooltip("While curled, fade the volumetric clouds of the act Volume (Play mode): the curled ground crosses the cloud layer.")]
        [Range(0f, 1f)] public float curlClearsClouds = 1f;

        [Header("Sun (the Sun of Soledad_Main, while the act is shown)")]
        [Tooltip("Aim the main scene's Sun from this act. Its own rotation comes back when the act ends.")]
        public bool overrideSun = true;
        [Animate] [Tooltip("Sun height above the horizon (degrees). Low = long shadows across the dunes.")]
        [Range(-5f, 90f)] public float sunElevation = 14f;
        [Animate] [Tooltip("Sun direction in the landscape (degrees): 0 = shining towards +Z (the start of the route runs along +Z).")]
        [Range(-180f, 180f)] public float sunAzimuth = 150f;
        [Tooltip("Sun shadow map resolution while the act is shown. Low sun = long shadows: 512 makes them blocky.")]
        public int sunShadowResolution = 4096;

        [Header("Landscape (rebuilds the terrain)")]
        public TerrainShape shape = TerrainShape.Default;

        [Header("Tiles (rebuild the terrain)")]
        [Tooltip("Size of the smallest tiles, near the camera (m).")]
        public float leafSize = 32f;
        [Tooltip("Quadtree levels: the largest tiles are leafSize x 2^levels.")]
        [Range(4, 14)] public int levels = 10;
        [Tooltip("Cells per tile side. More = finer relief everywhere.")]
        [Range(8, 64)] public int cellsPerTile = 48;
        [Tooltip("A tile splits when the camera is closer than its size times this. More = more detail far away.")]
        [Range(1f, 6f)] public float detailDistance = 1.5f;
        [Tooltip("Far clip plane given to every camera while the act is shown, in Play mode (m). The Oxipital cameras stop at 1 km.")]
        public float farClip = 40000f;
        [Tooltip("Keep flying in the Editor outside Play mode (Editor clock).")]
        public bool animateInEditMode = false;

        [Header("References")]
        [Tooltip("The fixed landscape: tiles, path and placed objects live under it. Translated so the bird stays here.")]
        [OSCQuery.DoNotExpose] public Transform world;
        [Tooltip("Flight path, a spline under World. Empty = the route centre line of the landscape.")]
        [OSCQuery.DoNotExpose] public UnityEngine.Splines.SplineContainer flightPath;
        [Tooltip("The act's Volume, whose fog and clouds clear while curled.")]
        [OSCQuery.DoNotExpose] public Volume actVolume;
        [OSCQuery.DoNotExpose] public Material terrainMaterial;
        [Tooltip("Empty = the camera named \"Main Camera\" (Oxipital Cameras prefab), found at run time.")]
        [OSCQuery.DoNotExpose] public Transform followTarget;

        static readonly int CurlOriginId = Shader.PropertyToID("_CurlOrigin");
        static readonly int CurlLateralId = Shader.PropertyToID("_CurlLateral");
        static readonly int CurlForwardId = Shader.PropertyToID("_CurlForward");
        static readonly int CurlFrameId = Shader.PropertyToID("_CurlFrame");

        class Tile
        {
            public GameObject gameObject;
            public MeshRenderer renderer;
            public Mesh mesh;
            public long key;
        }

        // Flight state, in the space of `world`
        double _distance;           // along the path (m)
        double2 _position;          // bird xz
        double _heading;            // radians, 0 = +Z
        float _pathHeight;          // height of the path itself, added to the altitude
        float _ground;
        bool _groundReady, _started;
        double _cameraHeading;      // heading already applied to the camera rigs
        bool _cameraHeadingReady;

        Transform _root;
        readonly Dictionary<long, Tile> _tiles = new Dictionary<long, Tile>();
        readonly Stack<Tile> _pool = new Stack<Tile>();
        readonly HashSet<long> _wanted = new HashSet<long>();
        readonly List<long> _toRemove = new List<long>();
        readonly List<long> _toBuild = new List<long>();
        MaterialPropertyBlock _block;
        bool _dirty = true, _curlBounds;
        uint _builtHash;
        Transform _foundTarget;
        float _nextTargetSearch;

        /// <summary>Back to the start of the route.</summary>
        [Animate]
        public void ResetFlight()
        {
            _distance = startDistance;
            SamplePath();
            _groundReady = false;
        }

        // Everything the tile meshes depend on: a change rebuilds them
        uint BuildHash()
        {
            TerrainShape s = shape;
            return math.hash(new float4(s.seed, s.desertEnd, s.mountainEnd, s.transition))
                ^ math.hash(new float4(s.borderWarp, s.meanderAmplitude, s.meanderWavelength, s.duneHeight)) * 3u
                ^ math.hash(new float4(s.duneWavelength, s.duneDirection, s.desertRelief, s.mountainHeight)) * 7u
                ^ math.hash(new float4(s.mountainScale, s.passWidth, s.valleyWidth, s.valleyRidgeHeight)) * 13u
                ^ math.hash(new float4(leafSize, levels, cellsPerTile, 0f)) * 17u;
        }

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            if (!_started)
            {
                ResetFlight();
                _started = true;
            }
            _dirty = true;
            ApplyFarClip();
            Tick(0f);
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnSceneSaved;
            _lastEditorTime = UnityEditor.EditorApplication.timeSinceStartup;
            UnityEditor.EditorApplication.update -= EditorTick;
            UnityEditor.EditorApplication.update += EditorTick;
#endif
        }

        // Cameras whose far plane was pushed out, with their own value to restore
        readonly List<Camera> _farCameras = new List<Camera>();
        readonly List<float> _farOriginals = new List<float>();

        // Play mode only, so the main scene's cameras are never saved with the act's value
        void ApplyFarClip()
        {
            if (!Application.isPlaying) return;
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (_farCameras.Contains(cam)) continue;
                _farCameras.Add(cam);
                _farOriginals.Add(cam.farClipPlane);
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, farClip);
            }
        }

        void RestoreFarClip()
        {
            for (int i = 0; i < _farCameras.Count; i++)
                if (_farCameras[i] != null) _farCameras[i].farClipPlane = _farOriginals[i];
            _farCameras.Clear();
            _farOriginals.Clear();
        }

        // ---------------------------------------------------------------- atmosphere while curled

        VolumeProfile _sharedProfile, _runtimeProfile;
        UnityEngine.Rendering.HighDefinition.Fog _fog;
        UnityEngine.Rendering.HighDefinition.VolumetricClouds _clouds;
        float _fogMeanFreePath, _cloudDensity;

        // Play mode only: works on a copy of the act Volume's profile, the asset is never touched
        void UpdateAtmosphere()
        {
            if (!Application.isPlaying || actVolume == null) return;
            if (_runtimeProfile == null)
            {
                _sharedProfile = actVolume.sharedProfile;
                if (_sharedProfile == null) return;
                _runtimeProfile = Instantiate(_sharedProfile);
                _runtimeProfile.name = _sharedProfile.name + " (runtime)";
                actVolume.sharedProfile = _runtimeProfile;
                // A value only counts when its override is on: switch them on, keeping the profile's own values
                if (_runtimeProfile.TryGet(out _fog))
                {
                    _fogMeanFreePath = _fog.meanFreePath.value;
                    _fog.meanFreePath.overrideState = true;
                }
                if (_runtimeProfile.TryGet(out _clouds))
                {
                    _cloudDensity = _clouds.densityMultiplier.value;
                    _clouds.densityMultiplier.overrideState = true;
                }
            }
            float curl = math.max(curlLateral, curlForward);
            if (_fog != null) _fog.meanFreePath.value = _fogMeanFreePath * math.lerp(1f, 40f, curl * curlClearsFog);
            if (_clouds != null) _clouds.densityMultiplier.value = _cloudDensity * (1f - curl * curlClearsClouds);
        }

        void RestoreAtmosphere()
        {
            if (_runtimeProfile == null) return;
            if (actVolume != null && actVolume.sharedProfile == _runtimeProfile) actVolume.sharedProfile = _sharedProfile;
            DestroyUnityObject(_runtimeProfile);
            _runtimeProfile = null;
            _fog = null;
            _clouds = null;
        }

        // ---------------------------------------------------------------- sun

        Light _sun;
        Quaternion _sunOriginal;
        bool _sunOverridden;
        UnityEngine.Rendering.HighDefinition.HDAdditionalLightData _sunData;
        int _sunResolutionOriginal;
        bool _sunResolutionUseOverride;

        void ApplySun()
        {
            if (!overrideSun)
            {
                RestoreSun();
                return;
            }
            if (_sun == null) _sun = FindSun();
            if (_sun == null) return;
            if (!_sunOverridden)
            {
                _sunOriginal = _sun.transform.rotation;
                _sunData = _sun.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
                if (_sunData != null)
                {
                    _sunResolutionOriginal = _sunData.shadowResolution.@override;
                    _sunResolutionUseOverride = _sunData.shadowResolution.useOverride;
                    _sunData.shadowResolution.useOverride = true;
                    _sunData.shadowResolution.@override = sunShadowResolution;
                }
                _sunOverridden = true;
            }
            var rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
            if (_sun.transform.rotation != rotation) _sun.transform.rotation = rotation;
        }

        void RestoreSun()
        {
            if (_sunOverridden && _sun != null)
            {
                _sun.transform.rotation = _sunOriginal;
                if (_sunData != null)
                {
                    _sunData.shadowResolution.@override = _sunResolutionOriginal;
                    _sunData.shadowResolution.useOverride = _sunResolutionUseOverride;
                }
            }
            _sunOverridden = false;
        }

        // The directional light named "Sun" (Soledad_Main), else the first directional light
        static Light FindSun()
        {
            Light first = null;
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                if (light.name == "Sun") return light;
                if (first == null) first = light;
            }
            return first;
        }

#if UNITY_EDITOR
        // Never save the act's sun into Soledad_Main: put the original back while a scene is written
        // Same for the World offset in the act scene
        void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            RestoreSun();
            RestoreWorld();
        }

        void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
        {
            ApplySun();
            if (_root != null) PlaceWorld();
        }
#endif

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
#endif
            RestoreSun();
            RestoreFarClip();
            RestoreAtmosphere();
            RestoreWorld();
            _cameraHeadingReady = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            ClearTiles(true);
            if (_root != null) DestroyUnityObject(_root.gameObject);
            _root = null;
        }

#if UNITY_EDITOR
        double _lastEditorTime;

        void EditorTick()
        {
            if (Application.isPlaying || !animateInEditMode || this == null || !isActiveAndEnabled) return;
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now - _lastEditorTime < 1.0 / 60.0) return;
            float dt = (float)System.Math.Min(now - _lastEditorTime, 0.1);
            _lastEditorTime = now;
            Tick(dt);
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
        }
#endif

        void Update()
        {
            // Outside Play mode, EditorTick advances the flight; here only apply edits made in the Inspector
            Tick(Application.isPlaying ? Time.deltaTime : 0f);
        }

        [ContextMenu("Rebuild Terrain")]
        void Rebuild() => _dirty = true;

        void Tick(float dt)
        {
            if (terrainMaterial == null) return;
            EnsureRoot();
            uint hash = BuildHash();
            if (_dirty || hash != _builtHash)
            {
                ClearTiles(false);
                _dirty = false;
                _builtHash = hash;
            }

            Fly(dt);
            PlaceWorld();
            TurnCameras();
            UpdateTiles();
            UpdateMaterial();
            UpdateAtmosphere();
            ApplySun();
        }

        // ---------------------------------------------------------------- flight

        void Fly(float dt)
        {
            _distance += speed * dt;
            SamplePath();

            // Ground below and ahead: rise early for what is coming
            float2 p = (float2)_position;
            float2 forward = new float2((float)math.sin(_heading), (float)math.cos(_heading));
            float target = TerrainField.Height(p, shape);
            for (int k = 1; k <= 4; k++)
            {
                float ahead = TerrainField.Height(p + forward * (lookAhead * k / 4f), shape);
                target = math.max(target, ahead - altitude * 0.25f * k);
            }
            if (!_groundReady)
            {
                _ground = target;
                _groundReady = true;
            }
            else if (dt > 0f)
            {
                _ground = math.lerp(_ground, target, 1f - math.exp(-dt / math.max(altitudeSmoothing, 1e-3f)));
            }
        }

        // Bird position and heading at _distance along the path (spline in world space), or along the route centre line
        void SamplePath()
        {
            var spline = flightPath != null ? flightPath.Spline : null;
            float3 position, tangent;
            if (spline != null && spline.Count >= 2)
            {
                float length = spline.GetLength();
                double d = loopPath ? _distance - math.floor(_distance / length) * length : math.clamp(_distance, 0.0, length);
                float t = length > 0f ? (float)(d / length) : 0f;
                position = UnityEngine.Splines.SplineUtility.EvaluatePosition(spline, t);
                tangent = UnityEngine.Splines.SplineUtility.EvaluateTangent(spline, t);
                // The container may sit anywhere under World: bring the path into World space
                if (world != null && flightPath.transform != world)
                {
                    Matrix4x4 m = world.worldToLocalMatrix * flightPath.transform.localToWorldMatrix;
                    position = m.MultiplyPoint3x4(position);
                    tangent = m.MultiplyVector(tangent);
                }
            }
            else
            {
                float z = (float)_distance;
                position = new float3(TerrainField.RouteX(z, shape), 0f, z);
                tangent = new float3(TerrainField.RouteSlope(z, shape), 0f, 1f);
            }

            float2 forward = math.normalizesafe(tangent.xz, new float2(0f, 1f));
            float2 right = new float2(forward.y, -forward.x);
            float2 xz = position.xz + right * pathOffset;
            _position = new double2(xz.x, xz.y);
            _heading = math.atan2(forward.x, forward.y);
            _pathHeight = position.y;
        }

        // Floating origin: World is translated (never rotated) so the bird sits on this object
        void PlaceWorld()
        {
            Transform w = world != null ? world : _root;
            var bird = new Vector3((float)_position.x, _ground + altitude + _pathHeight, (float)_position.y);
            Vector3 position = transform.position - bird;
            if (w.position != position || w.rotation != Quaternion.identity)
                w.SetPositionAndRotation(position, Quaternion.identity);
        }

        void RestoreWorld()
        {
            if (world != null) world.localPosition = Vector3.zero;
        }

        // Turn the Oxipital camera rigs with the path, around this object (Play mode). Orbital and Spaceship only:
        // the Sensor rig takes its rotation from the body sensor.
        void TurnCameras()
        {
            if (!Application.isPlaying) return;
            if (!_cameraHeadingReady)
            {
                _cameraHeading = _heading;
                _cameraHeadingReady = true;
                return;
            }
            float delta = (float)(_heading - _cameraHeading);
            delta = math.atan2(math.sin(delta), math.cos(delta));
            _cameraHeading = _heading;
            if (!cameraFollowsHeading || math.abs(delta) < 1e-6f) return;

            var turn = Quaternion.AngleAxis(math.degrees(delta), Vector3.up);
            Vector3 pivot = transform.position;
            if (_cameraController == null) _cameraController = FindFirstObjectByType<Oxipital.CameraController>(FindObjectsInactive.Include);
            if (_cameraController == null) return;

            if (_cameraController.orbitalCamera != null)
            {
                var orbital = _cameraController.orbitalCamera;
                orbital.positionTarget = pivot + turn * (orbital.positionTarget - pivot);
                orbital.rotateYAngle += math.degrees(delta);
                Transform t = orbital.transform;
                t.SetPositionAndRotation(pivot + turn * (t.position - pivot), turn * t.rotation);
            }
            if (_cameraController.spaceshipCamera != null)
            {
                Transform t = _cameraController.spaceshipCamera.transform;
                var body = t.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = pivot + turn * (body.position - pivot);
                    body.rotation = turn * body.rotation;
                    if (!body.isKinematic) body.linearVelocity = turn * body.linearVelocity;
                }
                t.SetPositionAndRotation(pivot + turn * (t.position - pivot), turn * t.rotation);
            }
        }

        Oxipital.CameraController _cameraController;

        // ---------------------------------------------------------------- tiles

        void UpdateTiles()
        {
            Transform target = followTarget != null ? followTarget : FindFollowTarget();
            Vector3 cameraTerrain = target != null ? _root.InverseTransformPoint(target.position)
                : new Vector3((float)_position.x, _ground + altitude, (float)_position.y);
            float2 c = new float2(cameraTerrain.x, cameraTerrain.z);
            float heightAbove = math.max(1f, cameraTerrain.y - TerrainField.Height(c, shape));

            _wanted.Clear();
            float rootSize = leafSize * (1 << levels);
            long rx = (long)math.floor(c.x / rootSize), rz = (long)math.floor(c.y / rootSize);
            for (long ix = rx - 1; ix <= rx + 1; ix++)
                for (long iz = rz - 1; iz <= rz + 1; iz++)
                    Collect(levels, ix, iz, c, heightAbove);

            _toRemove.Clear();
            foreach (var pair in _tiles)
                if (!_wanted.Contains(pair.Key)) _toRemove.Add(pair.Key);
            foreach (long key in _toRemove)
            {
                Tile tile = _tiles[key];
                tile.gameObject.SetActive(false);
                _pool.Push(tile);
                _tiles.Remove(key);
            }

            _toBuild.Clear();
            foreach (long key in _wanted)
                if (!_tiles.ContainsKey(key)) _toBuild.Add(key);
            if (_toBuild.Count > 0) BuildTiles();
        }

        void Collect(int level, long ix, long iz, float2 camera, float heightAbove)
        {
            float size = leafSize * (1 << level);
            float2 min = new float2(ix, iz) * size;
            float2 d = math.max(math.max(min - camera, camera - (min + size)), 0f);
            float distance = math.sqrt(math.lengthsq(d) + heightAbove * heightAbove);
            if (level > 0 && distance < size * detailDistance)
            {
                for (int k = 0; k < 4; k++)
                    Collect(level - 1, ix * 2 + (k & 1), iz * 2 + (k >> 1), camera, heightAbove);
                return;
            }
            _wanted.Add(Key(level, ix, iz));
        }

        static long Key(int level, long ix, long iz) =>
            ((long)level << 58) | (((ix + (1L << 28)) & 0x1FFFFFFFL) << 29) | ((iz + (1L << 28)) & 0x1FFFFFFFL);

        static void Unkey(long key, out int level, out long ix, out long iz)
        {
            level = (int)(key >> 58);
            ix = ((key >> 29) & 0x1FFFFFFFL) - (1L << 28);
            iz = (key & 0x1FFFFFFFL) - (1L << 28);
        }

        void BuildTiles()
        {
            int count = _toBuild.Count;
            int cells = math.clamp(cellsPerTile, 8, 64);
            int grid = cells + 1;
            int vertexCount = grid * grid + 4 * grid;
            int indexCount = cells * cells * 6 + 4 * cells * 6;

            var requests = new NativeArray<TileRequest>(count, Allocator.TempJob);
            var ranges = new NativeArray<float2>(count, Allocator.TempJob);
            var meshData = Mesh.AllocateWritableMeshData(count);
            var meshes = new Mesh[count];

            for (int i = 0; i < count; i++)
            {
                Unkey(_toBuild[i], out int level, out long ix, out long iz);
                float size = leafSize * (1 << level);
                requests[i] = new TileRequest { origin = new float2(ix, iz) * size, size = size };

                Mesh.MeshData data = meshData[i];
                data.SetVertexBufferParams(vertexCount, TileVertex.Layout);
                data.SetIndexBufferParams(indexCount, IndexFormat.UInt16);

                Tile tile = _pool.Count > 0 ? _pool.Pop() : CreateTile();
                tile.key = _toBuild[i];
                _tiles[tile.key] = tile;
                meshes[i] = tile.mesh;
            }

            new TileJob { requests = requests, meshes = meshData, ranges = ranges, cells = cells, shape = shape }
                .Schedule(count, 1).Complete();

            for (int i = 0; i < count; i++)
            {
                TileRequest r = requests[i];
                float skirt = r.size / cells * 4f + 10f;
                var bounds = new Bounds();
                bounds.SetMinMax(new Vector3(r.origin.x, ranges[i].x - skirt, r.origin.y),
                                 new Vector3(r.origin.x + r.size, ranges[i].y, r.origin.y + r.size));
                Mesh.MeshData data = meshData[i];     // a handle: setting it through a copy is fine
                data.subMeshCount = 1;
                data.SetSubMesh(0, new SubMeshDescriptor(0, indexCount) { bounds = bounds, vertexCount = vertexCount },
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
            Mesh.ApplyAndDisposeWritableMeshData(meshData, meshes, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            for (int i = 0; i < count; i++)
            {
                TileRequest r = requests[i];
                Tile tile = _tiles[_toBuild[i]];
                tile.mesh.bounds = tile.mesh.GetSubMesh(0).bounds;
                tile.renderer.sharedMaterial = terrainMaterial;
                tile.renderer.SetPropertyBlock(_block);
                SetCurlBounds(tile, _curlBounds);
                tile.gameObject.SetActive(true);
            }

            requests.Dispose();
            ranges.Dispose();
        }

        Tile CreateTile()
        {
            var go = new GameObject("Terrain Tile") { hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy };
            go.transform.SetParent(_root, false);
            var tile = new Tile
            {
                gameObject = go,
                mesh = new Mesh { name = "Terrain Tile (generated)", hideFlags = HideFlags.DontSave },
            };
            go.AddComponent<MeshFilter>().sharedMesh = tile.mesh;
            tile.renderer = go.AddComponent<MeshRenderer>();
            tile.renderer.shadowCastingMode = ShadowCastingMode.On;
            tile.renderer.receiveShadows = true;
            tile.renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            return tile;
        }

        void ClearTiles(bool destroy)
        {
            foreach (var tile in _tiles.Values)
            {
                if (destroy) DestroyTile(tile);
                else
                {
                    if (tile.gameObject != null) tile.gameObject.SetActive(false);
                    _pool.Push(tile);
                }
            }
            _tiles.Clear();
            if (destroy)
            {
                while (_pool.Count > 0) DestroyTile(_pool.Pop());
            }
        }

        void DestroyTile(Tile tile)
        {
            if (tile.mesh != null) DestroyUnityObject(tile.mesh);
            if (tile.gameObject != null) DestroyUnityObject(tile.gameObject);
        }

        static void DestroyUnityObject(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void EnsureRoot()
        {
            Transform parent = world != null ? world : transform;
            if (_root != null && _root.parent == parent) return;
            if (_root != null)
            {
                ClearTiles(true);
                DestroyUnityObject(_root.gameObject);
            }
            var go = new GameObject("Terrain (generated)") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            _root = go.transform;
        }

        // ---------------------------------------------------------------- material

        void UpdateMaterial()
        {
            // Curl around the ground point under the bird, along its heading
            Vector3 origin = transform.position - new Vector3(0f, altitude + _pathHeight, 0f);
            float lateral = curlLateral > 0f ? curlLateral / math.max(curlRadius, 1f) : 0f;
            float forward = curlForward > 0f ? curlForward / math.max(bendRadius, 1f) : 0f;

            _block.SetVector(CurlOriginId, origin);
            _block.SetFloat(CurlLateralId, lateral);
            _block.SetFloat(CurlForwardId, forward);
            _block.SetVector(CurlFrameId, new Vector4((float)math.sin(_heading), (float)math.cos(_heading), 0f, 0f));

            // Curled vertices leave their tile bounds: stop culling the tiles while the world is curled
            bool curled = lateral > 0f || forward > 0f;
            bool boundsChanged = curled != _curlBounds;
            _curlBounds = curled;
            foreach (var tile in _tiles.Values)
            {
                tile.renderer.SetPropertyBlock(_block);
                if (boundsChanged) SetCurlBounds(tile, curled);
            }
        }

        static void SetCurlBounds(Tile tile, bool curled)
        {
            if (curled) tile.renderer.localBounds = new Bounds(tile.mesh.bounds.center, new Vector3(1e6f, 1e6f, 1e6f));
            else tile.renderer.ResetLocalBounds();
        }

        Transform FindFollowTarget()
        {
            if (_foundTarget != null) return _foundTarget;
            float now = Time.realtimeSinceStartup;
            if (now < _nextTargetSearch) return null;
            _nextTargetSearch = now + 1f;
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Transform tagged = null;
            foreach (var cam in cameras)
            {
                if (cam.name == "Main Camera") { _foundTarget = cam.transform; return _foundTarget; }
                if (tagged == null && cam.CompareTag("MainCamera")) tagged = cam.transform;
            }
            _foundTarget = tagged;
            return _foundTarget;
        }

        // ---------------------------------------------------------------- tile job

        struct TileRequest
        {
            public float2 origin;
            public float size;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TileVertex
        {
            public float3 position;
            public float3 normal;
            public float4 tangent;
            public float4 color;
            public float2 uv0;
            public float2 uv1;

            public static readonly VertexAttributeDescriptor[] Layout =
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
            };
        }

        // Builds one tile per index: a (cells + 1)^2 grid in terrain space, plus a skirt hanging below each edge
        [BurstCompile]
        struct TileJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<TileRequest> requests;
            public Mesh.MeshDataArray meshes;
            public NativeArray<float2> ranges;
            public int cells;
            public TerrainShape shape;

            public void Execute(int index)
            {
                TileRequest r = requests[index];
                Mesh.MeshData mesh = meshes[index];
                NativeArray<TileVertex> vertices = mesh.GetVertexData<TileVertex>();
                NativeArray<ushort> indices = mesh.GetIndexData<ushort>();

                int grid = cells + 1;
                int border = grid + 2;
                float step = r.size / cells;
                float skirt = step * 4f + 10f;

                // Heights with a one-sample border, for normals that match across tiles
                var heights = new NativeArray<float>(border * border, Allocator.Temp);
                var biomes = new NativeArray<float4>(grid * grid, Allocator.Temp);
                float2 range = new float2(float.MaxValue, float.MinValue);
                for (int j = -1; j <= grid; j++)
                {
                    for (int i = -1; i <= grid; i++)
                    {
                        float2 p = r.origin + new float2(i, j) * step;
                        bool inside = i >= 0 && j >= 0 && i < grid && j < grid;
                        float h = TerrainField.Height(p, shape, out float3 biome, out float duneLevel);
                        heights[(i + 1) + (j + 1) * border] = h;
                        if (inside)
                        {
                            biomes[i + j * grid] = new float4(biome, duneLevel);
                            range = new float2(math.min(range.x, h), math.max(range.y, h));
                        }
                    }
                }
                ranges[index] = range;

                for (int j = 0; j < grid; j++)
                {
                    for (int i = 0; i < grid; i++)
                    {
                        float h = heights[(i + 1) + (j + 1) * border];
                        float dx = heights[(i + 2) + (j + 1) * border] - heights[i + (j + 1) * border];
                        float dz = heights[(i + 1) + (j + 2) * border] - heights[(i + 1) + j * border];
                        float3 normal = math.normalize(new float3(-dx, 2f * step, -dz));
                        float2 p = r.origin + new float2(i, j) * step;
                        vertices[i + j * grid] = new TileVertex
                        {
                            position = new float3(p.x, h, p.y),
                            normal = normal,
                            tangent = new float4(math.normalize(new float3(2f * step, dx, 0f)), -1f),   // bitangent = +Z
                            color = biomes[i + j * grid],                 // biome weights, a = dune level
                            uv0 = p,
                            uv1 = new float2(h, 1f - normal.y),
                        };
                    }
                }

                // Skirts: copies of the edge vertices, lowered
                int skirtBase = grid * grid;
                for (int k = 0; k < grid; k++)
                {
                    CopyDown(vertices, k, skirtBase + k, skirt);                                 // south (z min)
                    CopyDown(vertices, k + cells * grid, skirtBase + grid + k, skirt);           // north (z max)
                    CopyDown(vertices, k * grid, skirtBase + 2 * grid + k, skirt);               // west (x min)
                    CopyDown(vertices, cells + k * grid, skirtBase + 3 * grid + k, skirt);       // east (x max)
                }

                int t = 0;
                for (int j = 0; j < cells; j++)
                {
                    for (int i = 0; i < cells; i++)
                    {
                        int a = i + j * grid;
                        int b = a + grid;     // +Z
                        int c = a + 1;        // +X
                        int e = b + 1;
                        indices[t++] = (ushort)a; indices[t++] = (ushort)b; indices[t++] = (ushort)c;
                        indices[t++] = (ushort)c; indices[t++] = (ushort)b; indices[t++] = (ushort)e;
                    }
                }
                for (int k = 0; k < cells; k++)
                {
                    // Each skirt faces outwards
                    Quad(indices, ref t, k, k + 1, skirtBase + k + 1, skirtBase + k);                                            // south
                    Quad(indices, ref t, k + 1 + cells * grid, k + cells * grid, skirtBase + grid + k, skirtBase + grid + k + 1); // north
                    Quad(indices, ref t, (k + 1) * grid, k * grid, skirtBase + 2 * grid + k, skirtBase + 2 * grid + k + 1);     // west
                    Quad(indices, ref t, cells + k * grid, cells + (k + 1) * grid, skirtBase + 3 * grid + k + 1, skirtBase + 3 * grid + k); // east
                }

                heights.Dispose();
                biomes.Dispose();
            }

            static void CopyDown(NativeArray<TileVertex> vertices, int from, int to, float depth)
            {
                TileVertex v = vertices[from];
                v.position.y -= depth;
                vertices[to] = v;
            }

            // Two clockwise triangles: top-left, top-right, bottom-right, bottom-left as seen from the front
            static void Quad(NativeArray<ushort> indices, ref int t, int topLeft, int topRight, int bottomRight, int bottomLeft)
            {
                indices[t++] = (ushort)topLeft; indices[t++] = (ushort)topRight; indices[t++] = (ushort)bottomRight;
                indices[t++] = (ushort)topLeft; indices[t++] = (ushort)bottomRight; indices[t++] = (ushort)bottomLeft;
            }
        }
    }
}

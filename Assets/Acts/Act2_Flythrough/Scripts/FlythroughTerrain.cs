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
    /// <summary>How the Act 2 world curl is oriented: along the camera's motion, or a fixed axis of the landscape.</summary>
    public enum CurlFrame { CameraDirection, Axis }

    /// <summary>
    /// Act 2 (Flythrough): a bird's flight over an endless landscape, desert, then mountains, then the valley, laid out
    /// along a route (TerrainShape). The landscape is fixed in the space of <see cref="world"/>, and the Oxipital camera
    /// (Soledad_Main) really flies through it: the Spline camera rides <see cref="flightPath"/> (handed to it in Play
    /// mode, with this landscape as its ground, so it keeps its altitude above the relief) for the film, the Spaceship
    /// camera flies freely for the show. <see cref="ResetFlight"/> puts both at the start of the path.
    ///
    /// The terrain is a quadtree of tiles around the camera named "Main Camera" (dense near it, coarse towards the
    /// horizon). Tiles are built in terrain space by a Burst job (TerrainField), with skirts hiding the seams between
    /// levels, and drawn with Soledad/FlythroughTerrain, which also rolls the world up (<see cref="curlLateral"/>,
    /// <see cref="curlForward"/>). Tiles and meshes are never saved in the scene.
    ///
    /// Time is accumulated from Time.deltaTime, so a Recorder capture at a fixed frame rate is deterministic.
    /// </summary>
    [ExecuteAlways]
    public class FlythroughTerrain : MonoBehaviour, Oxipital.ICameraGround
    {
        [Header("Flight (the Oxipital cameras)")]
        [Tooltip("Play mode: hand the flight path and this landscape (its ground) to the Spline camera while the act is shown. " +
                 "Speed, altitude, look and drift are set on Cameras/Spline Camera target.")]
        public bool driveSplineCamera = true;
        [Tooltip("Movie mode: when the act is first shown (Play mode), switch the Oxipital camera to Spline and start the ride at the beginning of the path.")]
        public bool startOnSplineCamera = true;

        [Header("World curl")]
        [Animate] [Tooltip("Camera Direction: the world curls along the direction the camera moves in (smoothed). Axis: along a fixed direction of the landscape (curlAxis).")]
        public CurlFrame curlFrame = CurlFrame.CameraDirection;
        [Animate] [Tooltip("Axis mode: curl direction in the landscape (degrees), 0 = +Z (the route), 90 = +X.")]
        [Range(-180f, 180f)] public float curlAxis = 0f;
        [Tooltip("Camera Direction mode: seconds for the curl to turn with a change of flight direction. The ground point under the camera follows with this smoothing in both modes.")]
        public float curlHeadingSmoothing = 1.5f;
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
        [Tooltip("Curl at which the fog and clouds are fully cleared: low = they clear early in the curl, so the " +
                 "curled ground overhead stays solid instead of fading into the haze.")]
        [Range(0.05f, 1f)] public float curlClearRamp = 0.25f;

        [Header("Sun (the Sun of Soledad_Main, while the act is shown)")]
        [Tooltip("Aim the main scene's Sun from this act. Its own rotation comes back when the act ends.")]
        public bool overrideSun = true;
        [Animate] [Tooltip("Sun height above the horizon (degrees). Low = long shadows across the dunes.")]
        [Range(-5f, 90f)] public float sunElevation = 14f;
        [Animate] [Tooltip("Sun direction in the landscape (degrees): 0 = shining towards +Z (the start of the route runs along +Z).")]
        [Range(-180f, 180f)] public float sunAzimuth = 150f;
        [Animate] [Tooltip("Sun colour temperature (K) while the act is shown: about 3000 to 4000 = sunset, 6500 = white noon light.")]
        [Range(1500f, 20000f)] public float sunTemperature = 3500f;
        [Tooltip("Sun shadow map resolution while the act is shown. Low sun = long shadows: 512 makes them blocky.")]
        public int sunShadowResolution = 4096;
        [Tooltip("Largest blur of the sun's soft shadows (m) while the act is shown. The blur grows with the distance between the object and the ground, so cloud shadows soften while the dunes stay crisp. The main Sun caps it at 0.56 m.")]
        public float sunMaxPenumbra = 40f;

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

        [Header("References")]
        [Tooltip("The fixed landscape: tiles, path and placed objects live under it.")]
        [OSCQuery.DoNotExpose] public Transform world;
        [Tooltip("Flight path, a spline under World, ridden by the Spline camera. Keep it near the route centre line: the pass and the valley follow that line.")]
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

        // Curl frame, following the camera: ground point under it and its flight direction (world xz), smoothed
        float2 _curlDirection = new float2(0f, 1f);
        float _curlGround;
        Vector3 _lastCameraPosition;
        bool _curlReady;

        Oxipital.CameraController _cameraController;
        Oxipital.SplineMovement _splineCamera;
        bool _flightStarted;

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

        /// <summary>
        /// Back to the start of the flight path: the Spline camera restarts its ride (at its startDistance), and the
        /// Spaceship camera is put at the same place, at the Spline camera's altitude, looking along the path.
        /// </summary>
        [Animate]
        public void ResetFlight()
        {
            _ridePath = null;
            _rideGround = true;
            if (!BindCameras()) return;
            _splineCamera.ResetRide();
            var spaceship = _cameraController.spaceshipCamera;
            if (spaceship != null && _splineCamera.EvaluatePose(_splineCamera.startDistance, out Vector3 position, out Quaternion rotation))
            {
                position.y = GetGroundHeight(position) + _splineCamera.altitude;
                Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
                spaceship.SetCameraTransform(position, Quaternion.LookRotation(forward.sqrMagnitude > 1e-6f ? forward : Vector3.forward));
            }
            _curlReady = false;
        }

        /// <summary>World height of this landscape under a world position (Oxipital.ICameraGround, for the Spline camera).</summary>
        public float GetGroundHeight(Vector3 worldPosition)
        {
            Transform space = world != null ? world : transform;
            Vector3 p = space.InverseTransformPoint(worldPosition);
            float h = TerrainField.Height(new float2(p.x, p.z), shape);
            return space.TransformPoint(new Vector3(p.x, h, p.z)).y;
        }

        // The spline the camera rides: the flight path, or another one handed by the act (BogotaSequence)
        UnityEngine.Splines.SplineContainer _ridePath;
        bool _rideGround = true;
        bool _terrainVisible = true;

        UnityEngine.Splines.SplineContainer CurrentPath => _ridePath != null ? _ridePath : flightPath;

        /// <summary>Hand the Spline camera another spline of the act (null = back to the flight path). It joins it at
        /// the point nearest to the camera, keeping its speed. followGround: ride at altitude above this landscape.</summary>
        public void RideSpline(UnityEngine.Splines.SplineContainer path, bool followGround)
        {
            _ridePath = path;
            _rideGround = followGround;
            BindCameras();
        }

        /// <summary>Show or hide the terrain tiles (the curl, tiles and sun go on).</summary>
        public void SetTerrainVisible(bool visible)
        {
            _terrainVisible = visible;
            if (_root != null && _root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        }

        // Play mode: hand the path and the ground to the Spline camera of Soledad_Main. False without one.
        bool BindCameras()
        {
            if (!Application.isPlaying || !driveSplineCamera) return false;
            if (_cameraController == null) _cameraController = FindFirstObjectByType<Oxipital.CameraController>(FindObjectsInactive.Include);
            var path = CurrentPath;
            if (_cameraController == null || _cameraController.splineCamera == null || path == null) return false;
            if (_splineCamera != _cameraController.splineCamera || _splineCamera.spline != path)
            {
                _splineCamera = _cameraController.splineCamera;
                _splineCamera.SetSpline(path, false);
                _splineCamera.SetGround(_rideGround ? this : null);
            }
            return true;
        }

        void UnbindCameras()
        {
            if (_splineCamera != null && (_splineCamera.spline == flightPath || _splineCamera.spline == _ridePath))
            {
                _splineCamera.SetSpline(null, false);
                _splineCamera.SetGround(null);
            }
            _splineCamera = null;
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
            _dirty = true;
            _curlReady = false;
            ApplyFarClip();
            // The first time the act is shown, the flight starts at the beginning of the path
            if (BindCameras() && !_flightStarted)
            {
                _flightStarted = true;
                ResetFlight();
                if (startOnSplineCamera) _cameraController.cameraType = Oxipital.CameraController.CameraMovementType.Spline;
            }
            Tick(0f);
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnSceneSaved;
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
            // The Cinemachine brain gives the main camera its virtual camera's lens every frame (16:9): push them too
            foreach (var vcam in FindObjectsByType<Unity.Cinemachine.CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (_farVcams.Contains(vcam)) continue;
                var lens = vcam.Lens;
                _farVcams.Add(vcam);
                _farVcamOriginals.Add(lens.FarClipPlane);
                lens.FarClipPlane = Mathf.Max(lens.FarClipPlane, farClip);
                vcam.Lens = lens;
            }
        }

        readonly List<Unity.Cinemachine.CinemachineCamera> _farVcams = new List<Unity.Cinemachine.CinemachineCamera>();
        readonly List<float> _farVcamOriginals = new List<float>();

        void RestoreFarClip()
        {
            for (int i = 0; i < _farCameras.Count; i++)
                if (_farCameras[i] != null) _farCameras[i].farClipPlane = _farOriginals[i];
            _farCameras.Clear();
            _farOriginals.Clear();
            for (int i = 0; i < _farVcams.Count; i++)
            {
                if (_farVcams[i] == null) continue;
                var lens = _farVcams[i].Lens;
                lens.FarClipPlane = _farVcamOriginals[i];
                _farVcams[i].Lens = lens;
            }
            _farVcams.Clear();
            _farVcamOriginals.Clear();
        }

        // ---------------------------------------------------------------- atmosphere while curled

        VolumeProfile _sharedProfile, _runtimeProfile;
        UnityEngine.Rendering.HighDefinition.Fog _fog;
        UnityEngine.Rendering.HighDefinition.VolumetricClouds _clouds;

        // Play mode only, and only while curled: the act Volume then uses a copy of its profile with thinner fog and
        // fainter clouds. Flat, the real profile is back, so editing it in Play mode shows at once.
        void UpdateAtmosphere()
        {
            if (!Application.isPlaying || actVolume == null) return;
            float curl = math.max(curlLateral, curlForward);
            bool clearing = curl > 0f && (curlClearsFog > 0f || curlClearsClouds > 0f);
            if (!clearing)
            {
                RestoreAtmosphere();
                return;
            }
            if (_runtimeProfile == null)
            {
                _sharedProfile = actVolume.sharedProfile;
                if (_sharedProfile == null) return;
                // A deep copy: Instantiate of a profile keeps referencing the same settings (its components are
                // separate objects), and editing them would change the real profile, compounding every frame
                _runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                _runtimeProfile.name = _sharedProfile.name + " (runtime)";
                foreach (var component in _sharedProfile.components)
                    if (component != null) _runtimeProfile.components.Add(Instantiate(component));
                _runtimeProfile.isDirty = true;
                actVolume.sharedProfile = _runtimeProfile;
                _runtimeProfile.TryGet(out _fog);
                _runtimeProfile.TryGet(out _clouds);
            }
            // Cleared early in the curl (smoothly, fully at curlClearRamp): the curled ground overhead is kilometres
            // away and the haze would wash it out and sparkle on it
            float clear = math.smoothstep(0f, math.max(curlClearRamp, 0.01f), curl);
            // Base values from the real profile every frame, so its edits still count while curled
            if (_fog != null && _sharedProfile.TryGet(out UnityEngine.Rendering.HighDefinition.Fog sharedFog))
            {
                _fog.meanFreePath.overrideState = true;
                _fog.meanFreePath.value = sharedFog.meanFreePath.value * math.pow(200f, clear * curlClearsFog);
            }
            if (_clouds != null && _sharedProfile.TryGet(out UnityEngine.Rendering.HighDefinition.VolumetricClouds sharedClouds))
            {
                _clouds.densityMultiplier.overrideState = true;
                _clouds.densityMultiplier.value = sharedClouds.densityMultiplier.value * (1f - clear * curlClearsClouds);
            }
        }

        void RestoreAtmosphere()
        {
            if (_runtimeProfile == null) return;
            if (actVolume != null && actVolume.sharedProfile == _runtimeProfile) actVolume.sharedProfile = _sharedProfile;
            foreach (var component in _runtimeProfile.components) DestroyUnityObject(component);
            DestroyUnityObject(_runtimeProfile);
            _runtimeProfile = null;
            _fog = null;
            _clouds = null;
        }

        // ---------------------------------------------------------------- sun

        Light _sun;
        // The Sun's own values, serialized so a script reload in the Editor keeps them (otherwise the act would take
        // its own values for the originals after a recompile and leave them on the Sun)
        [SerializeField, HideInInspector] Quaternion _sunOriginal;
        [SerializeField, HideInInspector] bool _sunOverridden;
        UnityEngine.Rendering.HighDefinition.HDAdditionalLightData _sunData;
        [SerializeField, HideInInspector] int _sunResolutionOriginal;
        [SerializeField, HideInInspector] float _sunPenumbraOriginal;
        [SerializeField, HideInInspector] bool _sunResolutionUseOverride;
        [SerializeField, HideInInspector] float _sunTemperatureOriginal;
        [SerializeField, HideInInspector] bool _sunUseTemperatureOriginal;
        [SerializeField, HideInInspector] Color _sunColorOriginal;

        void ApplySun()
        {
            if (!overrideSun)
            {
                RestoreSun();
                return;
            }
            if (_sun == null) _sun = FindSun();
            if (_sun == null) return;
            if (_sunData == null) _sunData = _sun.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
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
                    _sunPenumbraOriginal = _sunData.dirLightPCSSMaxPenumbraSize;
                    _sunData.dirLightPCSSMaxPenumbraSize = sunMaxPenumbra;
                }
                _sunTemperatureOriginal = _sun.colorTemperature;
                _sunUseTemperatureOriginal = _sun.useColorTemperature;
                _sunColorOriginal = _sun.color;
                _sunOverridden = true;
            }
            var rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
            if (_sun.transform.rotation != rotation) _sun.transform.rotation = rotation;
            // Warm light for the low sun: a colour temperature on a white filter (the Physically Based Sky reddens it more near the horizon)
            if (!_sun.useColorTemperature) _sun.useColorTemperature = true;
            if (_sun.color != Color.white) _sun.color = Color.white;
            if (_sun.colorTemperature != sunTemperature) _sun.colorTemperature = sunTemperature;
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
                    _sunData.dirLightPCSSMaxPenumbraSize = _sunPenumbraOriginal;
                }
                _sun.useColorTemperature = _sunUseTemperatureOriginal;
                _sun.colorTemperature = _sunTemperatureOriginal;
                _sun.color = _sunColorOriginal;
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
        void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            RestoreSun();
        }

        void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
        {
            ApplySun();
        }
#endif

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
#endif
            UnbindCameras();
            RestoreSun();
            RestoreFarClip();
            RestoreAtmosphere();
            ClearTiles(true);
            if (_root != null) DestroyUnityObject(_root.gameObject);
            _root = null;
        }

        void Update()
        {
            Tick(Application.isPlaying ? Time.deltaTime : 0f);
        }

        [ContextMenu("Rebuild Terrain")]
        void Rebuild()
        {
            _dirty = true;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

#if UNITY_EDITOR
        // Outside Play mode Update only runs when the Editor decides to: ask for one after every Inspector change, so
        // a landscape or tile setting rebuilds the terrain at once
        void OnValidate()
        {
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }
#endif

        void Tick(float dt)
        {
            if (terrainMaterial == null) return;
            EnsureRoot();
            if (_root.gameObject.activeSelf != _terrainVisible) _root.gameObject.SetActive(_terrainVisible);
            uint hash = BuildHash();
            if (_dirty || hash != _builtHash)
            {
                ClearTiles(false);
                _dirty = false;
                _builtHash = hash;
            }

            BindCameras();
            Transform target = followTarget != null ? followTarget : FindFollowTarget();
            Vector3 viewer = target != null ? target.position : transform.position;
            UpdateCurlFrame(viewer, dt);
            UpdateTiles(viewer);
            UpdateMaterial();
            UpdateAtmosphere();
            ApplySun();
        }

        // ---------------------------------------------------------------- curl frame

        // The world curls around the ground point under the camera, along its flight direction (the direction it
        // moves in, smoothed; kept while it hovers), whichever Oxipital camera flies
        void UpdateCurlFrame(Vector3 viewer, float dt)
        {
            float ground = GetGroundHeight(viewer);
            if (!_curlReady)
            {
                _curlReady = true;
                _curlGround = ground;
                _lastCameraPosition = viewer;
                Transform target = followTarget != null ? followTarget : _foundTarget;
                if (target != null) _curlDirection = math.normalizesafe(new float2(target.forward.x, target.forward.z), _curlDirection);
                return;
            }
            if (dt <= 0f) return;
            float k = 1f - math.exp(-dt / math.max(curlHeadingSmoothing, 1e-3f));
            _curlGround = math.lerp(_curlGround, ground, k);
            Vector3 moved = viewer - _lastCameraPosition;
            _lastCameraPosition = viewer;
            float2 step = new float2(moved.x, moved.z);
            float speed = math.length(step) / dt;
            // Hovering keeps the direction; a jump (reset, end of a looped path) is not a flight direction
            if (speed > 0.5f && speed < 2000f)
                _curlDirection = math.normalizesafe(math.lerp(_curlDirection, step / (speed * dt), k), _curlDirection);
        }

        // ---------------------------------------------------------------- tiles

        void UpdateTiles(Vector3 viewer)
        {
            Vector3 cameraTerrain = _root.InverseTransformPoint(viewer);
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
            // Curl around the ground point under the camera, along its flight direction
            Vector3 origin = new Vector3(_lastCameraPosition.x, _curlGround, _lastCameraPosition.z);
            float lateral = curlLateral > 0f ? curlLateral / math.max(curlRadius, 1f) : 0f;
            float forward = curlForward > 0f ? curlForward / math.max(bendRadius, 1f) : 0f;

            _block.SetVector(CurlOriginId, origin);
            _block.SetFloat(CurlLateralId, lateral);
            _block.SetFloat(CurlForwardId, forward);
            float2 frame = curlFrame == CurlFrame.Axis
                ? new float2(math.sin(math.radians(curlAxis)), math.cos(math.radians(curlAxis))) : _curlDirection;
            _block.SetVector(CurlFrameId, new Vector4(frame.x, frame.y, 0f, 0f));

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
            public float2 uv2;     // terrain normal xz, before the curl (triplanar rock)

            public static readonly VertexAttributeDescriptor[] Layout =
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2),
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
                            uv2 = normal.xz,
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

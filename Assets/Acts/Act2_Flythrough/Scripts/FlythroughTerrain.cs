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
    /// Act 2 (Flythrough): a bird's flight over an endless desert, with mountain ranges drawn as splines
    /// (<see cref="MountainRange"/> objects under <see cref="world"/>) and the dunes and relief of TerrainShape. The landscape is fixed in the space of <see cref="world"/>, and the Oxipital camera
    /// (Soledad_Main) really flies through it: the Spline camera rides <see cref="flightPath"/> (handed to it in Play
    /// mode; one spline for the whole flight, desert then Bogota, its heights baked in by BogotaSequence, so no ground
    /// following) for the film, the Spaceship camera flies freely for the show. <see cref="ResetFlight"/> puts both at
    /// the start of the path. Outside Play mode, while a Timeline drives the Spline camera by progress, the camera is
    /// previewed on the path (<see cref="previewInEditor"/>).
    ///
    /// The terrain is a quadtree of tiles around the camera named "Main Camera" (dense near it, coarse towards the
    /// horizon). Tiles are built in terrain space by a Burst job (TerrainField), with skirts hiding the seams between
    /// levels, and drawn with Soledad/FlythroughTerrain, which also rolls the world up (<see cref="curlLateral"/>,
    /// <see cref="curlForward"/>). Tiles and meshes are never saved in the scene. In Play mode, while the ground is flat,
    /// no tile is built behind the camera (<see cref="cullBehind"/>, the dome sees 180 degrees ahead).
    ///
    /// Time is accumulated from Time.deltaTime, so a Recorder capture at a fixed frame rate is deterministic.
    /// </summary>
    [ExecuteAlways]
    public class FlythroughTerrain : MonoBehaviour, Oxipital.ICameraGround, ISunDriver
    {
        [Header("Flight (the Oxipital cameras)")]
        [Tooltip("Play mode: hand the flight path to the Spline camera while the act is shown (its heights are baked: no ground following). " +
                 "Speed, look and drift are set on Cameras/Spline Camera target.")]
        public bool driveSplineCamera = true;
        [Tooltip("Movie mode: when the act is first shown (Play mode), switch the Oxipital camera to Spline and start the ride at the beginning of the path.")]
        public bool startOnSplineCamera = true;
        [Tooltip("Outside Play mode, while a Timeline drives the Spline camera by progress (Timeline preview, useProgress on), " +
                 "put it on the flight path and show it in the Game view (Cinemachine solo), so the Timeline can be scrubbed without Play. " +
                 "Offset, look (Spline And Look), spline up and pitch are previewed; drift, bank, smoothing and the spline's roll are not.")]
        public bool previewInEditor = true;

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
        [Tooltip("Play mode, ground flat: no tile behind the camera (a plane through it, square to its horizontal view direction). The dome sees 180 degrees around its view direction. Off while curled: the curl brings ground from behind and far away back into view (overhead).")]
        public bool cullBehind = true;
        [Tooltip("Tiles up to this far behind the camera (m) are kept: their shadows, a turn of the head.")]
        public float behindMargin = 1500f;
        [Tooltip("Cell of the grid the mountain ranges are rasterised on (m). Smaller = crest lines followed more closely, slower to rebuild.")]
        [Min(25f)] public float rangeCellSize = 150f;
        [Tooltip("Far clip plane given to every camera while the act is shown, in Play mode (m). The Oxipital cameras stop at 1 km. " +
                 "Keep it well beyond the farthest ground (about 70 km): with their 1 cm near plane, HDRP's tile classification takes " +
                 "pixels near the far plane for sky (beyond ~36 km with 40 km, ~100 km with 150 km) and drops the sun on their whole " +
                 "16 px tile: stair-stepped dark edges on the curled ground.")]
        public float farClip = 150000f;

        [Header("References")]
        [Tooltip("The fixed landscape: tiles, path and placed objects live under it.")]
        [OSCQuery.DoNotExpose] public Transform world;
        [Tooltip("Flight path, a spline under World, ridden by the Spline camera: the whole flight (drawn part, then the tail towards Bogota built by BogotaSequence), heights baked in. Keep it near the route centre line: the pass and the valley follow that line.")]
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
        /// Spaceship camera is put at the same place, looking along the path.
        /// </summary>
        [Animate]
        public void ResetFlight()
        {
            if (!BindCameras()) return;
            _splineCamera.ResetRide();
            var spaceship = _cameraController.spaceshipCamera;
            if (spaceship != null && _splineCamera.EvaluatePose(_splineCamera.startDistance, out Vector3 position, out Quaternion rotation))
            {
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
            float h = TerrainHeight(new float2(p.x, p.z));
            return space.TransformPoint(new Vector3(p.x, h, p.z)).y;
        }

        /// <summary>Ground height (m) at a point of terrain space (the space of <see cref="world"/>), mountain ranges included.</summary>
        public float TerrainHeight(float2 terrainPoint)
        {
            if (!_rangesBuilt) UpdateRanges();          // otherwise kept up to date once a frame (Tick)
            return TerrainField.Height(terrainPoint, shape, _ranges);
        }

        bool _terrainVisible = true;

        /// <summary>Show or hide the terrain tiles (the curl, tiles and sun go on).</summary>
        public void SetTerrainVisible(bool visible)
        {
            _terrainVisible = visible;
            if (_root != null && _root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        }

        // Play mode: hand the flight path to the Spline camera of Soledad_Main, without ground (its heights are baked).
        // False without one.
        bool BindCameras()
        {
            if (!Application.isPlaying || !driveSplineCamera) return false;
            if (_cameraController == null) _cameraController = FindFirstObjectByType<Oxipital.CameraController>(FindObjectsInactive.Include);
            if (_cameraController == null || _cameraController.splineCamera == null || flightPath == null) return false;
            if (_splineCamera != _cameraController.splineCamera || _splineCamera.spline != flightPath)
            {
                _splineCamera = _cameraController.splineCamera;
                _splineCamera.SetSpline(flightPath, false);
                _splineCamera.SetGround(null);
            }
            return true;
        }

        void UnbindCameras()
        {
            if (_splineCamera != null && _splineCamera.spline == flightPath)
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
            return math.hash(new float4(s.seed, s.duneHeight, s.duneWavelength, s.duneDirection))
                ^ math.hash(new float4(s.desertRelief, s.mountainScale, s.mountainRoughness, 0f)) * 7u
                ^ math.hash(new float4(leafSize, levels, cellsPerTile, 0f)) * 17u
                ^ _rangesHash * 31u;
        }

        // ---------------------------------------------------------------- mountain ranges

        readonly List<MountainRange> _rangeList = new List<MountainRange>();
        RangeField _ranges;
        uint _rangesHash;
        bool _rangesBuilt;

        // The MountainRange splines under World, rasterised on a grid when they change (a knot moved, a value changed)
        void UpdateRanges()
        {
            Transform space = world != null ? world : transform;
            space.GetComponentsInChildren(false, _rangeList);
            uint hash = math.hash(new float4(rangeCellSize, _rangeList.Count, 0f, 0f));
            for (int r = 0; r < _rangeList.Count; r++)
            {
                MountainRange range = _rangeList[r];
                var container = range.Container;
                hash = hash * 9176u + math.hash(new float4(range.height, range.width, container.Splines.Count, r))
                                    + math.hash(new float2(range.breakup, range.taper)) * 5u;
                Matrix4x4 m = space.worldToLocalMatrix * container.transform.localToWorldMatrix;
                hash = hash * 9176u + math.hash(new float4(m.m00, m.m02, m.m03, m.m13)) + math.hash(new float4(m.m20, m.m22, m.m23, m.m11));
                for (int i = 0; i < container.Splines.Count; i++)
                {
                    var spline = container.Splines[i];
                    hash = hash * 9176u + (uint)spline.Count + (spline.Closed ? 7u : 0u);
                    for (int k = 0; k < spline.Count; k++)
                    {
                        var knot = spline[k];
                        hash = hash * 9176u + math.hash(new float4(knot.Position, k))
                                            + math.hash(new float4(knot.TangentIn, 0f)) * 5u
                                            + math.hash(new float4(knot.TangentOut, 0f)) * 7u
                                            + math.hash(knot.Rotation.value) * 3u;
                    }
                }
            }
            if (_rangesBuilt && hash == _rangesHash) return;
            _rangesHash = hash;
            _rangesBuilt = true;
            BuildRanges(space);
        }

        void BuildRanges(Transform space)
        {
            DisposeRanges();
            float cell = math.max(rangeCellSize, 25f);
            var segments = new List<float4>();
            var parameters = new List<float4>();
            var shapes = new List<float2>();
            float2 min = float.MaxValue, max = float.MinValue;
            foreach (var range in _rangeList)
            {
                var container = range.Container;
                for (int i = 0; i < container.Splines.Count; i++)
                {
                    var spline = container.Splines[i];
                    if (spline.Count < 2) continue;
                    float length = container.CalculateLength(i);
                    int n = math.max(2, (int)math.ceil(length / cell) + 1);
                    // Ends sink over taper x width, at most a third of the range each
                    float taper = math.min(range.taper * range.width / math.max(length, 1f), 0.33f);
                    float2 previous = 0f;
                    for (int k = 0; k < n; k++)
                    {
                        Vector3 world3 = container.EvaluatePosition(i, k / (float)(n - 1));
                        Vector3 local = space.InverseTransformPoint(world3);
                        float2 p = new float2(local.x, local.z);
                        if (k > 0)
                        {
                            segments.Add(new float4(previous, p));
                            parameters.Add(new float4(range.height, range.width, (k - 1) / (float)(n - 1), k / (float)(n - 1)));
                            shapes.Add(new float2(range.breakup, taper));
                        }
                        min = math.min(min, p - range.width);
                        max = math.max(max, p + range.width);
                        previous = p;
                    }
                }
            }
            if (segments.Count == 0)
            {
                // No range: an empty field (a job needs a valid array; count 0 samples as nothing)
                _ranges = new RangeField { cells = new NativeArray<float2>(1, Allocator.Persistent), cellSize = 1f };
                return;
            }

            // Grid over all ranges, at most 2048 cells a side (coarser cells for a very wide layout)
            min -= cell;
            max += cell;
            cell = math.max(cell, math.cmax(max - min) / 2047f);
            int2 count = (int2)math.ceil((max - min) / cell) + 1;
            _ranges = new RangeField
            {
                cells = new NativeArray<float2>(count.x * count.y, Allocator.Persistent),
                origin = min,
                cellSize = cell,
                count = count,
            };
            var segmentArray = new NativeArray<float4>(segments.ToArray(), Allocator.TempJob);
            var parameterArray = new NativeArray<float4>(parameters.ToArray(), Allocator.TempJob);
            var shapeArray = new NativeArray<float2>(shapes.ToArray(), Allocator.TempJob);
            new RangeJob
            {
                segments = segmentArray, parameters = parameterArray, shapes = shapeArray, cells = _ranges.cells,
                origin = min, cellSize = cell, countX = count.x, seed = shape.seed * 1013f,
            }.Schedule(count.x * count.y, 64).Complete();
            segmentArray.Dispose();
            parameterArray.Dispose();
            shapeArray.Dispose();
        }

        void DisposeRanges()
        {
            if (_ranges.cells.IsCreated) _ranges.cells.Dispose();
            _ranges = default;
        }

        // Per grid cell: the highest weight and envelope of all range segments. Across a range: smoothstep from the
        // crest line (1) to width (0), measured from a warped point so the edges wander. Along it: the ends taper, the
        // height wanders (0.35 to 1 of the range's), and breakup cuts it into separate massifs.
        [BurstCompile]
        struct RangeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> segments;      // a.xy, b.xy (terrain space)
            [ReadOnly] public NativeArray<float4> parameters;    // height, width, position along the range at a and b (0..1)
            [ReadOnly] public NativeArray<float2> shapes;        // breakup, end taper (fraction of the range)
            public NativeArray<float2> cells;
            public float2 origin;
            public float cellSize;
            public int countX;
            public float seed;

            public void Execute(int index)
            {
                float2 p = origin + new float2(index % countX, index / countX) * cellSize;
                float2 q = p + seed;
                float2 warped = p + 1400f * new float2(TerrainField.Fbm(q / 7000f, 3), TerrainField.Fbm(q / 7000f + 19.3f, 3));
                float along = 0.65f + 0.5f * TerrainField.Fbm(q / 9000f + 5.1f, 3);
                float weight = 0f, envelope = 0f;
                for (int s = 0; s < segments.Length; s++)
                {
                    float4 seg = segments[s];
                    float4 hw = parameters[s];
                    float2 a = seg.xy, ab = seg.zw - seg.xy;
                    float t = math.saturate(math.dot(warped - a, ab) / math.max(math.dot(ab, ab), 1e-6f));
                    float d = math.length(warped - (a + ab * t));
                    float w = math.saturate(1f - d / hw.y);
                    if (w <= 0f) continue;
                    w = w * w * (3f - 2f * w);
                    float2 shape = shapes[s];
                    float u = math.lerp(hw.z, hw.w, t);
                    float taper = shape.y > 0f ? math.smoothstep(0f, shape.y, u) * math.smoothstep(1f, 1f - shape.y, u) : 1f;
                    float massif = 1f;
                    if (shape.x > 0f)
                    {
                        float n = TerrainField.Fbm(q / (hw.y * 1.5f) + 41.7f, 3);
                        massif = math.saturate((n + 0.6f - 1.4f * shape.x) * 3f);
                    }
                    float k = taper * massif;
                    weight = math.max(weight, w * k);
                    envelope = math.max(envelope, w * k * hw.x * math.saturate(along));
                }
                cells[index] = new float2(weight, envelope);
            }
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
            SunControl.Register(this);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorPreview;
            if (!Application.isPlaying) UnityEditor.EditorApplication.update += EditorPreview;
#endif
            Tick(0f);
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

        // The Sun's own values are kept by SunControl (several acts may be enabled at once), which gives them back
        // when the act ends and around scene saves, so Soledad_Main never keeps the act's sun
        public bool DrivesSun => overrideSun;

        public void DriveSun(Light sun, UnityEngine.Rendering.HighDefinition.HDAdditionalLightData data)
        {
            var rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
            if (sun.transform.rotation != rotation) sun.transform.rotation = rotation;
            // Warm light for the low sun: a colour temperature on a white filter (the Physically Based Sky reddens it more near the horizon)
            if (!sun.useColorTemperature) sun.useColorTemperature = true;
            if (sun.color != Color.white) sun.color = Color.white;
            if (sun.colorTemperature != sunTemperature) sun.colorTemperature = sunTemperature;
            if (data == null) return;
            if (!data.shadowResolution.useOverride) data.shadowResolution.useOverride = true;
            if (data.shadowResolution.@override != sunShadowResolution) data.shadowResolution.@override = sunShadowResolution;
            if (data.dirLightPCSSMaxPenumbraSize != sunMaxPenumbra) data.dirLightPCSSMaxPenumbraSize = sunMaxPenumbra;
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorPreview;
            StopPreview();
#endif
            UnbindCameras();
            SunControl.Unregister(this);
            RestoreFarClip();
            RestoreAtmosphere();
            ClearTiles(true);
            if (_root != null) DestroyUnityObject(_root.gameObject);
            _root = null;
            DisposeRanges();
            _rangesBuilt = false;
        }

        void Update()
        {
            Tick(Application.isPlaying ? Time.deltaTime : 0f);
        }

        /// <summary>Rebuilds the terrain, mountain ranges included (they also rebuild by themselves when a knot moves).</summary>
        [ContextMenu("Rebuild Terrain")]
        public void Rebuild()
        {
            _dirty = true;
            _rangesBuilt = false;
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
            UpdateRanges();
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
            SunControl.Drive(this);
        }

#if UNITY_EDITOR
        // ---------------------------------------------------------------- editor preview of the Spline camera

        // The Spline camera's own Update runs in Play mode only, and the path is handed to it in Play mode: outside
        // Play, while a Timeline drives it by progress, its pose is computed here and its Cinemachine camera is shown
        // solo, so scrubbing the Timeline flies the Game view along the path. Its target's own pose comes back after.
        Oxipital.CameraController _previewController;
        Oxipital.SplineMovement _previewCamera;
        Unity.Cinemachine.CinemachineCamera _previewVcam;
        double _nextPreviewSearch;
        bool _previewing;
        Vector3 _savedTargetPosition, _lastPreviewPosition;
        Quaternion _savedTargetRotation, _lastPreviewRotation;
        float _savedVcamFar;

        // The solo camera's lens far plane, which the Cinemachine brain gives the main camera (1 km on the Oxipital
        // cameras): farClip while previewing, as in Play mode (ApplyFarClip), its own value back after
        void SetPreviewFar(float far)
        {
            if (_previewVcam == null) return;
            var lens = _previewVcam.Lens;
            if (lens.FarClipPlane == far) return;
            lens.FarClipPlane = far;
            _previewVcam.Lens = lens;
        }

        void EditorPreview()
        {
            if (this == null || Application.isPlaying) return;
            if (_previewController == null && UnityEditor.EditorApplication.timeSinceStartup >= _nextPreviewSearch)
            {
                _nextPreviewSearch = UnityEditor.EditorApplication.timeSinceStartup + 1.0;
                _previewController = FindFirstObjectByType<Oxipital.CameraController>(FindObjectsInactive.Include);
            }
            var cam = _previewController != null ? _previewController.splineCamera : null;
            bool want = previewInEditor && isActiveAndEnabled && cam != null && cam.useProgress && HasFlightPath();
            if (!want)
            {
                StopPreview();
                return;
            }
            if (!PreviewPose(cam, out Vector3 position, out Quaternion rotation, out Vector3 flat)) return;
            if (!_previewing || _previewCamera != cam)
            {
                StopPreview();
                _previewCamera = cam;
                _savedTargetPosition = cam.transform.position;
                _savedTargetRotation = cam.transform.rotation;
                _previewVcam = null;
                foreach (var vcam in FindObjectsByType<Unity.Cinemachine.CinemachineCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (vcam.Follow == cam.transform) { _previewVcam = vcam; break; }
                if (_previewVcam != null)
                {
                    Unity.Cinemachine.CinemachineCore.SoloCamera = _previewVcam;
                    _savedVcamFar = _previewVcam.Lens.FarClipPlane;
                }
                _previewing = true;
                _lastPreviewPosition = new Vector3(float.NaN, 0f, 0f);
            }
            SetPreviewFar(farClip);
            if (position == _lastPreviewPosition && rotation == _lastPreviewRotation) return;
            _lastPreviewPosition = position;
            _lastPreviewRotation = rotation;
            cam.transform.SetPositionAndRotation(position, rotation);
            // The curl follows the previewed camera at once (outside Play its frame does not move on its own)
            _curlReady = true;
            _curlGround = GetGroundHeight(position);
            _curlDirection = math.normalizesafe(new float2(flat.x, flat.z), _curlDirection);
            _lastCameraPosition = position;
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
        }

        void StopPreview()
        {
            if (!_previewing) return;
            _previewing = false;
            if (_previewVcam != null && ReferenceEquals(Unity.Cinemachine.CinemachineCore.SoloCamera, _previewVcam))
                Unity.Cinemachine.CinemachineCore.SoloCamera = null;
            SetPreviewFar(_savedVcamFar);
            if (_previewCamera != null) _previewCamera.transform.SetPositionAndRotation(_savedTargetPosition, _savedTargetRotation);
            _previewCamera = null;
            _previewVcam = null;
            _curlReady = false;
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }

        bool HasFlightPath() => flightPath != null && flightPath.Splines.Count > 0 && flightPath.Spline.Count >= 2;

        // The Spline camera's pose at its progress on the flight path, as SplineMovement computes it without time:
        // offset, aim ahead, spline up, pitch and the look of Spline And Look (stick x the max angles, no spring)
        bool PreviewPose(Oxipital.SplineMovement cam, out Vector3 position, out Quaternion rotation, out Vector3 flat)
        {
            position = default;
            rotation = Quaternion.identity;
            flat = Vector3.forward;
            var s = flightPath.Spline;
            float length = s.GetLength();
            if (length <= 0f) return false;
            float distance = Mathf.Clamp01(cam.progress) * length;
            SamplePath(s, distance, length, cam.loop, out Vector3 onSpline, out Quaternion splineRotation);
            Vector3 forward = splineRotation * Vector3.forward;
            if (cam.aimAhead > 0.01f)
            {
                SamplePath(s, distance + cam.aimAhead, length, cam.loop, out Vector3 aim, out _);
                if ((aim - onSpline).sqrMagnitude > 1e-4f) forward = aim - onSpline;
            }
            forward.Normalize();
            Vector3 up = cam.splineUp <= 0f ? Vector3.up : Vector3.Slerp(Vector3.up, splineRotation * Vector3.up, cam.splineUp).normalized;
            flat = Vector3.ProjectOnPlane(forward, up);
            if (flat.sqrMagnitude < 1e-8f) flat = Vector3.Cross(Vector3.right, up);
            flat.Normalize();
            Vector3 right = Vector3.Cross(up, flat);
            position = onSpline + right * cam.offset.x + up * cam.offset.y;
            float climb = Vector3.SignedAngle(flat, forward, right);
            rotation = Quaternion.LookRotation(flat, up) * Quaternion.AngleAxis(climb * cam.followPitch, Vector3.right);
            if (cam.rotationMode == Oxipital.SplineMovement.RotationMode.SplineAndLook)
            {
                float yaw = PreviewStick(cam.yaw1D, cam.stickDeadzone) * cam.maxLookYaw;
                float pitch = PreviewStick(cam.pitch1D, cam.stickDeadzone) * cam.maxLookPitch;
                float roll = PreviewStick(cam.roll1D, cam.stickDeadzone) * cam.maxLookRoll;
                rotation = rotation * Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, Vector3.right)
                                    * Quaternion.AngleAxis(-roll, Vector3.forward);
            }
            return true;
        }

        static float PreviewStick(float value, float deadzone)
        {
            float a = Mathf.Abs(Mathf.Clamp(value, -1f, 1f));
            return a <= deadzone ? 0f : Mathf.Sign(value) * (a - deadzone) / Mathf.Max(1f - deadzone, 1e-3f);
        }

        // Same sampling as SplineMovement: the container's position and rotation, not its scale
        void SamplePath(UnityEngine.Splines.Spline s, float d, float length, bool loop, out Vector3 position, out Quaternion rotation)
        {
            d = loop ? Mathf.Repeat(d, length) : Mathf.Clamp(d, 0f, length);
            float t = UnityEngine.Splines.SplineUtility.ConvertIndexUnit(s, d, UnityEngine.Splines.PathIndexUnit.Distance, UnityEngine.Splines.PathIndexUnit.Normalized);
            UnityEngine.Splines.SplineUtility.Evaluate(s, t, out float3 p, out float3 tangent, out float3 upVector);
            Vector3 fwd = tangent, upv = upVector;
            if (Vector3.Cross(fwd, upv).sqrMagnitude < 1e-10f)
            {
                fwd = Vector3.forward;
                upv = Vector3.up;
            }
            Transform c = flightPath.transform;
            position = c.position + c.rotation * (Vector3)p;
            rotation = c.rotation * Quaternion.LookRotation(fwd, upv);
        }
#endif

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
            float heightAbove = math.max(1f, cameraTerrain.y - TerrainField.Height(c, shape, _ranges));

            // Play mode, flat ground: what is behind the camera's horizontal view direction is never seen (16:9 or
            // dome). Curled, the ground rolls up overhead and comes back into view: every tile is kept
            _cullForward = 0f;
            if (cullBehind && Application.isPlaying && curlLateral <= 0f && curlForward <= 0f)
            {
                Transform target = followTarget != null ? followTarget : _foundTarget;
                if (target != null)
                {
                    Vector3 f = _root.InverseTransformDirection(target.forward);
                    float2 flat = new float2(f.x, f.z);
                    if (math.lengthsq(flat) > 0.01f) _cullForward = math.normalize(flat);
                }
            }

            _wanted.Clear();
            float rootSize = leafSize * (1 << levels);
            // 4 x 4 root tiles around the root corner nearest to the camera, so the covered square changes only when the
            // camera crosses the middle of a root tile, and its edge is always 1.5 to 2.5 root tiles away. A 3 x 3 grid
            // around the camera's own root tile switched a whole 32 km column at every tile edge, x = 0 among them,
            // right under the flight: curled, that column rolls up overhead and popped in or out in one frame
            long rx = (long)math.round(c.x / rootSize), rz = (long)math.round(c.y / rootSize);
            for (long ix = rx - 2; ix <= rx + 1; ix++)
                for (long iz = rz - 2; iz <= rz + 1; iz++)
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
            if (math.any(_cullForward != 0f))
            {
                // Farthest corner ahead: the tile is behind if even that one is
                float2 rel = min - camera;
                float ahead = math.dot(rel, _cullForward) + size * (math.max(_cullForward.x, 0f) + math.max(_cullForward.y, 0f));
                if (ahead < -behindMargin) return;
            }
            float distance = math.sqrt(math.lengthsq(d) + heightAbove * heightAbove);
            if (level > 0 && distance < size * detailDistance)
            {
                for (int k = 0; k < 4; k++)
                    Collect(level - 1, ix * 2 + (k & 1), iz * 2 + (k >> 1), camera, heightAbove);
                return;
            }
            _wanted.Add(Key(level, ix, iz));
        }

        float2 _cullForward;

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

            new TileJob { requests = requests, meshes = meshData, ranges = ranges, cells = cells, shape = shape, mountains = _ranges }
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
            public RangeField mountains;

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
                        float h = TerrainField.Height(p, shape, mountains, out float mountain, out float duneLevel);
                        heights[(i + 1) + (j + 1) * border] = h;
                        if (inside)
                        {
                            biomes[i + j * grid] = new float4(1f - mountain, mountain, 0f, duneLevel);
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
                            color = biomes[i + j * grid],                 // desert, mountain weights, a = dune level
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

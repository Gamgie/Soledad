using UnityEngine;
using UnityEngine.Rendering;

namespace Soledad
{
    /// <summary>
    /// Act 1 (Beyond The Veil): an endless box tunnel made of a thin grid, whose cells are "windows" that can light
    /// up. Random mode blinks cells all over the tunnel; the solo window is one cell, always the same, blinking at
    /// its own rate (the one lit window of a building at night). The tunnel can be deformed by a fractal noise,
    /// like a Noise SOP in TouchDesigner.
    ///
    /// The mesh is generated here (dense, so the noise bends the lines) and drawn with the Soledad/GridTunnel
    /// shader. It is put on the child <see cref="meshRenderer"/>, which this script slides along the tunnel axis
    /// (local +Z) in whole cells to stay around the camera: the grid, the blinking and the noise are computed in
    /// tunnel space, so the slide is invisible and the tunnel never ends. <see cref="travelSpeed"/> scrolls the
    /// grid and the noise as if flying forward without moving the camera.
    ///
    /// Time is accumulated from Time.deltaTime, so a Recorder capture at a fixed frame rate is deterministic.
    /// With <see cref="animateInEditMode"/> it also runs in the Editor outside Play mode (Editor clock).
    /// Every public field is exposed to Chataigne over OSCQuery.
    /// </summary>
    [ExecuteAlways]
    public class GridTunnel : MonoBehaviour
    {
        /// <summary>In which order the lines of a family appear (stagger) and light up (effect delay).</summary>
        public enum LineOrder { Chase, Reverse, Random, Mirror }

        /// <summary>Light effect running on each line, like the effects of Blux (Ben Kuper).</summary>
        public enum LineEffect { None, Sine, Noise, Pulse, Saw }

        [Header("Shape (rebuilds the mesh)")]
        [Tooltip("Size of a cell (m).")]
        public float cellSize = 1f;
        [Tooltip("Cells across the floor and the ceiling.")]
        public int widthCells = 12;
        [Tooltip("Cells across the side walls.")]
        public int heightCells = 12;
        [Tooltip("Length of the mesh, in cells. The tunnel itself is endless when it follows the camera.")]
        public int lengthCells = 240;
        [Tooltip("Cells of the mesh kept behind the camera.")]
        public int cellsBehind = 12;
        [Tooltip("Vertices per cell side. More = smoother noise deformation of the lines.")]
        public int subdivisions = 3;

        [Header("Infinite tunnel")]
        [Tooltip("Slide the mesh along the tunnel to stay around the camera.")]
        public bool followCamera = true;
        [Tooltip("Scrolls the grid and the noise towards the camera (m/s), as if flying forward along +Z.")]
        public float travelSpeed = 0f;

        [Header("Look")]
        [Tooltip("Global brightness of the lines and windows (also multiplied by the weight of the parent Act).")]
        [Range(0f, 1f)] public float masterIntensity = 1f;
        public Color lineColor = Color.white;
        public float lineIntensity = 1f;
        [Tooltip("Width of the grid lines, in cells.")]
        [Range(0f, 0.5f)] public float lineWidth = 0.02f;
        public Color windowColor = new Color(1f, 0.85f, 0.6f);
        public float windowIntensity = 4f;
        public Color backgroundColor = Color.black;
        [Tooltip("Distance (m) where lines and windows start fading into the background.")]
        public float fadeStart = 60f;
        [Tooltip("Distance (m) where they are gone. Kept short of the end of the mesh.")]
        public float fadeEnd = 180f;

        [Header("Rails: lines along the tunnel, drawn from the camera towards the end")]
        [Tooltip("0 = no rail, 1 = every rail fully drawn. Meant to be animated (Timeline, Chataigne).")]
        [Range(0f, 1f)] public float railsReveal = 1f;
        [Tooltip("0 = all rails drawn together, 1 = one after another, in the order below.")]
        [Range(0f, 1f)] public float railsStagger = 0.5f;
        [Tooltip("Chase: around the tunnel from the middle of the floor. Reverse, Random, Mirror: both sides of the floor first.")]
        public LineOrder railsOrder = LineOrder.Chase;
        public LineEffect railsEffect = LineEffect.None;
        [Tooltip("Effect cycles per second.")]
        public float railsEffectSpeed = 0.5f;
        [Tooltip("Effect time offset between the first and the last rail (s), in the order above. 0 = all in sync.")]
        public float railsEffectDelay = 1f;
        [Tooltip("Brightness at the low point of the effect.")]
        [Range(0f, 1f)] public float railsEffectMin = 0f;

        [Header("Rings: lines around the tunnel, drawn from the middle of the floor up both sides")]
        [Tooltip("0 = no ring, 1 = every ring fully drawn. Meant to be animated (Timeline, Chataigne).")]
        [Range(0f, 1f)] public float ringsReveal = 1f;
        [Tooltip("0 = all rings drawn together, 1 = one after another, in the order below.")]
        [Range(0f, 1f)] public float ringsStagger = 0.5f;
        [Tooltip("Chase: from the camera towards the end. Reverse: from the end. Random. Mirror: near and far first.")]
        public LineOrder ringsOrder = LineOrder.Chase;
        public LineEffect ringsEffect = LineEffect.None;
        [Tooltip("Effect cycles per second.")]
        public float ringsEffectSpeed = 0.5f;
        [Tooltip("Effect time offset between the first and the last ring (s), in the order above. 0 = all in sync.")]
        public float ringsEffectDelay = 1f;
        [Tooltip("Brightness at the low point of the effect.")]
        [Range(0f, 1f)] public float ringsEffectMin = 0f;

        [Header("Random blink")]
        [Tooltip("Probability that a cell is lit during one of its ticks. 0 = no random window.")]
        [Range(0f, 1f)] public float randomDensity = 0.02f;
        [Tooltip("Ticks per second per cell. 0 = static pattern.")]
        public float randomRate = 0.5f;
        [Tooltip("0 = every cell ticks at the same rate, 1 = rates spread from 0x to 2x.")]
        [Range(0f, 1f)] public float randomRateJitter = 0.5f;
        [Tooltip("0 = hard on/off, 1 = each blink fades in and out over its whole tick.")]
        [Range(0f, 1f)] public float randomFade = 0.3f;
        public int randomSeed = 0;

        [Header("Solo window")]
        public bool soloEnabled = false;
        [Tooltip("0 floor, 1 right, 2 ceiling, 3 left.")]
        [Range(0, 3)] public int soloWall = 3;
        [Tooltip("Column on the wall, from 0.")]
        public int soloColumn = 6;
        [Tooltip("Row along the tunnel. Relative to the camera when soloFollowCamera is on.")]
        public int soloRow = 20;
        [Tooltip("The row counts from the camera's cell, so the window stays ahead of the camera.")]
        public bool soloFollowCamera = true;
        [Tooltip("Blinks per second. 0 = steady light.")]
        public float soloBlinkRate = 0f;
        [Tooltip("Part of each blink the window is on.")]
        [Range(0f, 1f)] public float soloDuty = 0.5f;
        [Tooltip("Softness of each blink edge.")]
        [Range(0f, 1f)] public float soloFade = 0.2f;
        [Tooltip("Seconds to fade the solo window in or out when it is switched on or off.")]
        public float soloToggleTime = 1.5f;
        public Color soloColor = new Color(1f, 0.85f, 0.6f);
        public float soloIntensity = 6f;

        [Header("Blink time")]
        [Tooltip("Speed of all the blinking (random and solo).")]
        public float blinkSpeed = 1f;
        [Tooltip("Keep blinking, noise and travel running in the Editor outside Play mode (Scene and Game views).")]
        public bool animateInEditMode = true;

        [Header("Noise deformation")]
        [Tooltip("Displacement (m). 0 = straight tunnel.")]
        public float noiseAmplitude = 0f;
        [Tooltip("Size of the noise features (m).")]
        public float noisePeriod = 8f;
        [Range(1, 8)] public int noiseHarmonics = 3;
        [Tooltip("Gain of each harmonic.")]
        [Range(0f, 1f)] public float noiseRoughness = 0.5f;
        [Tooltip("Frequency step between harmonics.")]
        [Range(1f, 4f)] public float noiseSpread = 2f;
        [Tooltip("Stretches the noise along the tunnel (2 = features twice as long).")]
        public float noiseStretch = 1f;
        [Tooltip("0 = free 3D displacement, 1 = only towards / away from the tunnel axis.")]
        [Range(0f, 1f)] public float noiseRadial = 1f;
        [Tooltip("Speed of the noise evolution.")]
        public float noiseSpeed = 0.2f;
        [Tooltip("Direction the noise drifts in while it evolves.")]
        public Vector3 noiseFlow = new Vector3(0.13f, 0.07f, 0.21f);
        public float noiseSeed = 0f;

        [Header("References")]
        [OSCQuery.DoNotExpose] public MeshRenderer meshRenderer;
        [Tooltip("Followed along the tunnel. Empty = the camera named \"Main Camera\" (Oxipital Cameras prefab), else the one tagged MainCamera, found at run time.")]
        [OSCQuery.DoNotExpose] public Transform followTarget;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int LineColorId = Shader.PropertyToID("_LineColor");
        static readonly int LineWidthId = Shader.PropertyToID("_LineWidth");
        static readonly int WindowColorId = Shader.PropertyToID("_WindowColor");
        static readonly int SoloColorId = Shader.PropertyToID("_SoloColor");
        static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
        static readonly int FadeEndId = Shader.PropertyToID("_FadeEnd");
        static readonly int RandomDensityId = Shader.PropertyToID("_RandomDensity");
        static readonly int RandomRateId = Shader.PropertyToID("_RandomRate");
        static readonly int RandomRateJitterId = Shader.PropertyToID("_RandomRateJitter");
        static readonly int RandomFadeId = Shader.PropertyToID("_RandomFade");
        static readonly int RandomIntensityId = Shader.PropertyToID("_RandomIntensity");
        static readonly int RandomSeedId = Shader.PropertyToID("_RandomSeed");
        static readonly int SoloCellId = Shader.PropertyToID("_SoloCell");
        static readonly int SoloLevelId = Shader.PropertyToID("_SoloLevel");
        static readonly int BlinkTimeId = Shader.PropertyToID("_BlinkTime");
        static readonly int NoiseTimeId = Shader.PropertyToID("_NoiseTime");
        static readonly int TunnelOffsetId = Shader.PropertyToID("_TunnelOffset");
        static readonly int NoiseAmplitudeId = Shader.PropertyToID("_NoiseAmplitude");
        static readonly int NoisePeriodId = Shader.PropertyToID("_NoisePeriod");
        static readonly int NoiseOctavesId = Shader.PropertyToID("_NoiseOctaves");
        static readonly int NoiseRoughnessId = Shader.PropertyToID("_NoiseRoughness");
        static readonly int NoiseLacunarityId = Shader.PropertyToID("_NoiseLacunarity");
        static readonly int NoiseStretchId = Shader.PropertyToID("_NoiseStretch");
        static readonly int NoiseRadialId = Shader.PropertyToID("_NoiseRadial");
        static readonly int NoiseFlowId = Shader.PropertyToID("_NoiseFlow");
        static readonly int NoiseSeedId = Shader.PropertyToID("_NoiseSeed");
        static readonly int ShowTimeId = Shader.PropertyToID("_ShowTime");
        static readonly int CameraRowId = Shader.PropertyToID("_CameraRow");
        static readonly int GridInfoId = Shader.PropertyToID("_GridInfo");
        static readonly int RailsGrowId = Shader.PropertyToID("_RailsGrow");
        static readonly int RailsFxId = Shader.PropertyToID("_RailsFx");
        static readonly int RingsGrowId = Shader.PropertyToID("_RingsGrow");
        static readonly int RingsFxId = Shader.PropertyToID("_RingsFx");
        static readonly int LinesFxMinId = Shader.PropertyToID("_LinesFxMin");

        Mesh _mesh;
        MaterialPropertyBlock _block;
        Act _act;
        MeshFilter _filter;

        // Shape the current mesh was built with
        float _builtCellSize;
        int _builtWidth, _builtHeight, _builtLength, _builtBehind, _builtSubdivisions;
        float _boundsAmplitude = -1f;

        double _blinkTime, _noiseTime, _soloTime, _travel, _showTime;
        float _soloOn;
        float _nextTargetSearch;
        Transform _foundTarget;     // found at run time, kept out of the scene (the camera lives in another scene)

        /// <summary>Picks a new random pattern.</summary>
        public void Reseed() => randomSeed++;

        /// <summary>Brings the travel scroll back to 0.</summary>
        public void ResetTravel() => _travel = 0.0;

        /// <summary>Moves the solo window to a random cell of a random wall, ahead of the camera.</summary>
        public void RandomizeSolo()
        {
            soloWall = Random.Range(0, 4);
            soloColumn = Random.Range(0, CellsOnWall(soloWall));
            if (soloFollowCamera) soloRow = Random.Range(8, Mathf.Max(9, lengthCells - cellsBehind - 8));
        }

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            _act = GetComponentInParent<Act>(true);
            if (meshRenderer == null) meshRenderer = GetComponentInChildren<MeshRenderer>(true);
            if (meshRenderer == null)
            {
                Debug.LogWarning("GridTunnel: no child MeshRenderer to draw the tunnel.", this);
                return;
            }
            _filter = meshRenderer.GetComponent<MeshFilter>();
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            BuildMesh();
            Tick(0f);
#if UNITY_EDITOR
            _lastEditorTime = UnityEditor.EditorApplication.timeSinceStartup;
            UnityEditor.EditorApplication.update -= EditorTick;
            UnityEditor.EditorApplication.update += EditorTick;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            if (meshRenderer != null) meshRenderer.SetPropertyBlock(null);
        }

#if UNITY_EDITOR
        double _lastEditorTime;

        // Outside Play mode Update only runs when something changes: drive the animation from the Editor loop
        void EditorTick()
        {
            if (Application.isPlaying || !animateInEditMode || this == null || !isActiveAndEnabled) return;
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now - _lastEditorTime < 1.0 / 60.0) return;     // about 60 fps, like the show
            float dt = (float)System.Math.Min(now - _lastEditorTime, 0.1);
            _lastEditorTime = now;
            Tick(dt);
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
        }
#endif

        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }

        void Update()
        {
            // Outside Play mode, EditorTick advances the time; here only apply edits made in the Inspector
            Tick(Application.isPlaying ? Time.deltaTime : 0f);
        }

        void Tick(float dt)
        {
            if (meshRenderer == null || _filter == null) return;

            if (ShapeChanged()) BuildMesh();

            _blinkTime += dt * blinkSpeed;
            _soloTime += dt * blinkSpeed;
            _noiseTime += dt * noiseSpeed;
            _travel += dt * travelSpeed;
            _showTime += dt;

            float s = Mathf.Max(cellSize, 1e-3f);

            // Slide the mesh along the tunnel in whole cells to stay around the camera
            long snappedCells = 0;
            double cameraCells = 0.0;
            Transform target = followTarget != null ? followTarget : FindFollowTarget();
            if (target != null) cameraCells = transform.InverseTransformPoint(target.position).z / s;
            if (followCamera && target != null) snappedCells = (long)System.Math.Floor(cameraCells);
            var meshPosition = new Vector3(0f, 0f, snappedCells * s);
            if (meshRenderer.transform.localPosition != meshPosition) meshRenderer.transform.localPosition = meshPosition;

            double travelCells = _travel / s;
            double rowOffset = snappedCells + travelCells;
            double rowInt = System.Math.Floor(rowOffset);

            // Solo window
            float soloTarget = soloEnabled ? 1f : 0f;
            _soloOn = soloToggleTime > 0f ? Mathf.MoveTowards(_soloOn, soloTarget, dt / soloToggleTime) : soloTarget;
            if (!Application.isPlaying && !animateInEditMode) _soloOn = soloTarget;
            long soloRowAbs = soloFollowCamera ? (long)System.Math.Floor(cameraCells + travelCells) + soloRow : soloRow;
            int wall = Mathf.Clamp(soloWall, 0, 3);
            int column = Mathf.Clamp(soloColumn, 0, CellsOnWall(wall) - 1);

            float master = masterIntensity * (_act != null ? _act.weight : 1f);
            float meshFront = (lengthCells - cellsBehind - 1) * s;
            float end = Mathf.Min(fadeEnd, meshFront);

            _block.SetColor(BaseColorId, backgroundColor);
            _block.SetColor(LineColorId, lineColor * (lineIntensity * master));
            _block.SetFloat(LineWidthId, lineWidth);
            _block.SetColor(WindowColorId, windowColor * (windowIntensity * master));
            _block.SetColor(SoloColorId, soloColor * (soloIntensity * master));
            _block.SetFloat(FadeStartId, Mathf.Min(fadeStart, end));
            _block.SetFloat(FadeEndId, end);

            _block.SetFloat(RandomDensityId, randomDensity);
            _block.SetFloat(RandomRateId, randomRate);
            _block.SetFloat(RandomRateJitterId, randomRateJitter);
            _block.SetFloat(RandomFadeId, randomFade);
            _block.SetFloat(RandomIntensityId, 1f);
            _block.SetFloat(RandomSeedId, randomSeed);

            _block.SetVector(SoloCellId, new Vector4(wall, column, soloRowAbs, _soloOn > 0f ? 1f : 0f));
            _block.SetFloat(SoloLevelId, SoloBlinkLevel() * _soloOn);

            _block.SetFloat(BlinkTimeId, (float)_blinkTime);
            _block.SetFloat(NoiseTimeId, (float)_noiseTime);
            _block.SetVector(TunnelOffsetId, new Vector4(
                (float)(snappedCells * (double)s + _travel), (float)rowInt, (float)(rowOffset - rowInt), 0f));

            _block.SetFloat(NoiseAmplitudeId, noiseAmplitude);
            _block.SetFloat(NoisePeriodId, noisePeriod);
            _block.SetFloat(NoiseOctavesId, noiseHarmonics);
            _block.SetFloat(NoiseRoughnessId, noiseRoughness);
            _block.SetFloat(NoiseLacunarityId, noiseSpread);
            _block.SetFloat(NoiseStretchId, noiseStretch);
            _block.SetFloat(NoiseRadialId, noiseRadial);
            _block.SetVector(NoiseFlowId, noiseFlow);
            _block.SetFloat(NoiseSeedId, noiseSeed);

            _block.SetFloat(ShowTimeId, (float)_showTime);
            _block.SetFloat(CameraRowId, (float)(cameraCells + travelCells - rowInt));
            _block.SetVector(GridInfoId, new Vector4(Mathf.Max(1, widthCells), Mathf.Max(1, heightCells), s, end));
            _block.SetVector(RailsGrowId, new Vector4(Mathf.Clamp01(railsReveal), Mathf.Clamp01(railsStagger), 0f, 0f));
            _block.SetVector(RailsFxId, new Vector4((int)railsOrder, (int)railsEffect, railsEffectSpeed, railsEffectDelay));
            _block.SetVector(RingsGrowId, new Vector4(Mathf.Clamp01(ringsReveal), Mathf.Clamp01(ringsStagger), 0f, 0f));
            _block.SetVector(RingsFxId, new Vector4((int)ringsOrder, (int)ringsEffect, ringsEffectSpeed, ringsEffectDelay));
            _block.SetVector(LinesFxMinId, new Vector4(railsEffectMin, ringsEffectMin, 0f, 0f));

            meshRenderer.SetPropertyBlock(_block);

            float amplitude = Mathf.Abs(noiseAmplitude);
            if (amplitude != _boundsAmplitude) UpdateBounds(amplitude);
        }

        float SoloBlinkLevel()
        {
            if (soloBlinkRate <= 0f) return 1f;
            float phase = (float)(_soloTime * soloBlinkRate % 1.0);
            float duty = Mathf.Clamp01(soloDuty);
            float edge = soloFade * Mathf.Min(duty, 1f - duty) + 1e-4f;
            return SmoothStep(0f, edge, phase) * SmoothStep(duty, duty - edge, phase);
        }

        static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        int CellsOnWall(int wall) => Mathf.Max(1, (wall & 1) == 0 ? widthCells : heightCells);

        Transform FindFollowTarget()
        {
            if (_foundTarget != null) return _foundTarget;
            float now = Time.realtimeSinceStartup;
            if (now < _nextTargetSearch) return null;
            _nextTargetSearch = now + 1f;
            // The main camera is disabled in full dome, so look at inactive ones too. The dome warping camera is
            // also tagged MainCamera, so the Oxipital "Main Camera" (followed by the dome rig) comes first.
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

        bool ShapeChanged()
        {
            return cellSize != _builtCellSize || widthCells != _builtWidth || heightCells != _builtHeight
                || lengthCells != _builtLength || cellsBehind != _builtBehind || subdivisions != _builtSubdivisions;
        }

        void BuildMesh()
        {
            _builtCellSize = cellSize;
            _builtWidth = widthCells;
            _builtHeight = heightCells;
            _builtLength = lengthCells;
            _builtBehind = cellsBehind;
            _builtSubdivisions = subdivisions;

            float s = Mathf.Max(cellSize, 1e-3f);
            int w = Mathf.Max(1, widthCells);
            int h = Mathf.Max(1, heightCells);
            int length = Mathf.Max(2, lengthCells);
            int behind = Mathf.Clamp(cellsBehind, 0, length - 1);
            int d = Mathf.Clamp(subdivisions, 1, 16);
            float halfW = w * s * 0.5f;
            float halfH = h * s * 0.5f;

            // Walls go around the axis; the column direction turns so every face looks inward
            var starts = new[] { new Vector2(-halfW, -halfH), new Vector2(halfW, -halfH), new Vector2(halfW, halfH), new Vector2(-halfW, halfH) };
            var dirs = new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            var cells = new[] { w, h, w, h };

            int nv = length * d + 1;
            int vertexCount = 0, indexCount = 0;
            for (int i = 0; i < 4; i++)
            {
                int nu = cells[i] * d + 1;
                vertexCount += nu * nv;
                indexCount += (nu - 1) * (nv - 1) * 6;
            }

            var vertices = new Vector3[vertexCount];
            var uv0 = new Vector2[vertexCount];
            var uv1 = new Vector2[vertexCount];
            var indices = new int[indexCount];

            int v = 0, t = 0;
            float step = s / d;
            for (int wall = 0; wall < 4; wall++)
            {
                int nu = cells[wall] * d + 1;
                int baseVertex = v;
                for (int j = 0; j < nv; j++)
                {
                    float z = (j - behind * d) * step;
                    float row = (float)j / d - behind;
                    for (int i = 0; i < nu; i++)
                    {
                        Vector2 p = starts[wall] + dirs[wall] * (i * step);
                        vertices[v] = new Vector3(p.x, p.y, z);
                        uv0[v] = new Vector2((float)i / d, row);
                        uv1[v] = new Vector2(wall, 0f);
                        v++;
                    }
                }
                for (int j = 0; j < nv - 1; j++)
                {
                    for (int i = 0; i < nu - 1; i++)
                    {
                        int a = baseVertex + j * nu + i;
                        int b = a + nu;     // next row (+Z)
                        int c = a + 1;      // next column
                        int e = b + 1;
                        indices[t++] = a; indices[t++] = b; indices[t++] = c;
                        indices[t++] = c; indices[t++] = b; indices[t++] = e;
                    }
                }
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Grid Tunnel (generated)", hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic();
            }
            _mesh.Clear();
            _mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.vertices = vertices;
            _mesh.uv = uv0;
            _mesh.uv2 = uv1;
            _mesh.SetIndices(indices, MeshTopology.Triangles, 0, true);
            _filter.sharedMesh = _mesh;
            _boundsAmplitude = -1f;
        }

        void UpdateBounds(float amplitude)
        {
            _boundsAmplitude = amplitude;
            if (_mesh == null) return;
            float s = Mathf.Max(cellSize, 1e-3f);
            int length = Mathf.Max(2, lengthCells);
            int behind = Mathf.Clamp(cellsBehind, 0, length - 1);
            float zMin = -behind * s, zMax = (length - behind) * s;
            float pad = amplitude * 1.5f + s;
            var size = new Vector3(Mathf.Max(1, widthCells) * s + 2f * pad, Mathf.Max(1, heightCells) * s + 2f * pad, zMax - zMin + 2f * pad);
            var bounds = new Bounds(new Vector3(0f, 0f, (zMin + zMax) * 0.5f), size);
            // The submesh keeps its own bounds, used for culling: widen both for the noise displacement
            var sub = _mesh.GetSubMesh(0);
            sub.bounds = bounds;
            _mesh.SetSubMesh(0, sub, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.bounds = bounds;
        }
    }
}

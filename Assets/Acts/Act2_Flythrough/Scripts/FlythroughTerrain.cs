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
    /// along a route (TerrainShape). The bird is this object's position; the flight is along world +Z. The terrain
    /// moves under it like the Act 1 tunnel moves around the camera: the camera rig stays put, the terrain root is
    /// placed every frame so that the flight point is <see cref="altitude"/> metres above the ground.
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
        [Animate] [Tooltip("Follow the route centre line (through the mountain pass, along the valley). Off: steer with turnRate.")]
        public bool followRoute = true;
        [Animate] [Tooltip("Sideways offset from the route centre line (m).")]
        public float routeOffset = 0f;
        [Animate] [Tooltip("Turn speed when not following the route (degrees per second, positive = right).")]
        public float turnRate = 0f;
        [Tooltip("Seconds for the flight height to settle on the ground below.")]
        public float altitudeSmoothing = 2.5f;
        [Tooltip("How far ahead (m) the ground is anticipated.")]
        public float lookAhead = 600f;
        [Tooltip("Route distance (m) where the flight starts (ResetFlight).")]
        public float startDistance = 0f;

        [Header("World curl")]
        [Animate] [Tooltip("Rolls the ground up into a tube around the flight axis. 0 = flat, 1 = closed tube of radius curlRadius.")]
        [Range(0f, 1f)] public float curlLateral = 0f;
        [Tooltip("Tube radius (m) at lateral curl 1.")]
        public float curlRadius = 2500f;
        [Animate] [Tooltip("Lifts the ground ahead into a wall. 0 = flat, 1 = bend of radius bendRadius.")]
        [Range(0f, 1f)] public float curlForward = 0f;
        [Tooltip("Bend radius (m) at forward curl 1.")]
        public float bendRadius = 4000f;

        [Header("Landscape (rebuilds the terrain)")]
        public TerrainShape shape = TerrainShape.Default;

        [Header("Tiles (rebuild the terrain)")]
        [Tooltip("Size of the smallest tiles, near the camera (m).")]
        public float leafSize = 32f;
        [Tooltip("Quadtree levels: the largest tiles are leafSize x 2^levels.")]
        [Range(4, 14)] public int levels = 10;
        [Tooltip("Cells per tile side. More = finer relief everywhere.")]
        [Range(8, 64)] public int cellsPerTile = 32;
        [Tooltip("A tile splits when the camera is closer than its size times this. More = more detail far away.")]
        [Range(1f, 6f)] public float detailDistance = 1.5f;
        [Tooltip("Far clip plane given to every camera while the act is shown, in Play mode (m). The Oxipital cameras stop at 1 km.")]
        public float farClip = 40000f;
        [Tooltip("Keep flying in the Editor outside Play mode (Editor clock).")]
        public bool animateInEditMode = false;

        [Header("References")]
        [OSCQuery.DoNotExpose] public Material terrainMaterial;
        [Tooltip("Empty = the camera named \"Main Camera\" (Oxipital Cameras prefab), found at run time.")]
        [OSCQuery.DoNotExpose] public Transform followTarget;

        static readonly int CurlOriginId = Shader.PropertyToID("_CurlOrigin");
        static readonly int CurlLateralId = Shader.PropertyToID("_CurlLateral");
        static readonly int CurlForwardId = Shader.PropertyToID("_CurlForward");

        class Tile
        {
            public GameObject gameObject;
            public MeshRenderer renderer;
            public Mesh mesh;
            public long key;
        }

        // Flight state (terrain space: x across, z along the route)
        double2 _position;
        double _heading;            // radians, 0 = +Z
        float _ground;
        bool _groundReady, _started;

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
            _position = new double2(TerrainField.RouteX(startDistance, shape) + routeOffset, startDistance);
            _heading = math.atan(TerrainField.RouteSlope(startDistance, shape));
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

        void OnDisable()
        {
            RestoreFarClip();
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
            PlaceRoot();
            UpdateTiles();
            UpdateMaterial();
        }

        // ---------------------------------------------------------------- flight

        void Fly(float dt)
        {
            if (followRoute)
            {
                double z = _position.y + speed * dt;
                _position = new double2(TerrainField.RouteX((float)z, shape) + routeOffset, z);
                _heading = math.atan(TerrainField.RouteSlope((float)z, shape));
            }
            else
            {
                _heading += math.radians(turnRate) * dt;
                _position += new double2(math.sin(_heading), math.cos(_heading)) * (speed * dt);
            }

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

        // The terrain point under the bird ends up `altitude` metres below this object, the heading along world +Z
        void PlaceRoot()
        {
            var rotation = Quaternion.Euler(0f, -math.degrees((float)_heading), 0f);
            var ground = new Vector3((float)_position.x, _ground, (float)_position.y);
            _root.SetPositionAndRotation(transform.position - new Vector3(0f, altitude, 0f) - rotation * ground, rotation);
        }

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
            if (_root != null) return;
            var go = new GameObject("Terrain (generated)") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            _root = go.transform;
        }

        // ---------------------------------------------------------------- material

        void UpdateMaterial()
        {
            Vector3 origin = transform.position - new Vector3(0f, altitude, 0f);
            float lateral = curlLateral > 0f ? curlLateral / math.max(curlRadius, 1f) : 0f;
            float forward = curlForward > 0f ? curlForward / math.max(bendRadius, 1f) : 0f;

            _block.SetVector(CurlOriginId, origin);
            _block.SetFloat(CurlLateralId, lateral);
            _block.SetFloat(CurlForwardId, forward);

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
                var biomes = new NativeArray<float3>(grid * grid, Allocator.Temp);
                float2 range = new float2(float.MaxValue, float.MinValue);
                for (int j = -1; j <= grid; j++)
                {
                    for (int i = -1; i <= grid; i++)
                    {
                        float2 p = r.origin + new float2(i, j) * step;
                        bool inside = i >= 0 && j >= 0 && i < grid && j < grid;
                        float h = TerrainField.Height(p, shape, out float3 biome);
                        heights[(i + 1) + (j + 1) * border] = h;
                        if (inside)
                        {
                            biomes[i + j * grid] = biome;
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
                            color = new float4(biomes[i + j * grid], 1f),
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

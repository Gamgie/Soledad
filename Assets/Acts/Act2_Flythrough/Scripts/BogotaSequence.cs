using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace Soledad
{
    /// <summary>
    /// Act 2 film ending: from the curled valley into Bogota. Bogota (the GIS terrain, World/Bogota) stands upright
    /// across the valley ahead, its map facing the camera, so it fills the hole of the closed tube.
    ///
    /// The whole flight is one spline, the act's flight path (FlythroughTerrain.flightPath), with its heights baked in:
    /// the drawn part (its first <see cref="drawnKnots"/> knots: desert, mountains, valley, edited by hand) ends at the
    /// hand-off, then the generated tail flies straight on towards Bogota, turns a quarter up onto it and follows the
    /// zoom-18 strip to the lake, the map below the camera (its up is then the map's), and dives into the lake
    /// (<see cref="lakeDive"/>). A Timeline rides it by progress.
    ///
    /// Over the map, <see cref="skyVolume"/> turns the Physically Based Sky's planet so its zenith is the map's up
    /// (planet placed by the build, see PlaceSky).
    ///
    /// Three switches, meant for the Act 2 Timeline (through Show/Act2Controller): <see cref="showBogota"/>,
    /// <see cref="showLandscape"/> (terrain, clouds, monoliths) and <see cref="bogotaSky"/>. Play mode only: in the Editor everything stays visible.
    /// Context menu: Build Flight Path (regenerates the tail and places Bogota from the drawn part as it is), Bake Heights
    /// And Build Flight Path (first resamples the drawn part every <see cref="bakeSpacing"/> at
    /// <see cref="flightAltitude"/> above the relief).
    /// </summary>
    public class BogotaSequence : MonoBehaviour
    {
        [Animate] [Tooltip("Show Bogota (Play mode). Off by default: it appears during the sequence.")]
        public bool showBogota = false;
        [Animate] [Tooltip("Show the landscape: terrain, clouds and monoliths (Play mode). Off once Bogota fills the view.")]
        public bool showLandscape = true;
        [Animate] [Tooltip("The sky over Bogota (Play mode): switches on skyVolume, whose planet is turned so the sky's zenith is the map's up. A cut, not a blend: key it while Bogota's face fills the frame (no sky in view).")]
        public bool bogotaSky = false;

        [Header("References")]
        [OSCQuery.DoNotExpose] public FlythroughTerrain terrain;
        [OSCQuery.DoNotExpose] public GameObject bogota;
        [Tooltip("Hidden with the terrain when showLandscape is off.")]
        [OSCQuery.DoNotExpose] public GameObject[] landscape;

        [Header("Flight path (context menu: Build Flight Path)")]
        [Tooltip("Knots of the flight path drawn by hand, from its start to the hand-off (its last one). The knots after them are the generated tail, replaced by every build. -1 = all of them are drawn.")]
        public int drawnKnots = -1;
        [Tooltip("Bake: camera height above the relief (m), anticipated and smoothed like the Spline camera's ground following.")]
        [FormerlySerializedAs("handoffAltitude")]
        public float flightAltitude = 140f;
        [Tooltip("Bake: how far ahead (m) the relief is anticipated, so the path rises before a hill.")]
        public float groundLookAhead = 600f;
        [Tooltip("Bake: spacing of the knots of the drawn part (m).")]
        public float bakeSpacing = 200f;

        [Header("Towards Bogota (the generated tail)")]
        [Tooltip("The z18 route (Bogota_GIS film_path.json): x east, y ground height, z north, metres from the lake.")]
        [OSCQuery.DoNotExpose] public TextAsset route;
        [Tooltip("Straight flight (m) from the hand-off to the start of the turn up onto Bogota.")]
        public float approachLength = 2200f;
        [Tooltip("Radius (m) of the quarter turn up onto Bogota.")]
        public float climbRadius = 250f;
        [Tooltip("Height of the camera above Bogota's ground (m), on top of the ridges near the route.")]
        public float bogotaAltitude = 300f;
        [Tooltip("Metres of the z18 route skipped at its start.")]
        public float routeTrim = 0f;
        [Tooltip("Spacing of the knots along the route (m).")]
        public float knotSpacing = 300f;

        [Header("Into the lake (the end of the route: San Rafael reservoir)")]
        [Tooltip("End the path with a dive into the lake: the route's last knot tips down, then one knot under the water.")]
        public bool lakeDive = true;
        [Tooltip("Rotation of the route's last knot (above the lake) in the route's frame (Euler: x = nose down, y = heading, z = roll, degrees): tipped down, the path starts curving into the dive there.")]
        public Vector3 diveEntryRotation = new Vector3(17.8f, 1.5f, -1.3f);
        [Tooltip("Dive knot, past the route's last point along the route's direction (m).")]
        public float diveForward = 107f;
        [Tooltip("Dive knot, to the right of the route's direction (m).")]
        public float diveSide = 43f;
        [Tooltip("Dive knot, under the lake's surface (m). The surface = the route's last ground height.")]
        public float diveDepth = 161f;
        [Tooltip("Rotation of the dive knot in the route's frame (Euler: x = nose down, y = heading, z = roll, degrees). x near 90 = the camera looks at the lake from the zenith; with splineUp 1 the roll sets which way is up in the image while diving.")]
        public Vector3 diveRotation = new Vector3(74.3f, -176.1f, -98.5f);

        [Header("Sky over Bogota (planet placed by Build Flight Path)")]
        [Tooltip("Global Volume under Bogota, switched on by bogotaSky (Play mode; keep it inactive in the scene): the Physically Based Sky's planet is turned so its zenith is Bogota's up.")]
        [OSCQuery.DoNotExpose] public UnityEngine.Rendering.Volume skyVolume;

        int _applied = -1;

        void Update()
        {
            int state = (showBogota ? 1 : 0) | (showLandscape ? 2 : 0) | (bogotaSky ? 4 : 0);
            if (state == _applied) return;
            _applied = state;
            Apply(showBogota, showLandscape, bogotaSky);
        }

        void OnDisable()
        {
            if (_applied >= 0) Apply(true, true, false);
            _applied = -1;
        }

        void Apply(bool bogotaVisible, bool landscapeVisible, bool sky)
        {
            if (bogota != null && bogota.activeSelf != bogotaVisible) bogota.SetActive(bogotaVisible);
            if (landscape != null)
                foreach (var go in landscape)
                    if (go != null && go.activeSelf != landscapeVisible) go.SetActive(landscapeVisible);
            if (terrain != null) terrain.SetTerrainVisible(landscapeVisible);
            if (skyVolume != null && skyVolume.gameObject.activeSelf != sky) skyVolume.gameObject.SetActive(sky);
        }

#if UNITY_EDITOR

        /// <summary>Resamples the drawn part every bakeSpacing at flightAltitude above the relief, then builds the tail.</summary>
        [ContextMenu("Bake Heights And Build Flight Path")]
        public void BakeHeightsAndBuildFlightPath()
        {
            if (!CanBuild()) return;
            var flight = terrain.flightPath;
            float end = DrawnLength(flight);
            int n = Mathf.Max(1, Mathf.CeilToInt(end / Mathf.Max(bakeSpacing, 10f)));
            var pos = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i <= n; i++)
            {
                float d = end * i / n;
                FlightPoint(flight, d, end, out Vector3 p, out _);
                // Anticipated like the Spline camera: the path rises before a hill, never dips into it
                float g = terrain.GetGroundHeight(p);
                for (int k = 1; k <= 4; k++)
                {
                    FlightPoint(flight, d + groundLookAhead * k / 4f, end, out Vector3 q, out _);
                    g = Mathf.Max(g, terrain.GetGroundHeight(q) - flightAltitude * 0.25f * k);
                }
                p.y = g + flightAltitude;
                pos.Add(p);
            }
            for (int pass = 0; pass < 3; pass++)
            {
                var ys = new float[pos.Count];
                for (int i = 0; i < pos.Count; i++)
                    ys[i] = (pos[Mathf.Max(i - 1, 0)].y + 2f * pos[i].y + pos[Mathf.Min(i + 1, pos.Count - 1)].y) / 4f;
                for (int i = 0; i < pos.Count; i++) pos[i] = new Vector3(pos[i].x, ys[i], pos[i].z);
            }
            Build(pos);
        }

        /// <summary>Places Bogota upright across the valley ahead of the hand-off (the last drawn knot) and regenerates
        /// the tail of the flight path towards it. The drawn knots keep their positions (their tangents are recomputed).</summary>
        [ContextMenu("Build Flight Path")]
        public void BuildFlightPath()
        {
            if (!CanBuild()) return;
            var flight = terrain.flightPath;
            var s = flight.Spline;
            int count = drawnKnots < 2 || drawnKnots > s.Count ? s.Count : drawnKnots;
            var pos = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < count; i++) pos.Add(flight.transform.TransformPoint((Vector3)s[i].Position));
            Build(pos);
        }

        bool CanBuild()
        {
            if (terrain == null || terrain.flightPath == null || bogota == null || route == null)
            {
                Debug.LogError("BogotaSequence: terrain, its flight path, bogota and route are needed");
                return false;
            }
            if (terrain.flightPath.Splines.Count == 0 || terrain.flightPath.Spline.Count < 2)
            {
                Debug.LogError("BogotaSequence: the flight path needs at least two knots");
                return false;
            }
            return true;
        }

        // Length of the drawn part (to its last knot) along the current spline
        float DrawnLength(SplineContainer flight)
        {
            var s = flight.Spline;
            int last = (drawnKnots < 2 || drawnKnots > s.Count ? s.Count : drawnKnots) - 1;
            return s.ConvertIndexUnit(last, PathIndexUnit.Knot, PathIndexUnit.Distance);
        }

        // The drawn part (world positions, heights baked) + the generated tail, written into the flight path
        void Build(System.Collections.Generic.List<Vector3> drawn)
        {
            var points = ParseRoute(route.text);
            if (points.Count < 4) { Debug.LogError("BogotaSequence: route has too few points"); return; }
            var flight = terrain.flightPath;

            var pos = new System.Collections.Generic.List<Vector3>(drawn);
            var up = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < pos.Count; i++) up.Add(Vector3.up);
            int drawnCount = pos.Count;

            // Hand-off: the last drawn knot, flying level along the drawn part's last direction
            Vector3 h = pos[pos.Count - 1];
            Vector3 a = h - pos[pos.Count - 2];
            a.y = 0f;
            a = a.sqrMagnitude > 1e-6f ? a.normalized : Vector3.forward;
            // Straight on from there, at that height
            Vector3 b = h + a * approachLength;
            pos.Add(Vector3.Lerp(h, b, 0.5f)); up.Add(Vector3.up);
            // Quarter turn up: from level towards A to straight up, the camera's up turning to -A (Bogota's face)
            for (int i = 0; i <= 6; i++)
            {
                float th = math.radians(90f * i / 6f);
                pos.Add(b + climbRadius * (math.sin(th) * a + (1f - math.cos(th)) * Vector3.up));
                up.Add(-math.sin(th) * a + math.cos(th) * Vector3.up);
            }
            Vector3 top = pos[pos.Count - 1];

            // Route in Bogota's local space (x east, y height, z north), from routeTrim, every knotSpacing
            int first = Mathf.Clamp(Mathf.RoundToInt(routeTrim / 100f), 0, points.Count - 2);
            int every = Mathf.Max(1, Mathf.RoundToInt(knotSpacing / 100f));
            var local = new System.Collections.Generic.List<Vector3>();
            for (int i = first; i < points.Count; i += every)
                local.Add(new Vector3(points[i].x, points[i].y + bogotaAltitude, points[i].z));
            if ((local[local.Count - 1] - (Vector3)points[points.Count - 1]).sqrMagnitude > 1f)
                local.Add(new Vector3(points[points.Count - 1].x, points[points.Count - 1].y + bogotaAltitude, points[points.Count - 1].z));

            // Bogota upright: its up (height) faces back along -A, the route's first direction points up, its first
            // point at the top of the turn
            Vector3 d0 = local[1] - local[0];
            d0.y = 0f;
            Quaternion rotation = Quaternion.LookRotation(Vector3.up, -a) * Quaternion.Inverse(Quaternion.LookRotation(d0.normalized, Vector3.up));
            Vector3 position = top - rotation * local[0];
            UnityEditor.Undo.RecordObject(bogota.transform, "Place Bogota");
            bogota.transform.SetPositionAndRotation(position, rotation);
            if (bogota.transform.parent != null) bogota.transform.localScale = Vector3.one;

            for (int i = 1; i < local.Count; i++)
            {
                pos.Add(position + rotation * local[i]);
                up.Add(-a);
            }

            // Into the lake: the last route knot (bogotaAltitude above the water) tips its nose down, then one knot
            // under the water, its nose down (the camera sees the lake from the zenith, then goes through it).
            // Explicit knot rotations (Bogota frame: route direction, map up) and the dive knot's tangent as short as
            // the last route knot's, as the ending was first shaped by hand (Gamgie, 2026-11-26)
            var rotOverride = new System.Collections.Generic.Dictionary<int, Quaternion>();
            var lenOverride = new System.Collections.Generic.Dictionary<int, int>();   // knot -> knot whose length it takes
            if (lakeDive && local.Count >= 2)
            {
                Vector3 lastL = local[local.Count - 1];
                Vector3 dirL = lastL - local[local.Count - 2];
                dirL.y = 0f;
                dirL = dirL.sqrMagnitude > 1e-6f ? dirL.normalized : Vector3.right;
                Vector3 rightL = Vector3.Cross(Vector3.up, dirL);
                Quaternion routeFrame = Quaternion.LookRotation(rotation * dirL, rotation * Vector3.up);
                int lake = pos.Count - 1;
                rotOverride[lake] = routeFrame * Quaternion.Euler(diveEntryRotation);
                Vector3 diveL = lastL + dirL * diveForward + rightL * diveSide;
                diveL.y = points[points.Count - 1].y - diveDepth;
                pos.Add(position + rotation * diveL);
                up.Add(-a);
                rotOverride[pos.Count - 1] = routeFrame * Quaternion.Euler(diveRotation);
                lenOverride[pos.Count - 1] = lake;
            }
            PlaceSky();

            // Knots: tangents along the neighbours, a third of the shorter gap (smooth, no overshoot)
            UnityEditor.Undo.RecordObject(flight, "Build Flight Path");
            UnityEditor.Undo.RecordObject(this, "Build Flight Path");
            Transform pt = flight.transform;
            var spline = new Spline();
            float KnotLength(int i)
            {
                Vector3 p0 = pos[Mathf.Max(i - 1, 0)], p1 = pos[Mathf.Min(i + 1, pos.Count - 1)];
                return Mathf.Min(i > 0 ? (pos[i] - p0).magnitude : float.MaxValue,
                                 i < pos.Count - 1 ? (p1 - pos[i]).magnitude : float.MaxValue) / 3f;
            }
            for (int i = 0; i < pos.Count; i++)
            {
                Vector3 prev = pos[Mathf.Max(i - 1, 0)], next = pos[Mathf.Min(i + 1, pos.Count - 1)];
                Vector3 dir = (next - prev).normalized;
                float len = KnotLength(lenOverride.TryGetValue(i, out int from) ? from : i);
                Vector3 u = Vector3.ProjectOnPlane(up[i], dir).normalized;
                Quaternion knotRot = Quaternion.Inverse(pt.rotation) *
                    (rotOverride.TryGetValue(i, out Quaternion r) ? r : Quaternion.LookRotation(dir, u));
                Vector3 knotPos = pt.InverseTransformPoint(pos[i]);
                spline.Add(new BezierKnot(knotPos, new float3(0, 0, -len), new float3(0, 0, len), knotRot), TangentMode.Mirrored);
            }
            flight.Spline = spline;
            drawnKnots = drawnCount;
            UnityEditor.EditorUtility.SetDirty(flight);
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            float handoff = spline.ConvertIndexUnit(drawnCount - 1, PathIndexUnit.Knot, PathIndexUnit.Distance);
            Debug.Log(string.Format("Flight path: {0} knots ({1} drawn), {2:F1} km, hand-off at {3:F0} m; Bogota placed at {4}",
                pos.Count, drawnCount, spline.GetLength() / 1000f, handoff, bogota.transform.localPosition));
        }

        // The sky over upright Bogota: a Physically Based Sky whose planet lies behind the map (centre along -Bogota up,
        // its surface at the map's lowest point), so the sky's zenith, horizon and sunlight are the map's. HDRP blends a
        // planet centre linearly between volumes (halfway, the camera would be inside the planet), so this volume is a
        // cut: global, switched on by bogotaSky from the Timeline while Bogota's face fills the frame.
        void PlaceSky()
        {
            if (skyVolume == null) return;
            var profile = skyVolume.sharedProfile;
            if (profile == null || !profile.TryGet(out UnityEngine.Rendering.HighDefinition.VisualEnvironment env))
            {
                Debug.LogError("BogotaSequence: the sky volume needs a profile with a Visual Environment");
                return;
            }
            Transform bt = bogota.transform;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = -min;
            foreach (var r in bogota.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                var m = bt.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var p = m.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z));
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
            UnityEditor.Undo.RecordObject(skyVolume, "Place Bogota sky");
            skyVolume.isGlobal = true;

            // Planet: radius from the profile (km), surface at the map's lowest point
            float radius = env.planetRadius.value * 1000f;
            Vector3 center = bt.TransformPoint(new Vector3(0f, min.y, 0f)) - bt.up * radius;
            UnityEditor.Undo.RecordObject(profile, "Place Bogota sky");
            env.centerMode.Override(UnityEngine.Rendering.HighDefinition.VisualEnvironment.PlanetMode.Manual);
            env.planetCenter.Override(center / 1000f);
            env.renderingSpace.Override(UnityEngine.Rendering.HighDefinition.RenderingSpace.World);
            UnityEditor.EditorUtility.SetDirty(env);
            UnityEditor.EditorUtility.SetDirty(profile);
            UnityEditor.EditorUtility.SetDirty(skyVolume);
        }

        // World point and level direction of the flight path at a distance (m), clamped to [0, end]
        static void FlightPoint(SplineContainer flight, float distance, float end, out Vector3 position, out Vector3 direction)
        {
            Spline s = flight.Spline;
            float t = s.ConvertIndexUnit(Mathf.Clamp(distance, 0f, end), PathIndexUnit.Distance, PathIndexUnit.Normalized);
            SplineUtility.Evaluate(s, t, out float3 p, out float3 tangent, out float3 _);
            position = flight.transform.TransformPoint((Vector3)p);
            direction = flight.transform.TransformDirection((Vector3)tangent);
            direction.y = 0f;
            direction.Normalize();
        }

        static System.Collections.Generic.List<float3> ParseRoute(string json)
        {
            // film_path.json: {"step":..,"length":..,"points":[[x,y,z],...]} (JsonUtility cannot read nested arrays)
            var list = new System.Collections.Generic.List<float3>();
            int start = json.IndexOf("\"points\"");
            if (start < 0) return list;
            start = json.IndexOf('[', start) + 1;
            int i = start;
            while (true)
            {
                int open = json.IndexOf('[', i);
                if (open < 0) break;
                int close = json.IndexOf(']', open);
                var parts = json.Substring(open + 1, close - open - 1).Split(',');
                if (parts.Length == 3)
                    list.Add(new float3(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                                        float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                                        float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)));
                i = close + 1;
            }
            return list;
        }
#endif
    }
}

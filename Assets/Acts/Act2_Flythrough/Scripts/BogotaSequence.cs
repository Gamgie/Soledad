using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace Soledad
{
    /// <summary>
    /// Act 2 film ending: from the curled valley into Bogota. Bogota (the GIS terrain, World/Bogota) stands upright
    /// across the valley ahead, its map facing the camera, so it fills the hole of the closed tube. <see cref="bogotaPath"/>
    /// is the whole film: the flight path from <see cref="filmStartDistance"/> (ground height baked in), straight on
    /// towards Bogota from <see cref="handoffDistance"/>, a quarter turn up onto it, then over the zoom-18 strip to the
    /// lake, the map below the camera (its up is then the map's). A Timeline rides it by progress (0..1).
    ///
    /// Three switches, meant for the Act 2 Timeline (through Show/Act2Controller): <see cref="showBogota"/>,
    /// <see cref="showLandscape"/> (terrain, clouds, monoliths) and <see cref="rideBogota"/>. Play mode only: in the
    /// Editor everything stays visible. Bogota's placement and its path are built from the flight path by
    /// <see cref="BuildBogotaPath"/> (context menu), with the settings below: rebuild after changing them.
    /// </summary>
    public class BogotaSequence : MonoBehaviour
    {
        [Animate] [Tooltip("Show Bogota (Play mode). Off by default: it appears during the sequence.")]
        public bool showBogota = false;
        [Animate] [Tooltip("Show the landscape: terrain, clouds and monoliths (Play mode). Off once Bogota fills the view.")]
        public bool showLandscape = true;
        [Animate] [Tooltip("The Spline camera rides Bogota's path instead of the flight path, joining it where it is (Play mode).")]
        public bool rideBogota = false;

        [Header("References")]
        [OSCQuery.DoNotExpose] public FlythroughTerrain terrain;
        [OSCQuery.DoNotExpose] public GameObject bogota;
        [Tooltip("Built by Build Bogota Path, under Bogota (it moves with it).")]
        [OSCQuery.DoNotExpose] public SplineContainer bogotaPath;
        [Tooltip("Hidden with the terrain when showLandscape is off.")]
        [OSCQuery.DoNotExpose] public GameObject[] landscape;

        [Header("Bogota path (context menu: Build Bogota Path)")]
        [Tooltip("The z18 route (Bogota_GIS film_path.json): x east, y ground height, z north, metres from the lake.")]
        [OSCQuery.DoNotExpose] public TextAsset route;
        [Tooltip("Distance along the flight path (m) where the film starts: Bogota's path begins there, so the film rides one spline.")]
        public float filmStartDistance = 8924f;
        [Tooltip("Distance along the flight path (m) where Bogota's path leaves it, straight on towards Bogota.")]
        public float handoffDistance = 10700f;
        [Tooltip("Camera height above the valley at the hand-off (m): the Spline camera's altitude.")]
        public float handoffAltitude = 140f;
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

        int _applied = -1;

        void Update()
        {
            int state = (showBogota ? 1 : 0) | (showLandscape ? 2 : 0) | (rideBogota ? 4 : 0);
            if (state == _applied) return;
            _applied = state;
            Apply(showBogota, showLandscape, rideBogota);
        }

        void OnDisable()
        {
            if (_applied >= 0) Apply(true, true, false);
            _applied = -1;
        }

        void Apply(bool bogotaVisible, bool landscapeVisible, bool ride)
        {
            if (bogota != null && bogota.activeSelf != bogotaVisible) bogota.SetActive(bogotaVisible);
            if (landscape != null)
                foreach (var go in landscape)
                    if (go != null && go.activeSelf != landscapeVisible) go.SetActive(landscapeVisible);
            if (terrain != null)
            {
                terrain.SetTerrainVisible(landscapeVisible);
                terrain.RideSpline(ride ? bogotaPath : null, !ride);
            }
        }

#if UNITY_EDITOR

        /// <summary>Places Bogota upright across the valley ahead of the hand-off and builds its path (Editor).</summary>
        [ContextMenu("Build Bogota Path")]
        public void BuildBogotaPath()
        {
            if (terrain == null || terrain.flightPath == null || bogota == null || route == null)
            {
                Debug.LogError("BogotaSequence: terrain, its flight path, bogota and route are needed");
                return;
            }
            var points = ParseRoute(route.text);
            if (points.Count < 4) { Debug.LogError("BogotaSequence: route has too few points"); return; }

            // The film starts on the flight path at filmStartDistance: that stretch is baked into this path, at
            // handoffAltitude above the ground (anticipated and smoothed like the Spline camera does), so the whole
            // film rides one spline and a Timeline drives it with one progress curve (0..1)
            var flight = terrain.flightPath;
            var pos = new System.Collections.Generic.List<Vector3>();
            var up = new System.Collections.Generic.List<Vector3>();
            float start = Mathf.Min(filmStartDistance, handoffDistance);
            int n = Mathf.Max(1, Mathf.CeilToInt((handoffDistance - start) / 200f));
            Vector3 a = Vector3.forward;
            for (int i = 0; i <= n; i++)
            {
                float d = Mathf.Lerp(start, handoffDistance, i / (float)n);
                FlightPoint(flight, d, out Vector3 p, out a);
                float g = terrain.GetGroundHeight(p);
                for (int k = 1; k <= 4; k++)
                {
                    FlightPoint(flight, d + 600f * k / 4f, out Vector3 q, out _);
                    g = Mathf.Max(g, terrain.GetGroundHeight(q) - handoffAltitude * 0.25f * k);
                }
                p.y = g + handoffAltitude;
                pos.Add(p);
                up.Add(Vector3.up);
            }
            for (int pass = 0; pass < 3; pass++)
            {
                var ys = new float[pos.Count];
                for (int i = 0; i < pos.Count; i++)
                    ys[i] = (pos[Mathf.Max(i - 1, 0)].y + 2f * pos[i].y + pos[Mathf.Min(i + 1, pos.Count - 1)].y) / 4f;
                for (int i = 0; i < pos.Count; i++) pos[i] = new Vector3(pos[i].x, ys[i], pos[i].z);
            }
            // Hand-off: straight on from there, level, at that height
            Vector3 h = pos[pos.Count - 1];
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
            Transform parent = bogota.transform.parent;
            UnityEditor.Undo.RecordObject(bogota.transform, "Place Bogota");
            bogota.transform.SetPositionAndRotation(position, rotation);
            if (parent != null) bogota.transform.localScale = Vector3.one;

            for (int i = 1; i < local.Count; i++)
            {
                pos.Add(position + rotation * local[i]);
                up.Add(-a);
            }

            // The spline, under Bogota so it moves with it
            if (bogotaPath == null)
            {
                var go = new GameObject("Bogota Path");
                UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Bogota Path");
                go.transform.SetParent(bogota.transform, false);
                bogotaPath = go.AddComponent<SplineContainer>();
            }
            UnityEditor.Undo.RecordObject(bogotaPath, "Bogota Path");
            Transform pt = bogotaPath.transform;
            pt.localPosition = Vector3.zero;
            pt.localRotation = Quaternion.identity;
            var spline = new Spline();
            for (int i = 0; i < pos.Count; i++)
            {
                Vector3 prev = pos[Mathf.Max(i - 1, 0)], next = pos[Mathf.Min(i + 1, pos.Count - 1)];
                Vector3 dir = (next - prev).normalized;
                float len = Mathf.Min(i > 0 ? (pos[i] - prev).magnitude : float.MaxValue,
                                      i < pos.Count - 1 ? (next - pos[i]).magnitude : float.MaxValue) / 3f;
                Vector3 u = Vector3.ProjectOnPlane(up[i], dir).normalized;
                Quaternion knotRot = Quaternion.Inverse(pt.rotation) * Quaternion.LookRotation(dir, u);
                Vector3 knotPos = pt.InverseTransformPoint(pos[i]);
                spline.Add(new BezierKnot(knotPos, new float3(0, 0, -len), new float3(0, 0, len), knotRot), TangentMode.Mirrored);
            }
            bogotaPath.Spline = spline;
            UnityEditor.EditorUtility.SetDirty(bogotaPath);
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log(string.Format("Bogota path: {0} knots, {1:F1} km; Bogota placed at {2}, turn up {3:F0} m after the hand-off",
                pos.Count, spline.GetLength() / 1000f, bogota.transform.localPosition, approachLength));
        }


        // World point and level direction of the flight path at a distance (m)
        static void FlightPoint(SplineContainer flight, float distance, out Vector3 position, out Vector3 direction)
        {
            Spline s = flight.Spline;
            float t = s.ConvertIndexUnit(Mathf.Clamp(distance, 0f, s.GetLength()), PathIndexUnit.Distance, PathIndexUnit.Normalized);
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

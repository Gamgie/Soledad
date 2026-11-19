using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// The Earth (prefab Assets/Shared/Earth/Earth.prefab), usable in any act. The object itself stays unrotated: this
    /// script builds the globe mesh on the child "Globe" (a unit sphere, never saved, drawn with Soledad/Earth) and
    /// turns it every frame:
    ///   - Season: <see cref="month"/> sets the sun's declination (the angle between the axis and the sun), from the
    ///     sun light's direction. The pole leans towards <see cref="poleHint"/> as far as the season allows (by
    ///     default the north celestial pole of the Milky Way sky of Act 5, which makes the late December Earth true to
    ///     the stars when the sun shines from +Z, towards the galactic centre).
    ///   - Day: <see cref="hour"/> (UTC: 12 = the sun over Greenwich) plus the time elapsed, one turn per
    ///     <see cref="dayDuration"/> seconds. Time accumulates from Time.deltaTime (deterministic Recorder renders).
    ///   - Clouds drift by <see cref="cloudDrift"/> degrees per day over the ground.
    /// It also gives the shader the sun direction (night side lights).
    /// Mesh conventions (also the Earth.fbx / Earth.glb ones): north pole local +Y, Greenwich local -X, 90 E local -Z.
    /// </summary>
    [ExecuteAlways]
    public class EarthGlobe : MonoBehaviour
    {
        const float AxialTilt = 23.44f;
        const float YearDays = 365.25f;

        [Header("Season and time of day")]
        [Animate] [Tooltip("Time of year: 1 = 1 January, 7 = 1 July, 12.7 = winter solstice (21 December), up to 13.")]
        [Range(1f, 13f)] public float month = 12.7f;
        [Animate] [Tooltip("Length of a day, in seconds: one turn of the Earth (0 = it stands still).")]
        [Min(0f)] public float dayDuration = 600f;
        [Animate] [Tooltip("Start time, UTC hours: 12 = the sun over Greenwich. The time elapsed since is added to it.")]
        [Range(0f, 24f)] public float hour = 12f;
        [Tooltip("Let the day go by in the Editor outside Play mode too (preview).")]
        public bool runInEditMode;

        [Header("Orientation")]
        [Tooltip("Sun light (empty: the directional light named \"Sun\", else the first directional light).")]
        public Light sun;
        [Tooltip("World direction the north pole leans towards, as far as the season allows. Default: the north " +
                 "celestial pole of the Milky Way sky of Act 5 (galactic longitude 122.93, latitude +27.13).")]
        public Vector3 poleHint = new Vector3(-0.7471f, 0.4560f, -0.4838f);

        [Header("Clouds")]
        [Animate] [Tooltip("Cloud drift over the ground, degrees per day (eastward > 0).")]
        public float cloudDrift = 6f;

        [Header("Globe")]
        public Material material;
        [Tooltip("Segments around the equator (the globe is a UV sphere of radius 1; scale the Earth object).")]
        [Range(32, 512)] public int segments = 256;
        public bool castShadows = true;

        static readonly int SunDirectionId = Shader.PropertyToID("_EarthSunDirection");
        static readonly int CloudOffsetId = Shader.PropertyToID("_CloudOffset");

        Light _foundSun;
        Transform _globe;
        MeshFilter _filter;
        MeshRenderer _renderer;
        Mesh _mesh;
        int _builtSegments;
        MaterialPropertyBlock _block;
        double _elapsedDays;   // time gone by, in days (deterministic: sum of Time.deltaTime / dayDuration)

        /// <summary>Time of day now, UTC hours.</summary>
        public float CurrentHour => Mathf.Repeat(hour + (float)(_elapsedDays % 1.0) * 24f, 24f);

        /// <summary>Restart the day from <see cref="hour"/>.</summary>
        [Animate]
        public void ResetDay() => _elapsedDays = 0.0;

        void OnEnable()
        {
            EnsureGlobe();
            Apply();
        }

        void OnDisable()
        {
            if (_mesh != null) DestroyImmediateOrNot(_mesh);
            _mesh = null;
            _builtSegments = 0;
        }

        void Update()
        {
            if ((Application.isPlaying || runInEditMode) && dayDuration > 0f)
                _elapsedDays += Time.deltaTime / dayDuration;
            EnsureGlobe();
            Apply();
        }

        void Apply()
        {
            if (_globe == null) return;
            Vector3 toSun = SunDirection();
            Shader.SetGlobalVector(SunDirectionId, toSun);

            // Season: the axis makes (90 - declination) degrees with the sun, leaning towards the pole hint
            float day = (month - 1f) * YearDays / 12f;
            float declination = -AxialTilt * Mathf.Cos(2f * Mathf.PI * (day + 10f) / YearDays) * Mathf.Deg2Rad;
            Vector3 hint = Vector3.ProjectOnPlane(poleHint.sqrMagnitude > 1e-6f ? poleHint.normalized : Vector3.up, toSun);
            if (hint.sqrMagnitude < 1e-6f) hint = Vector3.ProjectOnPlane(Vector3.forward, toSun);
            hint.Normalize();
            Vector3 axis = (Mathf.Sin(declination) * toSun + Mathf.Cos(declination) * hint).normalized;

            // Day: at 12 UTC Greenwich faces the sun; the sun is over longitude (12 - hour) x 15 degrees east
            Vector3 sunOnEquator = Vector3.ProjectOnPlane(toSun, axis).normalized;
            Vector3 east = Vector3.Cross(sunOnEquator, axis);   // east of a point on the equator (Unity is left-handed)
            float subSolar = (12f - CurrentHour) * 15f * Mathf.Deg2Rad;
            Vector3 greenwich = Mathf.Cos(subSolar) * sunOnEquator - Mathf.Sin(subSolar) * east;
            Quaternion world = Quaternion.LookRotation(Vector3.Cross(axis, greenwich), axis);   // +Y = axis, -X = Greenwich
            _globe.localRotation = Quaternion.Inverse(transform.rotation) * world;

            if (_renderer != null)
            {
                _block ??= new MaterialPropertyBlock();
                _renderer.GetPropertyBlock(_block);
                _block.SetFloat(CloudOffsetId, Mathf.Repeat((float)(_elapsedDays * cloudDrift / 360.0), 1f));
                _renderer.SetPropertyBlock(_block);
                var shadows = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                if (_renderer.shadowCastingMode != shadows) _renderer.shadowCastingMode = shadows;
                if (_renderer.sharedMaterial != material) _renderer.sharedMaterial = material;
            }
        }

        // Found at run time, never stored in `sun`: the Sun lives in Soledad_Main and an act scene cannot keep a
        // reference to it
        Vector3 SunDirection()
        {
            Light light = sun;
            if (light == null || !light.isActiveAndEnabled)
            {
                if (_foundSun == null || !_foundSun.isActiveAndEnabled) _foundSun = FindSun();
                light = _foundSun;
            }
            return light != null ? -light.transform.forward : Vector3.forward;
        }

        // ---------------------------------------------------------------- globe

        void EnsureGlobe()
        {
            if (_globe == null)
            {
                _globe = transform.Find("Globe");
                if (_globe == null)
                {
                    _globe = new GameObject("Globe").transform;
                    _globe.SetParent(transform, false);
                }
                _filter = null;
                _renderer = null;
            }
            if (_filter == null && !_globe.TryGetComponent(out _filter)) _filter = _globe.gameObject.AddComponent<MeshFilter>();
            if (_renderer == null && !_globe.TryGetComponent(out _renderer)) _renderer = _globe.gameObject.AddComponent<MeshRenderer>();
            if (_mesh == null || _builtSegments != segments)
            {
                if (_mesh != null) DestroyImmediateOrNot(_mesh);
                _mesh = BuildSphere(segments);
                _builtSegments = segments;
            }
            if (_filter.sharedMesh != _mesh) _filter.sharedMesh = _mesh;
        }

        // UV sphere of radius 1: normals = positions, tangents eastward, uv equirectangular (the shader does not use
        // them, it looks the surface up from the direction)
        static Mesh BuildSphere(int around)
        {
            int rings = Mathf.Max(around / 2, 8);
            int columns = around + 1;
            var vertices = new Vector3[columns * (rings + 1)];
            var tangents = new Vector4[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            for (int r = 0; r <= rings; r++)
            {
                float lat = Mathf.PI * ((float)r / rings - 0.5f);
                float y = Mathf.Sin(lat), c = Mathf.Cos(lat);
                for (int i = 0; i < columns; i++)
                {
                    float lon = 2f * Mathf.PI * ((float)i / around - 0.5f);
                    // Greenwich (lon 0) = -X, 90 E = -Z
                    var p = new Vector3(-c * Mathf.Cos(lon), y, -c * Mathf.Sin(lon));
                    int k = r * columns + i;
                    vertices[k] = p;
                    tangents[k] = new Vector4(Mathf.Sin(lon), 0f, -Mathf.Cos(lon), 1f);   // eastward
                    uvs[k] = new Vector2((float)i / around, (float)r / rings);
                }
            }
            var triangles = new int[around * rings * 6];
            int t = 0;
            for (int r = 0; r < rings; r++)
                for (int i = 0; i < around; i++)
                {
                    int a = r * columns + i, b = a + 1, cc = a + columns, d = cc + 1;
                    triangles[t++] = a; triangles[t++] = cc; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = cc; triangles[t++] = d;
                }
            var mesh = new Mesh { name = "Earth Globe (generated)", hideFlags = HideFlags.DontSave };
            mesh.indexFormat = vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = vertices;
            mesh.tangents = tangents;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }

        static void DestroyImmediateOrNot(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
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
    }
}

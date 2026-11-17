using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Soledad
{
    /// <summary>
    /// Act 3: a light bulb rising from the deep. It starts where it is enabled (or where a spawner puts it with
    /// <see cref="Launch"/>), rises along Y at <see cref="riseSpeed"/> with a slow brownian drift on X and Z, fades in, and
    /// at the end of its <see cref="life"/> fades out (light intensity and sphere size down to 0). Then it calls
    /// <see cref="finished"/> (a spawner takes it back) or, alone, starts again from its start (<see cref="loop"/>) or
    /// destroys itself. Time comes from Time.deltaTime and the drift is seeded, so a Recorder render repeats. The light is
    /// the child "Light" (HDRP point), the bulb the child "Mesh" (Oxipital's Light Sphere material, colour set per
    /// bulb). Nothing is allocated per frame.
    /// </summary>
    public class RisingBulb : MonoBehaviour
    {
        [Header("Motion")]
        [Tooltip("Rising speed (m/s).")]
        public float riseSpeed = 2f;
        [Tooltip("How far it drifts sideways (m, on X and Z).")]
        public float drift = 1.5f;
        [Tooltip("How fast it drifts (Hz).")]
        public float driftFrequency = 0.12f;

        [Header("Life")]
        [Tooltip("Seconds from its start to the end of its fade out.")]
        public float life = 20f;
        [Tooltip("Seconds to fade in at the start.")]
        public float fadeIn = 1.5f;
        [Tooltip("Seconds to fade out (intensity and size to 0) at the end of its life.")]
        public float fadeOut = 4f;
        [Tooltip("Alone (no spawner): start again from the bottom at the end of its life. Off: destroyed.")]
        public bool loop = true;

        [Header("Look")]
        [ColorUsage(false)] public Color color = new Color(1f, 0.38f, 0.16f);
        [Tooltip("Luminous power (lumen) at full life.")]
        public float intensity = 6000f;
        [Tooltip("Distance the light reaches (m).")]
        public float range = 25f;
        [Tooltip("Bulb diameter (m) at full life.")]
        public float size = 0.5f;
        [Tooltip("Bulb brightness for the light's colour.")]
        public float meshEmission = 6f;
        public bool volumetric = true;
        public float volumetricIntensity = 1.5f;
        public bool shadows = false;
        public int seed = 0;

        [Header("Parts")]
        public Light lightSource;
        public MeshRenderer bulb;

        /// <summary>Called at the end of its life when a spawner launched it (instead of loop / destroy).</summary>
        public System.Action<RisingBulb> finished;

        static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");

        float _age;
        Vector3 _start;
        float _offsetX, _offsetZ;
        MaterialPropertyBlock _block;
        HDAdditionalLightData _lightData;

        void Awake()
        {
            Init();
        }

        // Also from OnEnable: a script recompile in Play mode clears these fields and calls OnEnable without Awake
        void Init()
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            if (_lightData == null && lightSource != null) _lightData = lightSource.GetComponent<HDAdditionalLightData>();
        }

        void OnEnable()
        {
            Init();
            if (finished == null) Launch(transform.position, seed);
        }

        /// <summary>Starts a life at this position, with this seed (its drift).</summary>
        public void Launch(Vector3 start, int bulbSeed)
        {
            _start = start;
            _age = 0f;
            seed = bulbSeed;
            _offsetX = Mathf.Repeat(bulbSeed * 12.9898f, 1000f);
            _offsetZ = Mathf.Repeat(bulbSeed * 78.233f + 500f, 1000f);
            Apply(0f);
        }

        void Update()
        {
            _age += Time.deltaTime;
            if (_age >= life)
            {
                if (finished != null) { finished(this); return; }
                if (loop) { Launch(_start, seed); return; }
                Destroy(gameObject);
                return;
            }
            Apply(_age);
        }

        void Apply(float age)
        {
            // Rise, with a smooth drift on X and Z (fractal noise, starts at 0 so the bulb leaves its spawn point)
            float t = age * driftFrequency;
            float dx = Fbm(t, _offsetX) - Fbm(0f, _offsetX);
            float dz = Fbm(t, _offsetZ) - Fbm(0f, _offsetZ);
            transform.position = _start + new Vector3(dx * drift, age * riseSpeed, dz * drift);

            // Fade in, then out at the end of its life
            float fin = fadeIn > 0f ? Mathf.Clamp01(age / fadeIn) : 1f;
            float fout = fadeOut > 0f ? Mathf.Clamp01((life - age) / fadeOut) : 1f;
            float level = Smooth(fin) * Smooth(fout);

            if (lightSource != null)
            {
                bool on = level > 0.001f;
                if (lightSource.enabled != on) lightSource.enabled = on;
                if (on)
                {
                    if (lightSource.lightUnit != UnityEngine.Rendering.LightUnit.Lumen) lightSource.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
                    lightSource.intensity = intensity * level;
                    lightSource.color = color;
                    if (lightSource.range != range) lightSource.range = range;
                    var want = shadows ? LightShadows.Soft : LightShadows.None;
                    if (lightSource.shadows != want) lightSource.shadows = want;
                    if (_lightData != null)
                    {
                        if (_lightData.affectsVolumetric != volumetric) _lightData.affectsVolumetric = volumetric;
                        if (_lightData.volumetricDimmer != volumetricIntensity) _lightData.volumetricDimmer = volumetricIntensity;
                    }
                }
            }
            if (bulb != null)
            {
                float s = size * level;
                bulb.transform.localScale = new Vector3(s, s, s);
                Color glow = color.linear * (meshEmission * level);
                glow.a = 1f;
                _block.SetColor(UnlitColorId, glow);
                bulb.SetPropertyBlock(_block);
            }
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);

        // Fractal noise in about -1..1
        static float Fbm(float t, float offset)
        {
            return ((Mathf.PerlinNoise(t, offset) * 2f - 1f) + 0.5f * (Mathf.PerlinNoise(t * 2.03f, offset + 17.1f) * 2f - 1f)) / 1.5f;
        }
    }
}

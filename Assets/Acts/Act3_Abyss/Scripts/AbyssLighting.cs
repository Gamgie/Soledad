using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Soledad
{
    /// <summary>
    /// Act 3 (Abyss) daylight: while the act is shown, the main scene's Sun comes almost straight down through the
    /// opening at the top of the rocks, cold and dim, and lights up the volumetric fog where the rocks let it through:
    /// the light shafts. Everything else stays black but for the Abyss Lights (an Oxipital Light Group). The Sun's own
    /// direction, colour and intensity come back when the act ends and around scene saves, so Soledad_Main never
    /// keeps them (same as Act 2's sun).
    /// </summary>
    [ExecuteAlways]
    public class AbyssLighting : MonoBehaviour
    {
        [Header("Sun through the opening (the Sun of Soledad_Main, while the act is shown)")]
        public bool overrideSun = true;
        [Animate] [Tooltip("Sun height (degrees): 90 = straight down the hole, lower = slanted shafts.")]
        [Range(0f, 90f)] public float sunElevation = 78f;
        [Animate] [Tooltip("Sun direction around the hole (degrees).")]
        [Range(-180f, 180f)] public float sunAzimuth = 30f;
        [Animate] [Tooltip("Sun intensity (lux): how bright the shafts are.")]
        [Range(0f, 130000f)] public float sunIntensity = 20000f;
        [Animate] [ColorUsage(false)]
        public Color sunColor = new Color(0.55f, 0.82f, 1f);
        [Animate] [Tooltip("How much the sun lights the fog: the strength of the shafts against the lit rocks.")]
        [Range(0f, 16f)] public float shaftStrength = 6f;

        Light _sun;
        HDAdditionalLightData _sunData;
        // The Sun's own values, serialized so a script reload in the Editor keeps them (otherwise the act would take
        // its own values for the originals after a recompile and leave them on the Sun)
        [SerializeField, HideInInspector] bool _overridden;
        [SerializeField, HideInInspector] Quaternion _rotation;
        [SerializeField, HideInInspector] float _intensity, _temperature, _volumetric;
        [SerializeField, HideInInspector] Color _color;
        [SerializeField, HideInInspector] bool _useTemperature;

        void OnEnable()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnSceneSaved;
#endif
            Apply();
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
#endif
            Restore();
        }

#if UNITY_EDITOR
        void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => Restore();
        void OnSceneSaved(UnityEngine.SceneManagement.Scene scene) => Apply();
#endif

        void Update() => Apply();

        void Apply()
        {
            if (!overrideSun) { Restore(); return; }
            if (_sun == null) _sun = FindSun();
            if (_sun == null) return;
            if (_sunData == null) _sunData = _sun.GetComponent<HDAdditionalLightData>();
            if (!_overridden)
            {
                _sunData = _sun.GetComponent<HDAdditionalLightData>();
                _rotation = _sun.transform.rotation;
                _intensity = _sun.intensity;
                _color = _sun.color;
                _temperature = _sun.colorTemperature;
                _useTemperature = _sun.useColorTemperature;
                _volumetric = _sunData != null ? _sunData.volumetricDimmer : 1f;
                _overridden = true;
            }
            var rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
            if (_sun.transform.rotation != rotation) _sun.transform.rotation = rotation;
            if (_sun.useColorTemperature) _sun.useColorTemperature = false;
            if (_sun.color != sunColor) _sun.color = sunColor;
            if (_sun.intensity != sunIntensity) _sun.intensity = sunIntensity;
            if (_sunData != null && _sunData.volumetricDimmer != shaftStrength) _sunData.volumetricDimmer = shaftStrength;
        }

        void Restore()
        {
            if (_overridden && _sun != null)
            {
                _sun.transform.rotation = _rotation;
                _sun.intensity = _intensity;
                _sun.color = _color;
                _sun.colorTemperature = _temperature;
                _sun.useColorTemperature = _useTemperature;
                if (_sunData != null) _sunData.volumetricDimmer = _volumetric;
            }
            _overridden = false;
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

using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// Act 4 (See Through) daylight: while the act is shown, the main scene's Sun is a clear day sun over the hill
    /// (height, direction, colour, intensity, all [Animate]). The Sun's own direction, colour and intensity come back
    /// when the act ends and around scene saves, so Soledad_Main never keeps them (as Acts 2 and 3).
    /// </summary>
    [ExecuteAlways]
    public class HillLighting : MonoBehaviour
    {
        [Header("Sun over the hill (the Sun of Soledad_Main, while the act is shown)")]
        public bool overrideSun = true;
        [Animate] [Tooltip("Sun height above the horizon (degrees).")]
        [Range(-10f, 90f)] public float sunElevation = 50f;
        [Animate] [Tooltip("Sun direction around the hill (degrees, 0 = towards +Z).")]
        [Range(-180f, 180f)] public float sunAzimuth = -40f;
        [Animate] [Tooltip("Sun intensity (lux).")]
        [Range(0f, 130000f)] public float sunIntensity = 100000f;
        [Animate] [ColorUsage(false)]
        public Color sunColor = new Color(1f, 0.96f, 0.9f);

        Light _sun;
        // The Sun's own values, serialized so a script reload in the Editor keeps them
        [SerializeField, HideInInspector] bool _overridden;
        [SerializeField, HideInInspector] Quaternion _rotation;
        [SerializeField, HideInInspector] float _intensity, _temperature;
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
            if (!_overridden)
            {
                _rotation = _sun.transform.rotation;
                _intensity = _sun.intensity;
                _color = _sun.color;
                _temperature = _sun.colorTemperature;
                _useTemperature = _sun.useColorTemperature;
                _overridden = true;
            }
            var rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
            if (_sun.transform.rotation != rotation) _sun.transform.rotation = rotation;
            if (_sun.useColorTemperature) _sun.useColorTemperature = false;
            if (_sun.color != sunColor) _sun.color = sunColor;
            if (_sun.intensity != sunIntensity) _sun.intensity = sunIntensity;
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

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Soledad
{
    /// <summary>
    /// Act 4 (See Through) daylight: while the act is shown, the main scene's Sun is a clear day sun over the hill
    /// (height, direction, colour, intensity, all [Animate]). The Sun's own values are kept by
    /// <see cref="SunControl"/>, which gives them back when the act ends and around scene saves, so Soledad_Main
    /// never keeps them (as Acts 2, 3 and 5).
    /// </summary>
    [ExecuteAlways]
    public class HillLighting : MonoBehaviour, ISunDriver
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

        void OnEnable() => SunControl.Register(this);
        void OnDisable() => SunControl.Unregister(this);
        void Update() => SunControl.Drive(this);

        public bool DrivesSun => overrideSun;

        public void DriveSun(Light sun, HDAdditionalLightData data)
        {
            var rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
            if (sun.transform.rotation != rotation) sun.transform.rotation = rotation;
            if (sun.useColorTemperature) sun.useColorTemperature = false;
            if (sun.color != sunColor) sun.color = sunColor;
            if (sun.intensity != sunIntensity) sun.intensity = sunIntensity;
        }
    }
}

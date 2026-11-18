using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Soledad
{
    /// <summary>
    /// Act 5 (Memento Mori) sunlight in space: while the act is shown, the main scene's Sun lights the Earth from the
    /// Seleynora direction (height, direction, temperature, intensity, all [Animate]), and the visible Sun Disc (with
    /// its lens flare) is kept on the light's axis, <see cref="sunDiscDistance"/> from the act root (the Earth).
    /// The Sun's own values are kept by <see cref="SunControl"/>, which gives them back when the act ends and around
    /// scene saves, so Soledad_Main never keeps them (as Acts 2 to 4).
    /// </summary>
    [ExecuteAlways]
    public class SpaceLighting : MonoBehaviour, ISunDriver
    {
        [Header("Sun (the Sun of Soledad_Main, while the act is shown)")]
        public bool overrideSun = true;
        [Animate] [Tooltip("Sun height above the Earth's equator plane (degrees).")]
        [Range(-90f, 90f)] public float sunElevation = 0f;
        [Animate] [Tooltip("Sun direction around the Earth (degrees, 0 = light travelling towards +Z; 180 = the sun " +
                           "on the +Z side, the Moon's side, as in Seleynora).")]
        [Range(-180f, 180f)] public float sunAzimuth = 180f;
        [Animate] [Tooltip("Sun intensity (lux).")]
        [Range(0f, 130000f)] public float sunIntensity = 78812f;
        [Animate] [Tooltip("Sun colour temperature (K).")]
        [Range(1500f, 20000f)] public float sunTemperature = 6570f;

        [Header("Sun Disc (visible sun, lens flare)")]
        public Transform sunDisc;
        [Animate] public bool showSunDisc = true;
        [Tooltip("Distance of the disc from the act root, against the light (m).")]
        public float sunDiscDistance = 500f;

        void OnEnable() => SunControl.Register(this);
        void OnDisable() => SunControl.Unregister(this);

        void Update()
        {
            PlaceSunDisc();
            SunControl.Drive(this);
        }

        public bool DrivesSun => overrideSun;

        public void DriveSun(Light sun, HDAdditionalLightData data)
        {
            var rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
            if (sun.transform.rotation != rotation) sun.transform.rotation = rotation;
            if (!sun.useColorTemperature) sun.useColorTemperature = true;
            if (sun.color != Color.white) sun.color = Color.white;
            if (sun.colorTemperature != sunTemperature) sun.colorTemperature = sunTemperature;
            if (sun.intensity != sunIntensity) sun.intensity = sunIntensity;
        }

        void PlaceSunDisc()
        {
            if (sunDisc == null) return;
            if (sunDisc.gameObject.activeSelf != showSunDisc) sunDisc.gameObject.SetActive(showSunDisc);
            var rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
            var position = transform.position - rotation * Vector3.forward * sunDiscDistance;
            if (sunDisc.position != position) sunDisc.position = position;
        }
    }
}

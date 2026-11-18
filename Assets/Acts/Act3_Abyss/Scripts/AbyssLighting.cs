using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Soledad
{
    /// <summary>
    /// Act 3 (Abyss) daylight: while the act is shown, the main scene's Sun comes almost straight down through the
    /// opening at the top of the rocks, cold and dim, and lights up the volumetric fog where the rocks let it through:
    /// the light shafts. Everything else stays black but for the Abyss Lights (an Oxipital Light Group). The Sun's own
    /// values are kept by <see cref="SunControl"/>, which gives them back when the act ends and around scene saves,
    /// so Soledad_Main never keeps them.
    /// </summary>
    [ExecuteAlways]
    public class AbyssLighting : MonoBehaviour, ISunDriver
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
            if (data != null && data.volumetricDimmer != shaftStrength) data.volumetricDimmer = shaftStrength;
        }
    }
}

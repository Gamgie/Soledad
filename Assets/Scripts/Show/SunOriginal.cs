using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Soledad
{
    /// <summary>
    /// The Sun's own values while an act drives it, kept by <see cref="SunControl"/> on the Sun itself: hidden and
    /// never saved, but serialized, so a script reload in the Editor keeps them. Not to be added by hand.
    /// </summary>
    [AddComponentMenu("")]
    public class SunOriginal : MonoBehaviour
    {
        [SerializeField] Quaternion _rotation;
        [SerializeField] float _intensity, _temperature, _volumetric, _penumbra;
        [SerializeField] Color _color;
        [SerializeField] bool _useTemperature, _resolutionUseOverride;
        [SerializeField] int _resolution;
        HDAdditionalLightData _data;

        public HDAdditionalLightData Data => _data != null ? _data : _data = GetComponent<HDAdditionalLightData>();

        public void CaptureFrom(Light sun)
        {
            _rotation = sun.transform.localRotation;
            _intensity = sun.intensity;
            _color = sun.color;
            _temperature = sun.colorTemperature;
            _useTemperature = sun.useColorTemperature;
            var data = Data;
            if (data == null) return;
            _volumetric = data.volumetricDimmer;
            _resolution = data.shadowResolution.@override;
            _resolutionUseOverride = data.shadowResolution.useOverride;
            _penumbra = data.dirLightPCSSMaxPenumbraSize;
        }

        public void ApplyTo(Light sun)
        {
            sun.transform.localRotation = _rotation;
            sun.intensity = _intensity;
            sun.color = _color;
            sun.colorTemperature = _temperature;
            sun.useColorTemperature = _useTemperature;
            var data = Data;
            if (data == null) return;
            data.volumetricDimmer = _volumetric;
            data.shadowResolution.@override = _resolution;
            data.shadowResolution.useOverride = _resolutionUseOverride;
            data.dirLightPCSSMaxPenumbraSize = _penumbra;
        }
    }
}

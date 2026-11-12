using UnityEngine;
using UnityEngine.Rendering;

namespace Soledad
{
    // Root of an act scene. ShowController moves it into Soledad_Main and crossfades its weight between acts.
    // The weight scales every Volume under the act (fog, volumetrics, colour look), so two acts blend during a
    // transition. Act-specific scripts can read weight to fade their own content.
    [DisallowMultipleComponent]
    public class Act : MonoBehaviour
    {
        [Tooltip("0 = hidden, 1 = fully shown. Set by ShowController during transitions, can be driven by Chataigne.")]
        [Range(0f, 1f)]
        public float weight = 1f;

        Volume[] _volumes;
        float[] _baseWeights;
        float _appliedWeight = -1f;

        void Awake()
        {
            _volumes = GetComponentsInChildren<Volume>(true);
            _baseWeights = new float[_volumes.Length];
            for (int i = 0; i < _volumes.Length; i++)
                _baseWeights[i] = _volumes[i].weight;
        }

        void OnEnable() => _appliedWeight = -1f;

        void Update()
        {
            if (weight == _appliedWeight) return;
            _appliedWeight = weight;
            for (int i = 0; i < _volumes.Length; i++)
                if (_volumes[i] != null)
                    _volumes[i].weight = _baseWeights[i] * weight;
        }
    }
}

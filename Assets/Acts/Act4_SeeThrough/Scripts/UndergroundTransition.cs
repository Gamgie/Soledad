using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soledad
{
    /// <summary>
    /// Act 4, going under the ground: two [Animate] steps, driven from Chataigne or a Timeline (Show/Act4Controller).
    /// <see cref="blackSky"/> is the weight of the underground Volume (its sky multiplier, cloud density and fog tint go
    /// to 0: the sky fades to black). <see cref="groundFade"/> dissolves the hill (its grass shells too) and the
    /// landscape in noisy patches (Soledad/HillGrass reads the globals _GroundDissolve and _GroundDissolveScale); fully
    /// faded, their renderers and the shell drawing are switched off in Play mode (nothing left to draw or shadow).
    /// Also previewed in the Editor. Runs after Act, which sets every volume's weight when the act weight changes.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(100)]
    public class UndergroundTransition : MonoBehaviour
    {
        [Animate] [Tooltip("Sky to black: 0 = the day sky, 1 = black (the underground Volume's weight).")]
        [Range(0f, 1f)] public float blackSky = 0f;
        [Animate] [Tooltip("Hill and landscape: 0 = whole, 1 = dissolved (switched off in Play mode).")]
        [Range(0f, 1f)] public float groundFade = 0f;

        [Tooltip("Size of the dissolving patches (m).")]
        public float dissolveScale = 6f;
        [Tooltip("The Volume that blacks the sky (global, weight 0 in the scene).")]
        public Volume undergroundVolume;
        [Tooltip("What dissolves: the hill and the landscape (their renderers and grass shells).")]
        public GameObject[] ground;

        static readonly int GroundDissolveId = Shader.PropertyToID("_GroundDissolve");
        static readonly int GroundDissolveScaleId = Shader.PropertyToID("_GroundDissolveScale");

        Act _act;
        bool _groundOff;
        readonly List<Renderer> _renderers = new List<Renderer>();
        readonly List<HillGrass> _grass = new List<HillGrass>();

        void OnEnable()
        {
            _act = GetComponentInParent<Act>();
            _groundOff = false;
        }

        void OnDisable()
        {
            Shader.SetGlobalFloat(GroundDissolveId, 0f);
            if (undergroundVolume != null) undergroundVolume.weight = 0f;
            if (_groundOff) SetGround(true);
        }

        void Update()
        {
            float act = Application.isPlaying && _act != null ? _act.weight : 1f;
            if (undergroundVolume != null)
            {
                float w = Mathf.Clamp01(blackSky) * act;
                if (undergroundVolume.weight != w) undergroundVolume.weight = w;
            }

            float fade = Mathf.Clamp01(groundFade);
            Shader.SetGlobalFloat(GroundDissolveId, fade);
            Shader.SetGlobalFloat(GroundDissolveScaleId, dissolveScale);

            if (Application.isPlaying)
            {
                bool off = fade >= 0.999f;
                if (off != _groundOff) SetGround(!off);
            }
        }

        // Switches the ground's renderers and shell drawing (only when the state changes)
        void SetGround(bool on)
        {
            _groundOff = !on;
            if (ground == null) return;
            foreach (var g in ground)
            {
                if (g == null) continue;
                g.GetComponentsInChildren(true, _renderers);
                foreach (var r in _renderers) r.enabled = on;
                g.GetComponentsInChildren(true, _grass);
                foreach (var s in _grass) s.enabled = on;
            }
        }
    }
}

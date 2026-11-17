using Oxipital;
using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// Act 4: the scanned tree, an Oxipital Orb Group (infinite life, custom mesh "Tree_AF" from
    /// StreamingAssets/emitters) on the same object, plus the groups in <see cref="alsoDrive"/> (the grafted stems,
    /// "Tree_AF_Stems"). Oxipital loads the emitter meshes asynchronously at Play (the scan takes a few seconds);
    /// infinite particles spawned before that would stay on the default shape forever, so the groups' intensity is held
    /// at 0 until the meshes are loaded (and one more frame, for the groups to take their mesh), then follows
    /// <see cref="density"/> (the fraction of each group's maxInfiniteParticles shown). The grains cast no shadow when
    /// <see cref="grainShadows"/> is off (a light proxy mesh casts the tree's shadow instead): drawn again in every
    /// shadow cascade, they were most of the tree's GPU cost.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(OrbGroup))]
    public class TreeEmitter : MonoBehaviour
    {
        [Animate] [Tooltip("Fraction of the tree's particles shown (0 = none, 1 = maxInfiniteParticles of each Orb Group).")]
        [Range(0f, 1f)] public float density = 1f;
        [Tooltip("Other Orb Groups of the same tree, driven together (the grafted stems).")]
        public OrbGroup[] alsoDrive;
        [Tooltip("Let the grains cast shadows (expensive: every grain again in every shadow cascade). Off: the Shadow Proxy casts them.")]
        public bool grainShadows = false;

        OrbGroup _group;
        int _framesLoaded;

        void OnEnable()
        {
            _group = GetComponent<OrbGroup>();
            _framesLoaded = 0;
            ApplyShadows();
        }

        void OnValidate() => ApplyShadows();

        void ApplyShadows()
        {
            var mode = grainShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            SetShadows(GetComponent<UnityEngine.VFX.VFXRenderer>(), mode);
            if (alsoDrive != null)
                foreach (var g in alsoDrive)
                    if (g != null) SetShadows(g.GetComponent<UnityEngine.VFX.VFXRenderer>(), mode);
        }

        static void SetShadows(Renderer r, UnityEngine.Rendering.ShadowCastingMode mode)
        {
            if (r != null && r.shadowCastingMode != mode) r.shadowCastingMode = mode;
        }

        void Update()
        {
            if (!Application.isPlaying || _group == null) return;
            if (MeshLoader.isLoaded) _framesLoaded++;
            float target = _framesLoaded > 1 ? density : 0f;
            Drive(_group, target);
            if (alsoDrive != null)
                foreach (var g in alsoDrive)
                    if (g != null) Drive(g, target);
        }

        static void Drive(OrbGroup g, float target)
        {
            if (g.dancerIntensity != target) g.dancerIntensity = target;
        }
    }
}

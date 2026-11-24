using Oxipital;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

namespace Soledad
{
    // Root of an act scene. ShowController moves it into Soledad_Main and crossfades its weight between acts.
    // The weight scales every Volume under the act (fog, volumetrics, colour look), so two acts blend during a
    // transition. Act-specific scripts can read weight to fade their own content.
    // Forces: the Ballet only feeds its force groups to the Orb Groups of its own Orb Groups manager, so the act
    // forwards them (the same buffers, by name: "Force Group 1", ...) to every Orb Group under the act. Each group's
    // forceWeight still says how much the forces move it (0 = not at all).
    [DisallowMultipleComponent]
    public class Act : MonoBehaviour
    {
        [Tooltip("0 = hidden, 1 = fully shown. Set by ShowController during transitions.")]
        [Range(0f, 1f)]
        public float weight = 1f;

        [Tooltip("Feed the Ballet's force groups (Chataigne) to the Orb Groups under this act.")]
        public bool linkForces = true;

        Volume[] _volumes;
        float[] _baseWeights;
        float _appliedWeight = -1f;

        VisualEffect[] _orbVfx;
        Transform _forceRoot;
        StandardForceGroup[] _forces = new StandardForceGroup[0];
        int[] _forceIds = new int[0], _forceIdsB = new int[0];
        int _forceChildCount = -1;

        void Awake()
        {
            _volumes = GetComponentsInChildren<Volume>(true);
            _baseWeights = new float[_volumes.Length];
            for (int i = 0; i < _volumes.Length; i++)
                _baseWeights[i] = _volumes[i].weight;

            var groups = GetComponentsInChildren<OrbGroup>(true);
            _orbVfx = new VisualEffect[groups.Length];
            for (int i = 0; i < groups.Length; i++)
                _orbVfx[i] = groups[i].GetComponent<VisualEffect>();
        }

        void OnEnable() => _appliedWeight = -1f;

        void Update()
        {
            if (linkForces && Application.isPlaying && _orbVfx.Length > 0) LinkForces();

            if (weight == _appliedWeight) return;
            _appliedWeight = weight;
            for (int i = 0; i < _volumes.Length; i++)
                if (_volumes[i] != null)
                    _volumes[i].weight = _baseWeights[i] * weight;
        }

        void LinkForces()
        {
            if (_forceRoot == null)
            {
                var manager = FindFirstObjectByType<StandardForceManager>();
                if (manager == null) return;
                _forceRoot = manager.transform;
                _forceChildCount = -1;
            }
            // The force groups change only when Chataigne changes their count (allocation then only)
            if (_forceRoot.childCount != _forceChildCount) CollectForces();

            for (int f = 0; f < _forces.Length; f++)
            {
                var force = _forces[f];
                if (force == null) { _forceChildCount = -1; continue; }
                var buffer = force.buffer;
                if (buffer == null || !buffer.IsValid()) continue;
                for (int v = 0; v < _orbVfx.Length; v++)
                {
                    var vfx = _orbVfx[v];
                    if (vfx == null) continue;
                    if (vfx.HasGraphicsBuffer(_forceIds[f])) vfx.SetGraphicsBuffer(_forceIds[f], buffer);
                    if (vfx.HasGraphicsBuffer(_forceIdsB[f])) vfx.SetGraphicsBuffer(_forceIdsB[f], buffer);
                }
            }
        }

        void CollectForces()
        {
            _forceChildCount = _forceRoot.childCount;
            _forces = _forceRoot.GetComponentsInChildren<StandardForceGroup>();
            _forceIds = new int[_forces.Length];
            _forceIdsB = new int[_forces.Length];
            for (int i = 0; i < _forces.Length; i++)
            {
                _forceIds[i] = Shader.PropertyToID(_forces[i].gameObject.name);
                _forceIdsB[i] = Shader.PropertyToID(_forces[i].gameObject.name + "B");
            }
        }
    }
}

using UnityEngine;
using UnityEngine.Video;

namespace Soledad
{
    /// <summary>
    /// The phone screen of Act 1: <see cref="Zdepth"/> slides it between <see cref="maxZ"/> (0) and
    /// <see cref="minZ"/> (1) on its local Z, <see cref="alpha"/> fades it. Runs in edit mode too, so the Timeline
    /// preview of Show/Act1Controller shows it. The alpha goes through a MaterialPropertyBlock (_BaseColor), so the
    /// material asset is never edited. Applied only when a value changes. In Play mode the VideoPlayer on the same
    /// object restarts when the phone is enabled and is clocked by game time (External Time, no frame skipping), so a
    /// Recorder render shows the video at the speed of the film.
    /// </summary>
    [ExecuteAlways]
    public class PhoneScreen : MonoBehaviour
    {
        public float minZ, maxZ;
        [Animate] [Range(0,1)] public float Zdepth;
        [Animate] [Range(0,1)] public float alpha;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        MeshRenderer _renderer;
        MaterialPropertyBlock _block;
        float _appliedZ = float.NaN;
        VideoPlayer _video;
        double _videoClock;

        void OnEnable()
        {
            _renderer = GetComponent<MeshRenderer>();
            _block ??= new MaterialPropertyBlock();
            _appliedZ = float.NaN;

            // Play mode: the video follows game time, not its own clock, so a Recorder render (game time stepped one
            // frame at a time, much slower than real time) shows the right image on every frame, and repeats
            _video = GetComponent<VideoPlayer>();
            if (Application.isPlaying && _video != null && _video.clip != null)
            {
                _video.timeReference = VideoTimeReference.ExternalTime;
                _video.skipOnDrop = false;
                _videoClock = 0.0;
                _video.externalReferenceTime = 0.0;
                _video.time = 0.0;
                _video.Play();
            }
        }

        void OnValidate() => _appliedZ = float.NaN;

        void Update()
        {
            if (Application.isPlaying && _video != null && _video.isPlaying)
            {
                _videoClock += Time.deltaTime;
                _video.externalReferenceTime = _videoClock;
            }

            float z = Mathf.Lerp(maxZ, minZ, Zdepth);
            if (z != _appliedZ)
            {
                _appliedZ = z;
                Vector3 p = transform.localPosition;
                if (p.z != z) transform.localPosition = new Vector3(p.x, p.y, z);
            }
        }

        // Every frame, after Update: the VideoPlayer (Material Override) writes the renderer's property block too, so
        // the alpha is set again on top of whatever it holds (its video texture is kept)
        void LateUpdate()
        {
            if (_renderer != null)
            {
                Material material = _renderer.sharedMaterial;
                Color color = material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
                color.a = alpha;
                _renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                _renderer.SetPropertyBlock(_block);
            }
        }
    }
}

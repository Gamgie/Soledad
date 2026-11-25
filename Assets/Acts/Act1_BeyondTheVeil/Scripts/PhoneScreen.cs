using UnityEngine;
using UnityEngine.Video;

namespace Soledad
{
    /// <summary>
    /// The phone screen of Act 1: <see cref="Zdepth"/> slides it between <see cref="maxZ"/> (0) and
    /// <see cref="minZ"/> (1) on its local Z, <see cref="alpha"/> fades it. Runs in edit mode too, so the Timeline
    /// preview of Show/Act1Controller shows it. The alpha goes through a MaterialPropertyBlock (_BaseColor), so the
    /// material asset is never edited. Applied only when a value changes. In Play mode the VideoPlayer on the same
    /// object restarts when the phone is enabled; while the Recorder renders it is paused and sought to the frame of the
    /// game time elapsed, so a
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
        long _videoFrame;

        // While the Recorder renders (capture delta time set), the video is paused and shown at the frame of the game
        // time elapsed since the phone was enabled, by seeking (StepForward did nothing on this clip). Its own clocks
        // (DSP, game time, External Time) all kept running in real time while the Recorder stepped game time much
        // slower: a render played it twice too fast. The seek lands a frame or so later, which a render frame (far
        // longer than real time) leaves room for. Live and preview: the video plays normally on its own clock.
        void StepVideo()
        {
            _videoClock += Time.deltaTime;
            if (Time.captureDeltaTime <= 0f) return;
            if (!_video.isPrepared) return;
            if (_video.isPlaying) _video.Pause();

            long last = (long)_video.frameCount - 1;
            long target = (long)(_videoClock * _video.frameRate);
            if (target > last) target = last;
            if (target == _videoFrame) return;
            _video.frame = target;
            _videoFrame = target;
        }

        void OnEnable()
        {
            _renderer = GetComponent<MeshRenderer>();
            _block ??= new MaterialPropertyBlock();
            _appliedZ = float.NaN;

            // Play mode: the video starts from its beginning when the phone is enabled (see StepVideo for renders)
            _video = GetComponent<VideoPlayer>();
            _videoClock = 0.0;
            _videoFrame = -1;
            if (Application.isPlaying && _video != null && _video.clip != null)
            {
                _video.Stop();
                _video.skipOnDrop = false;
                _video.time = 0.0;
                _video.Play();
            }
        }

        void OnValidate() => _appliedZ = float.NaN;

        void Update()
        {
            if (Application.isPlaying && _video != null && _video.clip != null)
                StepVideo();

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

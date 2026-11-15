using UnityEngine;
using UnityEngine.Playables;

namespace Soledad
{
    /// <summary>
    /// Starts a film Timeline (PlayableDirector, Play On Awake off) in step with the Spline camera: in Play mode, once
    /// the act has handed its spline to the camera, it switches the camera to Spline, puts the ride at
    /// <see cref="startDistance"/> at full speed, then plays the director. The Timeline's keys are then in time with
    /// distances along the path (speed x time), and a Recorder render repeats exactly.
    /// </summary>
    [OSCQuery.DoNotExpose]
    public class FilmTimeline : MonoBehaviour
    {
        public PlayableDirector director;
        public Oxipital.CameraController cameraController;
        [Tooltip("Distance along the act's path (m) where the shot starts.")]
        public float startDistance;
        [Tooltip("Start by itself when entering Play mode. Off: call Play() (or play the director by hand).")]
        public bool playOnStart = true;

        bool _started;

        void Update()
        {
            if (_started || !playOnStart) return;
            var spline = cameraController != null ? cameraController.splineCamera : null;
            if (spline == null || spline.spline == null) return;      // the act has not handed its path yet
            Play();
        }

        /// <summary>Ride from startDistance and play the Timeline from its start.</summary>
        public void Play()
        {
            var spline = cameraController != null ? cameraController.splineCamera : null;
            if (spline == null || director == null) return;
            _started = true;
            cameraController.cameraType = Oxipital.CameraController.CameraMovementType.Spline;
            spline.startDistance = startDistance;
            spline.ResetRide();
            director.time = 0;
            director.Play();
        }
    }
}

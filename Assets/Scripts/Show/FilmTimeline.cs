using UnityEngine;
using UnityEngine.Playables;

namespace Soledad
{
    /// <summary>
    /// The film Timeline of one act (PlayableDirector, Play On Awake off, on a child of Film in Soledad_Main). In Play
    /// mode, when <see cref="Film.playTimelines"/> is on and its act is shown at full weight (the act scene opened next
    /// to Soledad_Main is shown on Play), it plays the director from its start. With <see cref="rideSpline"/> it first
    /// waits for the act to hand its path to the Spline camera, switches the camera to Spline and puts the ride at
    /// <see cref="startDistance"/>, so a ride by speed is in time with the keys and a Recorder render repeats exactly.
    /// The director follows the audio clock (DSP) in a preview, so the picture stays on the sound whatever the frame
    /// rate, and game time when <see cref="Film.record"/> is on (the Recorder steps it one frame at a time).
    /// With <see cref="rideSpline"/> the first image is already at the Timeline's start, by a cut (see LateUpdate).
    /// </summary>
    [OSCQuery.DoNotExpose]
    [DefaultExecutionOrder(-50)]        // before the cameras' Update, and LateUpdate before CinemachineBrain's (100)
    public class FilmTimeline : MonoBehaviour
    {
        public PlayableDirector director;
        public ShowController show;
        [Tooltip("Act of this Timeline (1 to 5): it plays once that act is shown.")]
        [Range(1, 5)] public int act = 1;
        public Oxipital.CameraController cameraController;

        [Tooltip("Wait for the act's path, switch the camera to Spline and restart its ride before playing.")]
        public bool rideSpline;
        [Tooltip("Distance along the act's path (m) where the shot starts, for a ride by speed. A Timeline that rides by progress places the camera itself.")]
        public float startDistance;

        Film _film;
        bool _started;

        void Start()
        {
            _film = GetComponentInParent<Film>();
        }

        void Update()
        {
            if (_started || _film == null || !_film.playTimelines) return;
            if (show == null || show.ShownAct != act) return;
            if (rideSpline)
            {
                var spline = cameraController != null ? cameraController.splineCamera : null;
                if (spline == null || spline.spline == null) return;      // the act has not handed its path yet
            }
            Play();
        }

        /// <summary>Play the Timeline from its start (ride restarted first with <see cref="rideSpline"/>).</summary>
        public void Play()
        {
            if (director == null) return;
            _started = true;
            if (rideSpline && cameraController != null && cameraController.splineCamera != null)
            {
                var spline = cameraController.splineCamera;
                cameraController.cameraType = Oxipital.CameraController.CameraMovementType.Spline;
                spline.startDistance = startDistance;
                spline.ResetRide();
            }
            // Preview: the Timeline follows the audio clock, so a slow or long frame (act loading, Oxipital meshes)
            // drops images instead of putting the picture behind the sound (game time loses every frame longer than
            // Maximum Allowed Timestep). Recording: game time, which the Recorder steps by exactly one frame.
            director.timeUpdateMode = _film != null && _film.record ? DirectorUpdateMode.GameTime : DirectorUpdateMode.DSPClock;
            director.time = 0;
            // The keys at 0 (the Spline camera's progress, offset, look...) applied now: the director itself only
            // evaluates after the Updates, and the camera would place itself this frame from the scene's values
            director.Evaluate();
            director.Play();
            _cutPending = rideSpline;
        }

        bool _cutPending;

        // The frame the Timeline starts, once the cameras have updated and before Cinemachine places the main camera:
        // the Spline camera at the Timeline's start, shown by a cut. Otherwise the first image came from the camera the
        // brain had picked before Oxipital raised the Spline camera's priority (the Orbital one, at the origin), then an
        // 8 s blend (cameraTransitionDuration) to the path; and the Spline camera itself only reached the start a
        // frame later (it rides in its Update, before the Timeline's values of the frame are applied).
        void LateUpdate()
        {
            if (_cutPending)
            {
                _cutPending = false;
                var spline = cameraController != null ? cameraController.splineCamera : null;
                if (spline != null)
                {
                    if (spline.IsActive) spline.SendMessage("Update", SendMessageOptions.DontRequireReceiver);   // ride now
                    if (spline.virtualCamera != null) spline.virtualCamera.PreviousStateIsValid = false;         // no damping
                    if (cameraController.cinemachineBrain != null) cameraController.cinemachineBrain.ResetState(); // no blend
                }
            }
        }
    }
}

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

        [Tooltip("Preview only: steady frames to wait for before playing, once Oxipital's meshes are loaded, the camera held at the Timeline's start. On the audio clock the loading hitches counted: the preview started 2.5 s in, then skipped 1.6 s when the meshes came.")]
        [Min(0)] public int steadyFrames = 10;
        [Tooltip("Preview only: longest real frame time (s) counted as steady.")]
        [Min(0.01f)] public float steadyFrameTime = 0.1f;

        const double MaxWait = 20.0;        // a machine that never runs steady starts anyway

        Film _film;
        bool _started, _waiting, _record, _cutPending;
        int _steady;
        double _lastRealTime, _waitStart;

        void Start()
        {
            _film = GetComponentInParent<Film>();
        }

        void Update()
        {
            if (_waiting)
            {
                if (Ready())
                {
                    _waiting = false;
                    director.Play();
                }
                return;
            }
            if (_started || _film == null || !_film.playTimelines) return;
            if (show == null || show.ShownAct != act) return;
            if (rideSpline)
            {
                var spline = cameraController != null ? cameraController.splineCamera : null;
                if (spline == null || spline.spline == null) return;      // the act has not handed its path yet
            }
            Play();
        }

        // True once Oxipital's emitter meshes are loaded (asynchronous at Play; their arrival is a long frame, and the
        // Orb Groups spawned before it sit on the default shape), then in a preview steadyFrames frames in a row took
        // at most steadyFrameTime of real time
        bool Ready()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - _waitStart > MaxWait) return true;
            bool loaded = Oxipital.MeshLoader.isLoaded;
            if (_record) return loaded;
            _steady = loaded && now - _lastRealTime <= steadyFrameTime ? _steady + 1 : 0;
            _lastRealTime = now;
            return _steady >= steadyFrames;
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
            _record = _film != null && _film.record;
            director.timeUpdateMode = _record ? DirectorUpdateMode.GameTime : DirectorUpdateMode.DSPClock;
            director.time = 0;
            // The keys at 0 (the Spline camera's progress, offset, look...) applied now: the director itself only
            // evaluates after the Updates, and the camera would place itself this frame from the scene's values
            director.Evaluate();
            _cutPending = rideSpline;
            // Held at 0 until Oxipital's meshes are loaded and, in a preview, the frames are steady: the first frames
            // after the act shows are long, and the audio clock counts them. A Recorder render steps game time: it
            // only waits for the meshes.
            _waiting = true;
            _steady = 0;
            _lastRealTime = _waitStart = Time.realtimeSinceStartupAsDouble;
        }

        // The frame the Timeline starts, once the cameras have updated and before Cinemachine places the main camera:
        // the Spline camera at the Timeline's start, shown by a cut. Otherwise the first image came from the camera the
        // brain had picked before Oxipital raised the Spline camera's priority (the Orbital one, at the origin), then an
        // 8 s blend (cameraTransitionDuration) to the path; and the Spline camera itself only reached the start a
        // frame later (it rides in its Update, before the Timeline's values of the frame are applied).
        void LateUpdate()
        {
            if (!_cutPending) return;
            _cutPending = false;
            var spline = cameraController != null ? cameraController.splineCamera : null;
            if (spline == null) return;
            // The ride's state again, now that the Timeline's keys are applied: offset (otherwise smoothed from the
            // scene's value), turn rate and look.
            spline.ResetRide();
            // A ride by progress takes its direction of travel from the change of distance. The ride of this frame's
            // Update came from the distance the act left at load (1.7 km on): it read a move backwards and the
            // camera looked behind (progress 0 does not move, so it stayed so), then turned round as the progress
            // rose, a turn the bank took for 900 degrees/s: the camera rolled 60 degrees and took 3 s to level.
            // From just below the start, the first ride reads a move forwards.
            if (spline.useProgress) spline.distance = -1f;
            if (spline.IsActive) spline.SendMessage("Update", SendMessageOptions.DontRequireReceiver);   // ride now
            var brain = cameraController.cinemachineBrain;
            // No damping: one update with a negative deltaTime puts the camera on the target, so the damped follow
            // and rotation (Rotation Damping 3.9) start from there. PreviousStateIsValid = false was not enough: the
            // components only skip their damping on a negative deltaTime, and the first image was 6 degrees off.
            if (spline.virtualCamera != null)
                spline.virtualCamera.InternalUpdateCameraState(brain != null ? brain.DefaultWorldUp : Vector3.up, -1f);
            if (brain != null) brain.ResetState();                                                       // no blend
        }
    }
}

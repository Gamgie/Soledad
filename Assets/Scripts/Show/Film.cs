using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Soledad
{
    /// <summary>
    /// The film's switches, on the Film object of Soledad_Main (outside Root Scene: not in Chataigne), parent of the
    /// five act Timelines (<see cref="FilmTimeline"/>).
    ///
    /// <see cref="format"/> is the output of the whole project, show included: Flat (16:9, main camera) or Dome (the
    /// Dome Creator renders the DomeMaster). It sets <c>CameraController.isFullDome</c> (read once, when the camera
    /// controller is enabled, so set it before Play), the Dome Creator and the main camera, and in every act Timeline
    /// mutes the camera group of the other format ("Camera 16:9" / "Camera Dome"): the camera can be keyed apart for
    /// each format while the act's own tracks (group "Show") are shared.
    ///
    /// <see cref="record"/> unmutes the Recorder track of each camera group: Play then renders the shown act's
    /// Timeline to image sequences. Off, Play is a preview and nothing is written.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-1000)]      // before CameraController.OnEnable, which reads isFullDome
    [OSCQuery.DoNotExpose]
    public class Film : MonoBehaviour
    {
        public enum Format { Flat, Dome }

        public const string FlatCameraGroup = "Camera 16:9";
        public const string DomeCameraGroup = "Camera Dome";
        public const string ShowGroup = "Show";

        [Tooltip("Output format of the project (show and film): Flat 16:9 or Dome. Set it before Play.")]
        public Format format = Format.Flat;
        [Tooltip("Play mode plays the Timeline of the shown act (open the act scene next to Soledad_Main). Off for the live show.")]
        public bool playTimelines = true;
        [Tooltip("Unmute the Recorder tracks: Play renders the shown act's Timeline to Recordings/. Off: preview only.")]
        public bool record;

        public Oxipital.CameraController cameraController;
        [Tooltip("The Dome Creator root (pfc dome-tools).")]
        public GameObject domeCreator;

        void Awake() => Apply();

#if UNITY_EDITOR
        void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) Apply(); };
        }
#endif

        /// <summary>Applies the format and the record switch to the cameras and to every act Timeline.</summary>
        public void Apply()
        {
            bool dome = format == Format.Dome;
            if (cameraController != null)
            {
                if (cameraController.isFullDome != dome)
                {
                    cameraController.isFullDome = dome;
                    MarkDirty(cameraController);
                }
                var camera = cameraController.camera;
                if (camera != null && camera.enabled == dome)
                {
                    camera.enabled = !dome;
                    MarkDirty(camera);
                }
            }
            if (domeCreator != null && domeCreator.activeSelf != dome)
            {
                domeCreator.SetActive(dome);
                MarkDirty(domeCreator);
            }

            foreach (var timeline in GetComponentsInChildren<FilmTimeline>(true))
                ApplyTo(timeline.director, dome);
        }

        void ApplyTo(PlayableDirector director, bool dome)
        {
            var asset = director != null ? director.playableAsset as TimelineAsset : null;
            if (asset == null) return;

            bool changed = false;
            foreach (var track in asset.GetRootTracks())
            {
                if (!(track is GroupTrack)) continue;
                bool isFlat = track.name == FlatCameraGroup, isDome = track.name == DomeCameraGroup;
                if (!isFlat && !isDome) continue;
                changed |= SetMuted(track, isFlat ? dome : !dome);
                foreach (var child in track.GetChildTracks())
                    if (child.GetType().Name == "RecorderTrack")
                        changed |= SetMuted(child, !record);
            }
            if (!changed) return;
            MarkDirty(asset);
            if (director.playableGraph.IsValid()) director.RebuildGraph();
        }

        static bool SetMuted(TrackAsset track, bool muted)
        {
            if (track.muted == muted) return false;
            track.muted = muted;
            MarkDirty(track);
            return true;
        }

        static void MarkDirty(Object target)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(target);
#endif
        }
    }
}

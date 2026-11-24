using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.Recorder.Timeline;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace Soledad
{
    /// <summary>
    /// Soledad > Build Film Timelines (Soledad_Main open): makes sure Soledad_Main has the Film object with one
    /// Timeline per act, <c>Assets/Acts/&lt;ActScene&gt;/Timeline/ActN_Timeline.playable</c>, each with three groups:
    /// "Show" (an Animation track on Show, which drives Show/ActNController), "Camera 16:9" and "Camera Dome" (an
    /// Animation track on Cameras/Camera Controller and a Recorder track each). Existing content is kept: root tracks
    /// bound to Show move into "Show", other root Animation tracks (camera) move into "Camera 16:9" and are copied into
    /// "Camera Dome" when it has no track on the same object. Run it again after changing a Timeline's length: the
    /// Recorder clips are stretched to the whole Timeline.
    /// </summary>
    static class FilmTimelineBuilder
    {
        const double DefaultDuration = 60;
        const double FrameRate = 60;
        const int FlatWidth = 3840, FlatHeight = 2160;
        const string DomeOutputTexture = "Assets/Textures/Dome Output Texture.asset";

        [MenuItem("Soledad/Build Film Timelines")]
        static void Build()
        {
            var show = Object.FindFirstObjectByType<ShowController>(FindObjectsInactive.Include);
            if (show == null)
            {
                Debug.LogError("Film timelines: open Soledad_Main first.");
                return;
            }
            var scene = show.gameObject.scene;
            var cameraController = Object.FindFirstObjectByType<Oxipital.CameraController>(FindObjectsInactive.Include);
            var domeCreator = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Dome Creator");
            var domeOutput = AssetDatabase.LoadAssetAtPath<RenderTexture>(DomeOutputTexture);

            var film = Object.FindFirstObjectByType<Film>(FindObjectsInactive.Include);
            if (film == null)
            {
                var go = new GameObject("Film");
                SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Build Film Timelines");
                film = go.AddComponent<Film>();
            }
            Undo.RecordObject(film, "Build Film Timelines");
            film.cameraController = cameraController;
            film.domeCreator = domeCreator;
            if (domeCreator != null && domeCreator.activeSelf) film.format = Film.Format.Dome;

            var showAnimator = Ensure<Animator>(show.gameObject);
            var cameraAnimator = cameraController != null ? Ensure<Animator>(cameraController.gameObject) : null;

            var existing = Object.FindObjectsByType<FilmTimeline>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int act = 1; act <= show.actScenes.Length; act++)
            {
                string actScene = show.actScenes[act - 1];
                string folder = $"Assets/Acts/{actScene}/Timeline";
                string path = $"{folder}/Act{act}_Timeline.playable";
                var asset = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
                if (asset == null)
                {
                    if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder($"Assets/Acts/{actScene}", "Timeline");
                    asset = ScriptableObject.CreateInstance<TimelineAsset>();
                    asset.editorSettings.frameRate = FrameRate;
                    asset.durationMode = TimelineAsset.DurationMode.FixedLength;
                    asset.fixedDuration = DefaultDuration;
                    AssetDatabase.CreateAsset(asset, path);
                }

                var timeline = existing.FirstOrDefault(t => t.director != null && t.director.playableAsset == asset);
                if (timeline == null)
                {
                    var go = new GameObject($"Act{act}_Timeline");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    Undo.RegisterCreatedObjectUndo(go, "Build Film Timelines");
                    timeline = go.AddComponent<FilmTimeline>();
                    timeline.director = go.AddComponent<PlayableDirector>();
                }
                Undo.SetTransformParent(timeline.transform, film.transform, "Build Film Timelines");
                timeline.transform.SetSiblingIndex(act - 1);
                var director = timeline.director;
                Undo.RecordObject(director, "Build Film Timelines");
                director.playableAsset = asset;
                director.playOnAwake = false;
                director.extrapolationMode = DirectorWrapMode.Hold;
                Undo.RecordObject(timeline, "Build Film Timelines");
                timeline.show = show;
                timeline.act = act;
                timeline.cameraController = cameraController;

                BuildTracks(asset, director, act, actScene, showAnimator, cameraAnimator, domeOutput);
                EditorUtility.SetDirty(asset);
            }

            film.Apply();
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"Film timelines: {show.actScenes.Length} act Timelines under {film.name} ({film.format}). Save {scene.name}.", film);
        }

        static void BuildTracks(TimelineAsset asset, PlayableDirector director, int act, string actScene,
            Animator showAnimator, Animator cameraAnimator, RenderTexture domeOutput)
        {
            var showGroup = Group(asset, Film.ShowGroup, 0);
            var flatGroup = Group(asset, Film.FlatCameraGroup, 1);
            var domeGroup = Group(asset, Film.DomeCameraGroup, 2);

            // Earlier root tracks: the act's into Show, the camera's into Camera 16:9
            foreach (var track in asset.GetRootTracks().ToList())
            {
                if (!(track is AnimationTrack)) continue;
                var bound = director.GetGenericBinding(track) as Animator;
                Reparent(asset, track, bound == showAnimator ? showGroup : flatGroup);
            }

            if (!showGroup.GetChildTracks().Any(t => director.GetGenericBinding(t) == showAnimator))
                Bind(director, asset.CreateTrack<AnimationTrack>(showGroup, $"Act {act} (Show/Act{act}Controller)"), showAnimator);

            // Each camera group: the camera tracks of the other one (copied), Camera Controller, Recorder
            foreach (var track in flatGroup.GetChildTracks().OfType<AnimationTrack>().ToList())
            {
                var bound = director.GetGenericBinding(track);
                if (bound != null && !domeGroup.GetChildTracks().Any(t => director.GetGenericBinding(t) == bound))
                    Bind(director, CopyAnimationTrack(asset, track, domeGroup), bound);
            }
            foreach (var group in new[] { flatGroup, domeGroup })
            {
                if (cameraAnimator != null && !group.GetChildTracks().Any(t => director.GetGenericBinding(t) == cameraAnimator))
                    Bind(director, asset.CreateTrack<AnimationTrack>(group, "Camera Controller"), cameraAnimator);
                Recorder(asset, group, act, actScene, group == domeGroup, domeOutput);
            }
        }

        static GroupTrack Group(TimelineAsset asset, string name, int index)
        {
            var group = asset.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == name)
                        ?? asset.CreateTrack<GroupTrack>(null, name);
            // Keep the groups first, in order
            var so = new SerializedObject(asset);
            var tracks = so.FindProperty("m_Tracks");
            for (int i = 0; i < tracks.arraySize; i++)
                if (tracks.GetArrayElementAtIndex(i).objectReferenceValue == group && i != index)
                {
                    tracks.MoveArrayElement(i, Mathf.Min(index, tracks.arraySize - 1));
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Invalidate(asset);
                    break;
                }
            return group;
        }

        // Timeline has no public API to move a track into a group: edit the serialized lists.
        static void Reparent(TimelineAsset asset, TrackAsset track, GroupTrack group)
        {
            var root = new SerializedObject(asset);
            var tracks = root.FindProperty("m_Tracks");
            for (int i = tracks.arraySize - 1; i >= 0; i--)
                if (tracks.GetArrayElementAtIndex(i).objectReferenceValue == track)
                {
                    tracks.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    tracks.DeleteArrayElementAtIndex(i);
                }
            root.ApplyModifiedPropertiesWithoutUndo();

            var parent = new SerializedObject(group);
            var children = parent.FindProperty("m_Children");
            children.InsertArrayElementAtIndex(children.arraySize);
            children.GetArrayElementAtIndex(children.arraySize - 1).objectReferenceValue = track;
            parent.ApplyModifiedPropertiesWithoutUndo();

            var child = new SerializedObject(track);
            child.FindProperty("m_Parent").objectReferenceValue = group;
            child.ApplyModifiedPropertiesWithoutUndo();

            Invalidate(asset);
            Invalidate(group);
        }

        static AnimationTrack CopyAnimationTrack(TimelineAsset asset, AnimationTrack source, GroupTrack group)
        {
            var copy = asset.CreateTrack<AnimationTrack>(group, source.name);
            copy.trackOffset = source.trackOffset;
            copy.position = source.position;
            copy.rotation = source.rotation;
            foreach (var clip in source.GetClips())
            {
                var sourcePlayable = clip.asset as AnimationPlayableAsset;
                if (sourcePlayable == null || sourcePlayable.clip == null) continue;
                var animation = Object.Instantiate(sourcePlayable.clip);
                animation.name = sourcePlayable.clip.name;
                AssetDatabase.AddObjectToAsset(animation, asset);
                var target = copy.CreateClip(animation);
                var playable = (AnimationPlayableAsset)target.asset;
                EditorUtility.CopySerialized(sourcePlayable, playable);
                playable.clip = animation;
                playable.name = sourcePlayable.name;
                target.start = clip.start;
                target.duration = clip.duration;
                target.clipIn = clip.clipIn;
                target.timeScale = clip.timeScale;
                target.displayName = clip.displayName;
                SetInternal(target, nameof(TimelineClip.preExtrapolationMode), clip.preExtrapolationMode);
                SetInternal(target, nameof(TimelineClip.postExtrapolationMode), clip.postExtrapolationMode);
                target.easeInDuration = clip.easeInDuration;
                target.easeOutDuration = clip.easeOutDuration;
            }
            if (source.infiniteClip != null)
            {
                var infinite = Object.Instantiate(source.infiniteClip);
                infinite.name = source.infiniteClip.name;
                AssetDatabase.AddObjectToAsset(infinite, asset);
                var so = new SerializedObject(copy);
                so.FindProperty("m_InfiniteClip").objectReferenceValue = infinite;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return copy;
        }

        // One Recorder track per camera group, one clip over the whole Timeline: 16:9 records the main camera at
        // 3840 x 2160, Dome records the DomeMaster render texture. EXR (half float, sRGB, Zip) for grading in Resolve,
        // into Recordings/<ActScene>/<format>/ (not in git).
        static void Recorder(TimelineAsset asset, GroupTrack group, int act, string actScene, bool dome, RenderTexture domeOutput)
        {
            var track = group.GetChildTracks().OfType<RecorderTrack>().FirstOrDefault()
                        ?? asset.CreateTrack<RecorderTrack>(group, "Recorder");
            var clip = track.GetClips().FirstOrDefault();
            if (clip == null)
            {
                clip = track.CreateClip<RecorderClip>();
                string format = dome ? "Dome" : "16x9";
                var settings = ScriptableObject.CreateInstance<ImageRecorderSettings>();
                settings.name = clip.displayName = $"Act{act} {format}";
                settings.OutputFormat = ImageRecorderSettings.ImageRecorderOutputFormat.EXR;
                settings.EXRCompression = CompressionUtility.EXRCompressionType.Zip;
                settings.OutputColorSpace = ImageRecorderSettings.ColorSpaceType.sRGB_sRGB;
                settings.CaptureAlpha = false;
                if (dome)
                    settings.imageInputSettings = new RenderTextureInputSettings { RenderTexture = domeOutput };
                else
                    settings.imageInputSettings = new CameraInputSettings
                    {
                        Source = ImageSource.MainCamera,
                        OutputWidth = FlatWidth,
                        OutputHeight = FlatHeight,
                        CaptureUI = false,
                    };
                settings.OutputFile = $"Recordings/{actScene}/{format}/Act{act}_{format}_{DefaultWildcard.Frame}";
                settings.FrameRatePlayback = FrameRatePlayback.Constant;
                settings.FrameRate = (float)asset.editorSettings.frameRate;
                settings.CapFrameRate = true;
                AssetDatabase.AddObjectToAsset(settings, asset);
                ((RecorderClip)clip.asset).settings = settings;
            }
            clip.start = 0;
            clip.duration = asset.duration > 0 ? asset.duration : DefaultDuration;
        }

        static void Bind(PlayableDirector director, TrackAsset track, Object target)
        {
            Undo.RecordObject(director, "Build Film Timelines");
            director.SetGenericBinding(track, target);
        }

        static T Ensure<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(go);
        }

        // Timeline keeps some setters internal (clip extrapolation)
        static void SetInternal(object target, string property, object value)
        {
            target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        static void Invalidate(Object timelineObject)
        {
            timelineObject.GetType().GetMethod("Invalidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(timelineObject, null);
        }
    }
}

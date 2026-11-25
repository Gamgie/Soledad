using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Soledad
{
    /// <summary>
    /// Writes the takes of <see cref="TakeRecorder"/>: one Animation clip per recorded object (keys reduced within
    /// the recorder's tolerance, smooth tangents taken from the recording), saved in the take folder, and on the
    /// Timeline a group "&lt;takeName&gt; Take NN" with one Animation track per object, named by the object's path in the
    /// scene, bound to an Animator on it (added if missing). Takes are written in Play mode: the Timeline asset keeps
    /// them, but the director's bindings and the Animators added meanwhile are scene changes Unity drops when Play
    /// stops, so they are made again in edit mode (<see cref="RepairBindings"/>, also menu
    /// Soledad > Takes > Repair Take Bindings): save the scene afterwards.
    /// </summary>
    [InitializeOnLoad]
    static class TakeWriter
    {
        static TakeWriter()
        {
            TakeRecorder.Writer = Write;
            // Before and after Play (the edit-mode scenes are back when EnteredEditMode is raised), when a scene opens,
            // and after a script reload (scenes ready on the next editor update). In Play, TakeRecorder binds what is
            // still missing for the session.
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.ExitingEditMode)
                    RepairBindings();
            };
            EditorSceneManager.sceneOpened += (scene, mode) => RepairBindings();
            EditorApplication.delayCall += RepairBindings;
        }

        static void Write(TakeData data)
        {
            var recorder = data.recorder;
            var director = data.director;
            var timeline = director != null ? director.playableAsset as TimelineAsset : null;
            string timelinePath = timeline != null ? AssetDatabase.GetAssetPath(timeline) : null;

            string folder = !string.IsNullOrWhiteSpace(recorder.takeFolder) ? recorder.takeFolder.TrimEnd('/')
                          : !string.IsNullOrEmpty(timelinePath) ? Path.GetDirectoryName(timelinePath).Replace('\\', '/') + "/Takes"
                          : "Assets/Takes";
            EnsureFolder(folder);

            int number = NextTakeNumber(timeline, recorder.takeName, folder);
            string label = $"{recorder.takeName} Take {number:00}";
            string prefix = (timeline != null ? timeline.name + "_" : "") + FileName(recorder.takeName) + $"_Take{number:00}";
            float frameRate = timeline != null ? (float)timeline.editorSettings.frameRate : 60f;

            GroupTrack group = null;
            if (timeline != null)
            {
                GroupTrack parent = null;
                if (!string.IsNullOrEmpty(recorder.timelineGroup))
                {
                    parent = timeline.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == recorder.timelineGroup);
                    if (parent == null) Debug.LogWarning($"Take recorder: no group '{recorder.timelineGroup}' in {timeline.name}, take put at the root.", recorder);
                }
                group = timeline.CreateTrack<GroupTrack>(parent, label);
            }

            double length = data.end - data.start;
            var times = new double[data.times.Count];
            for (int i = 0; i < times.Length; i++) times[i] = data.times[i] - data.start;

            var written = new List<string>();
            int keys = 0, samples = 0;
            foreach (var pair in data.objects)
            {
                var go = pair.Key;
                if (go == null) continue;
                var clip = new AnimationClip { name = $"{prefix}_{FileName(go.name)}", frameRate = frameRate };
                foreach (var curve in pair.Value)
                {
                    var animationCurve = Build(curve, times, length, curve.angle ? recorder.angleTolerance : recorder.tolerance);
                    keys += animationCurve.length;
                    samples += curve.constant ? 2 : times.Length;
                    var binding = curve.discrete ? EditorCurveBinding.DiscreteCurve("", curve.type, curve.property)
                                                 : EditorCurveBinding.FloatCurve("", curve.type, curve.property);
                    AnimationUtility.SetEditorCurve(clip, binding, animationCurve);
                }
                string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{clip.name}.anim");
                AssetDatabase.CreateAsset(clip, path);
                written.Add(path);

                if (timeline == null) continue;
                string objectPath = ScenePath(go);
                var track = timeline.CreateTrack<AnimationTrack>(group, objectPath);
                track.trackOffset = TrackOffset.ApplyTransformOffsets;      // absolute: no offset (position 0, rotation identity)
                var timelineClip = track.CreateClip(clip);
                timelineClip.start = data.start;
                timelineClip.duration = length;
                timelineClip.displayName = label;
                var playable = (AnimationPlayableAsset)timelineClip.asset;
                playable.removeStartOffset = false;                         // keep the recorded positions as they are

                // After its end the take holds its last values (the camera stays where the take left it), unless an
                // earlier take of the same object goes on beyond (punch in). Before its start it gives the hand to the
                // tracks below (other camera work, earlier takes), unless it starts at the very start of the Timeline.
                bool before = data.start > 0.1 || Covered(timeline, track, objectPath, data.start - 0.01);
                bool after = Covered(timeline, track, objectPath, data.end + 0.01);
                SetInternal(timelineClip, nameof(TimelineClip.preExtrapolationMode), before ? TimelineClip.ClipExtrapolation.None : TimelineClip.ClipExtrapolation.Hold);
                SetInternal(timelineClip, nameof(TimelineClip.postExtrapolationMode), after ? TimelineClip.ClipExtrapolation.None : TimelineClip.ClipExtrapolation.Hold);

                var animator = go.GetComponent<Animator>();
                if (animator == null) animator = go.AddComponent<Animator>();   // Play mode: made again in edit mode
                director.SetGenericBinding(track, animator);
            }

            if (timeline != null) EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            string where = timeline != null ? $"{timeline.name} > {(group.parent as TrackAsset)?.name ?? "root"} > {label}" : string.Join(", ", written);
            Debug.Log($"Take recorder: {label}, {data.start:0.00} to {data.end:0.00} s ({data.times.Count} frames, {keys} keys from {samples} samples) in {where}.", recorder);
        }

        // ---- Curves ----

        static AnimationCurve Build(TakeCurve curve, double[] times, double length, float tolerance)
        {
            var result = new AnimationCurve();
            // A discrete (int, enum) key holds the integer's bits, not its value: the Animator copies the bits into the field
            float Key(float value) => curve.discrete ? System.BitConverter.Int32BitsToSingle(Mathf.RoundToInt(value)) : value;

            if (curve.constant)
            {
                result.AddKey(new Keyframe(0f, Key(curve.constantValue)));
                result.AddKey(new Keyframe((float)length, Key(curve.constantValue)));
                SetConstant(result);
                return result;
            }

            var v = curve.values;
            int n = Mathf.Min(times.Length, v.Count);
            if (curve.stepped)
            {
                result.AddKey(new Keyframe(0f, Key(v[0])));
                for (int i = 1; i < n; i++)
                    if (v[i] != v[i - 1]) result.AddKey(new Keyframe((float)times[i], Key(v[i])));
                if (result.keys[result.length - 1].time < (float)length) result.AddKey(new Keyframe((float)length, Key(v[n - 1])));
                SetConstant(result);
                return result;
            }

            // Slopes from the recording (central differences), then keep the fewest keys whose Hermite segments stay
            // within the tolerance of every sample: split each segment at its worst sample until it fits.
            var slope = new float[n];
            for (int i = 0; i < n; i++)
            {
                int a = Mathf.Max(0, i - 1), b = Mathf.Min(n - 1, i + 1);
                double dt = times[b] - times[a];
                slope[i] = dt > 0 ? (float)((v[b] - v[a]) / dt) : 0f;
            }
            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, n - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                if (b - a < 2) continue;
                double dt = times[b] - times[a];
                int worst = -1;
                double worstError = tolerance;
                for (int i = a + 1; i < b; i++)
                {
                    double s = (times[i] - times[a]) / dt, s2 = s * s, s3 = s2 * s;
                    double h = (2 * s3 - 3 * s2 + 1) * v[a] + (s3 - 2 * s2 + s) * dt * slope[a]
                             + (-2 * s3 + 3 * s2) * v[b] + (s3 - s2) * dt * slope[b];
                    double error = System.Math.Abs(h - v[i]);
                    if (error > worstError)
                    {
                        worstError = error;
                        worst = i;
                    }
                }
                if (worst < 0) continue;
                keep[worst] = true;
                stack.Push((a, worst));
                stack.Push((worst, b));
            }

            var keysList = new List<Keyframe>();
            for (int i = 0; i < n; i++)
                if (keep[i])
                    keysList.Add(new Keyframe((float)times[i], v[i], slope[i], slope[i]));
            result.keys = keysList.ToArray();
            for (int i = 0; i < result.length; i++)
            {
                AnimationUtility.SetKeyBroken(result, i, false);
                AnimationUtility.SetKeyLeftTangentMode(result, i, AnimationUtility.TangentMode.Free);
                AnimationUtility.SetKeyRightTangentMode(result, i, AnimationUtility.TangentMode.Free);
            }
            return result;
        }

        static void SetConstant(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }
        }

        // ---- Timeline ----

        static int NextTakeNumber(TimelineAsset timeline, string takeName, string folder)
        {
            int last = 0;
            var pattern = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(takeName) + @" Take (\d+)$");
            if (timeline != null)
                foreach (var track in timeline.GetRootTracks().SelectMany(Descendants))
                {
                    var match = pattern.Match(track.name);
                    if (match.Success) last = Mathf.Max(last, int.Parse(match.Groups[1].Value));
                }
            // Clips left from takes removed from the Timeline keep their numbers
            var file = new System.Text.RegularExpressions.Regex("_" + System.Text.RegularExpressions.Regex.Escape(FileName(takeName)) + @"_Take(\d+)_");
            if (AssetDatabase.IsValidFolder(folder))
                foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
                {
                    var match = file.Match(Path.GetFileName(AssetDatabase.GUIDToAssetPath(guid)));
                    if (match.Success) last = Mathf.Max(last, int.Parse(match.Groups[1].Value));
                }
            return last + 1;
        }

        static IEnumerable<TrackAsset> Descendants(TrackAsset track)
        {
            yield return track;
            foreach (var child in track.GetChildTracks())
                foreach (var t in Descendants(child))
                    yield return t;
        }

        // Is time inside an active (not muted) clip of another take track of the same object, extrapolation included?
        static bool Covered(TimelineAsset timeline, TrackAsset except, string objectPath, double time)
        {
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track == except || track.name != objectPath || track.mutedInHierarchy || !TakeRecorder.IsTakeGroup(track.parent as TrackAsset)) continue;
                foreach (var clip in track.GetClips())
                {
                    double start = clip.preExtrapolationMode == TimelineClip.ClipExtrapolation.None ? clip.start : double.NegativeInfinity;
                    double end = clip.postExtrapolationMode == TimelineClip.ClipExtrapolation.None ? clip.end : double.PositiveInfinity;
                    if (time >= start && time <= end) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Binds the take tracks (Animation tracks of a "... Take NN" group, named by an object's scene path) that have
        /// lost their binding, adding the Animator if missing, with Undo. Edit mode: before and after Play.
        /// </summary>
        [MenuItem("Soledad/Takes/Repair Take Bindings")]
        public static void RepairBindings()
        {
            if (EditorApplication.isPlaying) return;
            int repaired = 0;
            foreach (var director in Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var timeline = director.playableAsset as TimelineAsset;
                if (timeline == null) continue;
                foreach (var track in timeline.GetOutputTracks().OfType<AnimationTrack>())
                {
                    if (!TakeRecorder.IsTakeGroup(track.parent as TrackAsset) || director.GetGenericBinding(track) != null) continue;
                    var go = TakeRecorder.FindTakeObject(director, track.name);
                    if (go == null)
                    {
                        Debug.LogWarning($"Take recorder: {timeline.name} > {track.parent.name} > {track.name}: no such object in the open scenes.", director);
                        continue;
                    }
                    var animator = go.GetComponent<Animator>();
                    if (animator == null) animator = Undo.AddComponent<Animator>(go);
                    Undo.RecordObject(director, "Repair Take Bindings");
                    director.SetGenericBinding(track, animator);
                    EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
                    if (go.scene != director.gameObject.scene) EditorSceneManager.MarkSceneDirty(go.scene);
                    repaired++;
                }
            }
            if (repaired > 0) Debug.Log($"Take recorder: {repaired} take track(s) bound again. Save the scene.");
        }

        // ---- Helpers ----

        public static string ScenePath(GameObject go)
        {
            string path = go.name;
            for (var t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            return path;
        }

        static string FileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // Timeline keeps some setters internal (clip extrapolation)
        static void SetInternal(object target, string property, object value)
        {
            target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }
    }
}

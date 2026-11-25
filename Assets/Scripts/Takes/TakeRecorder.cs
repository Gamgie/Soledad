using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
#if UNITY_EDITOR
using System.Linq.Expressions;
#endif

namespace Soledad
{
    /// <summary>One member of a <see cref="TakeTarget"/> to record.</summary>
    [Serializable]
    public class TakeChannel
    {
        public enum Mode
        {
            [Tooltip("Sampled every frame.")] Record,
            [Tooltip("Sampled every frame as angles in degrees: unwrapped (no jump from 359 to 0), and a Vector3 of Euler angles kept continuous through the poles.")] Angles,
            [Tooltip("Not sampled: the take keys Value over its whole length; Live Value is set when the recording starts.")] Constant,
        }

        [Tooltip("Field of the component (public or serialized: float, int, bool, enum, Vector2/3/4, Color), or on a Transform: position, rotation, scale (local). * = every recordable field of the component.")]
        public string member;
        public Mode mode;
        [Tooltip("Constant: the value keyed over the whole take (bool: 0 or 1, enum: its index).")]
        public float value;
        [Tooltip("Constant: the value set when the recording starts, so the live control takes over (e.g. 0 while flying, 1 in the take).")]
        public float liveValue;
    }

    /// <summary>A component and the members of it a <see cref="TakeRecorder"/> records.</summary>
    [Serializable]
    public class TakeTarget
    {
        public Component component;
        public List<TakeChannel> channels = new List<TakeChannel>();
    }

    /// <summary>One recorded curve, handed to the editor writer.</summary>
    public class TakeCurve
    {
        public Type type;               // component type of the binding
        public string property;         // binding property name (e.g. "m_LocalPosition.x", "fov", "cameraPosition.y")
        public bool stepped;            // bool, int, enum: constant tangents, keys only where it changes
        public bool discrete;           // int, enum: a discrete binding (a float binding writes the float's bits into the field)
        public bool angle;              // degrees: angle tolerance
        public bool constant;           // keyed at constantValue over the whole take
        public float constantValue;
        public List<float> values;      // one per TakeData.times entry
    }

    /// <summary>A finished recording, handed to <see cref="TakeRecorder.Writer"/> (editor).</summary>
    public class TakeData
    {
        public TakeRecorder recorder;
        public PlayableDirector director;
        public double start, end;
        public List<double> times;
        public List<KeyValuePair<GameObject, List<TakeCurve>>> objects = new List<KeyValuePair<GameObject, List<TakeCurve>>>();
    }

    /// <summary>
    /// Records values in Play mode (a camera flown with a gamepad from Chataigne, act parameters...) into Animation
    /// clips placed on the Timeline that is playing, at the Timeline's own time, so a take is replayed, edited in the
    /// Animation window and rendered offline like any keyed animation. Editor only (a build records nothing).
    ///
    /// Start and stop with <see cref="record"/> (exposed to OSCQuery: a gamepad button in Chataigne), StartTake /
    /// StopTake / ToggleTake, or the inspector. The take starts where the Timeline is (punch in at 1:20), stops with
    /// record off, at the end of the Timeline or when Play stops. While recording, every track of the Timeline bound
    /// to a recorded object is muted (previous takes, keyed values), so the live control has the hand; they come back
    /// when it stops. The take is then a group "&lt;takeName&gt; Take NN" under <see cref="timelineGroup"/>, one Animation
    /// track per recorded object (named by its path in the scene), its clips saved in <see cref="takeFolder"/>. A
    /// later take overrides the earlier ones where it has keys: a take covered on both sides by an earlier one holds
    /// nothing outside itself (a punch in), otherwise it holds its first and last values.
    ///
    /// Values are sampled in LateUpdate (after physics interpolation and the Timeline). Times come from the director,
    /// smoothed: on the audio clock (DSPClock) the director's time moves by audio buffers, not by frames.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class TakeRecorder : MonoBehaviour
    {
        [Tooltip("Start or stop a take (Chataigne).")]
        public bool record;

        [OSCQuery.DoNotExpose, Tooltip("Takes are named '<Take Name> Take NN'.")]
        public string takeName = "Take";
        [OSCQuery.DoNotExpose, Tooltip("Timeline to record on. Empty: the director playing when the take starts.")]
        public PlayableDirector director;
        [OSCQuery.DoNotExpose, Tooltip("Root group track of the Timeline that receives the takes. Empty: the root.")]
        public string timelineGroup;
        [OSCQuery.DoNotExpose, Tooltip("Folder of the take clips. Empty: a Takes folder next to the Timeline asset.")]
        public string takeFolder;
        [OSCQuery.DoNotExpose, Tooltip("Largest error kept when reducing keys (units of the value).")]
        public float tolerance = 0.001f;
        [OSCQuery.DoNotExpose, Tooltip("Largest error kept when reducing keys of angles (degrees).")]
        public float angleTolerance = 0.02f;
        [OSCQuery.DoNotExpose, Tooltip("Samples reserved per curve when a take starts (no allocation while recording up to this). 60 fps x 15 min.")]
        public int reservedSamples = 54000;
        [OSCQuery.DoNotExpose]
        public List<TakeTarget> targets = new List<TakeTarget>();

        /// <summary>Set by the editor: writes the take (clips, Timeline tracks, bindings).</summary>
        public static Action<TakeData> Writer;

        public bool IsRecording => _recording;
        /// <summary>Time of the take so far (s), for the inspector.</summary>
        public double RecordedLength => _recording && _times.Count > 0 ? _times[_times.Count - 1] - _times[0] : 0;

        bool _recording;
        PlayableDirector _director;
        double _offset;
        readonly List<double> _times = new List<double>();
        readonly List<Sampler> _samplers = new List<Sampler>();
        readonly List<TrackAsset> _muted = new List<TrackAsset>();

        // Before the Timelines start: take tracks that lost their binding (edit mode not repaired yet) are bound for
        // this Play session (not saved; the editor repair makes it lasting)
        void Start()
        {
            foreach (var d in FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None))
                BindTakeTracks(d);
        }

        /// <summary>
        /// Binds the take tracks of a director that have no binding: Animation tracks of a "... Take NN" group, named by
        /// an object's path in the open scenes, bound to the Animator on that object (added if missing). Returns how
        /// many were bound. Play mode: for this session only.
        /// </summary>
        public static int BindTakeTracks(PlayableDirector director)
        {
            var timeline = director != null ? director.playableAsset as TimelineAsset : null;
            if (timeline == null) return 0;
            int bound = 0;
            foreach (var track in timeline.GetOutputTracks())
            {
                if (!(track is AnimationTrack) || !IsTakeGroup(track.parent as TrackAsset) || director.GetGenericBinding(track) != null) continue;
                var go = FindTakeObject(director, track.name);
                if (go == null) continue;
                var animator = go.GetComponent<Animator>();
                if (animator == null) animator = go.AddComponent<Animator>();
                director.SetGenericBinding(track, animator);
                bound++;
            }
            if (bound > 0 && director.playableGraph.IsValid()) RebuildKeepingTime(director);
            return bound;
        }

        /// <summary>A group holding one take: its name ends with " Take NN".</summary>
        public static bool IsTakeGroup(TrackAsset track) =>
            track is GroupTrack && System.Text.RegularExpressions.Regex.IsMatch(track.name, @" Take \d+$");

        /// <summary>The object a take track is named after (its path from the scene root), in the director's scene first.</summary>
        public static GameObject FindTakeObject(PlayableDirector director, string path)
        {
            var go = FindByPath(director.gameObject.scene, path);
            for (int i = 0; go == null && i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                go = FindByPath(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i), path);
            return go;
        }

        static GameObject FindByPath(UnityEngine.SceneManagement.Scene scene, string path)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            int slash = path.IndexOf('/');
            string root = slash < 0 ? path : path.Substring(0, slash);
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name != root) continue;
                if (slash < 0) return go;
                var child = go.transform.Find(path.Substring(slash + 1));
                if (child != null) return child.gameObject;
            }
            return null;
        }

        void Update()
        {
            if (record != _recording)
            {
                if (record) StartTake();
                else StopTake();
            }
        }

        void LateUpdate()
        {
            if (!_recording) return;
            if (_director == null || (_director.playableAsset != null && _director.time >= _director.duration - 1e-4 && _times.Count > 0))
            {
                if (_director != null) Sample(_director.duration);
                StopTake();
                return;
            }
            Sample(Clock());
        }

        void OnDisable() => StopTake();

        public void ToggleTake()
        {
            if (_recording) StopTake();
            else StartTake();
        }

        /// <summary>Starts a take at the director's current time.</summary>
        public void StartTake()
        {
#if UNITY_EDITOR
            if (_recording) return;
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Take recorder: takes are recorded in Play mode.", this);
                record = false;
                return;
            }
            _director = director != null ? director : PlayingDirector();
            if (_director == null) Debug.LogWarning("Take recorder: no Timeline playing, the take is saved as clips only.", this);

            _samplers.Clear();
            foreach (var target in targets)
                if (target.component != null)
                    foreach (var channel in target.channels)
                        AddSamplers(target.component, channel);
            if (_samplers.Count == 0)
            {
                Debug.LogWarning("Take recorder: nothing to record.", this);
                record = false;
                return;
            }

            // Live control: constants take their live value, the Timeline's tracks on the recorded objects are muted
            foreach (var sampler in _samplers)
                if (sampler.constant)
                    sampler.SetLive();
            MuteTracks();

            _offset = (_director != null ? _director.time : 0) - Time.unscaledTimeAsDouble;
            _times.Clear();
            if (_times.Capacity < reservedSamples) _times.Capacity = reservedSamples;
            foreach (var sampler in _samplers) sampler.Reserve(reservedSamples);
            _recording = true;
            record = true;
            Debug.Log($"Take recorder: recording ({_samplers.Count} members).", this);
#endif
        }

        /// <summary>Stops the take and writes it on the Timeline.</summary>
        public void StopTake()
        {
            if (!_recording) return;
            _recording = false;
            record = false;
            UnmuteTracks();

            if (_times.Count < 2)
            {
                Debug.LogWarning("Take recorder: take too short, nothing written.", this);
                return;
            }
            var data = new TakeData { recorder = this, director = _director, start = _times[0], end = _times[_times.Count - 1], times = new List<double>(_times) };
            foreach (var sampler in _samplers)
            {
                var go = sampler.component.gameObject;
                int index = data.objects.FindIndex(p => p.Key == go);
                if (index < 0)
                {
                    data.objects.Add(new KeyValuePair<GameObject, List<TakeCurve>>(go, new List<TakeCurve>()));
                    index = data.objects.Count - 1;
                }
                sampler.Export(data.objects[index].Value);
            }
            _samplers.Clear();

            if (Writer != null) Writer(data);
            else Debug.LogWarning("Take recorder: no writer (editor only).", this);

            if (_director != null && _director.playableGraph.IsValid()) RebuildKeepingTime(_director);
        }

        // Director time, smoothed on the frame time: on the audio clock it moves by audio buffers (about 20 ms), which
        // would put a judder into the keys. Follows it slowly, jumps when it is far (seek, pause).
        double Clock()
        {
            double frame = Time.unscaledTimeAsDouble;
            if (_director == null) return frame + _offset;
            double error = _director.time - (frame + _offset);
            _offset += Math.Abs(error) > 0.25 ? error : error * 0.05;
            return frame + _offset;
        }

        void Sample(double time)
        {
            if (_times.Count > 0 && time <= _times[_times.Count - 1] + 1e-5) return;
            _times.Add(time);
            for (int i = 0; i < _samplers.Count; i++) _samplers[i].Sample();
        }

        static PlayableDirector PlayingDirector()
        {
            foreach (var d in FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None))
                if (d.state == PlayState.Playing && d.playableAsset is TimelineAsset)
                    return d;
            return null;
        }

        // ---- Timeline muting ----

        void MuteTracks()
        {
            _muted.Clear();
            var timeline = _director != null ? _director.playableAsset as TimelineAsset : null;
            if (timeline == null) return;
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track.muted) continue;
                var animator = _director.GetGenericBinding(track) as Animator;
                if (animator == null || !_samplers.Exists(s => s.component != null && s.component.gameObject == animator.gameObject)) continue;
                track.muted = true;
                _muted.Add(track);
            }
            if (_muted.Count > 0 && _director.playableGraph.IsValid()) RebuildKeepingTime(_director);
        }

        void UnmuteTracks()
        {
            foreach (var track in _muted)
                if (track != null) track.muted = false;
            _muted.Clear();
        }

        public static void RebuildKeepingTime(PlayableDirector d)
        {
            double time = d.time;
            bool playing = d.state == PlayState.Playing;
            d.RebuildGraph();
            d.time = time;
            if (playing) d.Play();
            d.Evaluate();
        }

        // ---- Members ----

        static readonly Type[] RecordableTypes = { typeof(float), typeof(int), typeof(bool), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Color) };

        public static bool IsRecordable(Type type) => type.IsEnum || Array.IndexOf(RecordableTypes, type) >= 0;

        /// <summary>The members of a component a take can record (Transform: position, rotation, scale).</summary>
        public static List<string> RecordableMembers(Type type)
        {
            var list = new List<string>();
            if (typeof(Transform).IsAssignableFrom(type))
            {
                list.Add("position");
                list.Add("rotation");
                list.Add("scale");
                return list;
            }
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Component); t = t.BaseType)
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (Serialized(field) && IsRecordable(field.FieldType) && !list.Contains(field.Name))
                        list.Add(field.Name);
            return list;
        }

        static bool Serialized(FieldInfo field) =>
            !field.IsInitOnly && !field.IsDefined(typeof(NonSerializedAttribute)) &&
            (field.IsPublic || field.IsDefined(typeof(SerializeField)));

        static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

#if UNITY_EDITOR
        void AddSamplers(Component component, TakeChannel channel)
        {
            string member = channel.member != null ? channel.member.Trim() : "";
            if (member == "*")
            {
                foreach (string name in RecordableMembers(component.GetType()))
                    AddSampler(component, name, channel);
                return;
            }
            if (member.Length > 0) AddSampler(component, member, channel);
        }

        void AddSampler(Component component, string member, TakeChannel channel)
        {
            var sampler = Sampler.Create(component, member, channel);
            if (sampler == null)
                Debug.LogWarning($"Take recorder: {component.GetType().Name}.{member} cannot be recorded (unknown, or not a float, int, bool, enum, Vector or Color field).", this);
            else if (!_samplers.Exists(s => s.component == component && s.member == member))
                _samplers.Add(sampler);
        }
#endif

        /// <summary>Reads one member (1 to 4 floats) every frame without allocating.</summary>
        class Sampler
        {
            public Component component;
            public string member;
            public Type bindingType;
            public string[] properties;
            public Func<Component, float>[] readers;
            public bool stepped, discrete, angle, euler, constant;
            public float constantValue;
            public FieldInfo field;
            public float liveValue;
            public List<float>[] values;
            float[] _previous;
            bool _hasPrevious;

#if UNITY_EDITOR
            public static Sampler Create(Component component, string member, TakeChannel channel)
            {
                var s = new Sampler { component = component, member = member, constant = channel.mode == TakeChannel.Mode.Constant,
                                      constantValue = channel.value, liveValue = channel.liveValue };
                bool angles = channel.mode == TakeChannel.Mode.Angles;

                if (component is Transform)
                {
                    s.bindingType = typeof(Transform);
                    switch (member)
                    {
                        case "position":
                            s.properties = new[] { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
                            s.readers = new Func<Component, float>[] { c => ((Transform)c).localPosition.x, c => ((Transform)c).localPosition.y, c => ((Transform)c).localPosition.z };
                            break;
                        case "rotation":
                            s.properties = new[] { "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z" };
                            s.readers = new Func<Component, float>[] { c => ((Transform)c).localEulerAngles.x, c => ((Transform)c).localEulerAngles.y, c => ((Transform)c).localEulerAngles.z };
                            angles = true;
                            break;
                        case "scale":
                            s.properties = new[] { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
                            s.readers = new Func<Component, float>[] { c => ((Transform)c).localScale.x, c => ((Transform)c).localScale.y, c => ((Transform)c).localScale.z };
                            break;
                        default:
                            return null;
                    }
                    if (s.constant) return null;
                }
                else
                {
                    var type = component.GetType();
                    var field = FindField(type, member);
                    if (field == null || !Serialized(field) || !IsRecordable(field.FieldType)) return null;
                    s.field = field;
                    s.bindingType = type;
                    var ft = field.FieldType;
                    string[] subs = ft == typeof(Vector2) ? new[] { "x", "y" }
                                  : ft == typeof(Vector3) ? new[] { "x", "y", "z" }
                                  : ft == typeof(Vector4) ? new[] { "x", "y", "z", "w" }
                                  : ft == typeof(Color) ? new[] { "r", "g", "b", "a" }
                                  : null;
                    if (subs == null)
                    {
                        s.properties = new[] { member };
                        s.readers = new[] { Reader(type, field, null) };
                        s.discrete = ft == typeof(int) || ft.IsEnum;
                        s.stepped = s.discrete || ft == typeof(bool);
                    }
                    else
                    {
                        s.properties = new string[subs.Length];
                        s.readers = new Func<Component, float>[subs.Length];
                        for (int i = 0; i < subs.Length; i++)
                        {
                            s.properties[i] = member + "." + subs[i];
                            s.readers[i] = Reader(type, field, subs[i]);
                        }
                    }
                }
                s.angle = angles && !s.stepped;
                s.euler = s.angle && s.readers.Length == 3;
                return s;
            }

            // Compiled once per take: reads the field (and its x, y, z...) as a float without boxing
            static Func<Component, float> Reader(Type type, FieldInfo field, string sub)
            {
                var c = Expression.Parameter(typeof(Component), "c");
                Expression e = Expression.Field(Expression.Convert(c, type), field);
                if (sub != null) e = Expression.Field(e, sub);
                var t = e.Type;
                if (t == typeof(bool)) e = Expression.Condition(e, Expression.Constant(1f), Expression.Constant(0f));
                else if (t.IsEnum) e = Expression.Convert(Expression.Convert(e, Enum.GetUnderlyingType(t)), typeof(float));
                else if (t != typeof(float)) e = Expression.Convert(e, typeof(float));
                return Expression.Lambda<Func<Component, float>>(e, c).Compile();
            }
#endif

            public void SetLive()
            {
                if (field == null) return;
                var t = field.FieldType;
                object v = t == typeof(bool) ? (object)(liveValue > 0.5f)
                         : t == typeof(int) ? Mathf.RoundToInt(liveValue)
                         : t.IsEnum ? Enum.ToObject(t, Mathf.RoundToInt(liveValue))
                         : t == typeof(float) ? (object)liveValue
                         : null;
                if (v != null) field.SetValue(component, v);
            }

            public void Reserve(int capacity)
            {
                _hasPrevious = false;
                if (constant) return;
                values = new List<float>[readers.Length];
                for (int i = 0; i < values.Length; i++) values[i] = new List<float>(capacity);
                _previous = new float[readers.Length];
            }

            public void Sample()
            {
                if (constant) return;
                if (component == null)
                {
                    for (int i = 0; i < values.Length; i++) values[i].Add(_hasPrevious ? _previous[i] : 0f);
                    return;
                }
                if (euler)
                {
                    float x = readers[0](component), y = readers[1](component), z = readers[2](component);
                    if (_hasPrevious)
                    {
                        // Unity Euler angles (ZXY): (x, y, z) and (180 - x, y + 180, z + 180) are the same rotation. Keep
                        // the one closest to the previous sample, each angle unwrapped, so a pass over a pole does not flip.
                        float ax = Unwrap(_previous[0], x), ay = Unwrap(_previous[1], y), az = Unwrap(_previous[2], z);
                        float bx = Unwrap(_previous[0], 180f - x), by = Unwrap(_previous[1], y + 180f), bz = Unwrap(_previous[2], z + 180f);
                        float da = Mathf.Abs(ax - _previous[0]) + Mathf.Abs(ay - _previous[1]) + Mathf.Abs(az - _previous[2]);
                        float db = Mathf.Abs(bx - _previous[0]) + Mathf.Abs(by - _previous[1]) + Mathf.Abs(bz - _previous[2]);
                        if (db < da) { x = bx; y = by; z = bz; }
                        else { x = ax; y = ay; z = az; }
                    }
                    else
                    {
                        if (x > 180f) x -= 360f;
                        if (y > 180f) y -= 360f;
                        if (z > 180f) z -= 360f;
                    }
                    _previous[0] = x; _previous[1] = y; _previous[2] = z;
                    values[0].Add(x); values[1].Add(y); values[2].Add(z);
                }
                else
                {
                    for (int i = 0; i < readers.Length; i++)
                    {
                        float v = readers[i](component);
                        if (angle && _hasPrevious) v = Unwrap(_previous[i], v);
                        _previous[i] = v;
                        values[i].Add(v);
                    }
                }
                _hasPrevious = true;
            }

            static float Unwrap(float previous, float angle) => previous + Mathf.DeltaAngle(previous, angle);

            public void Export(List<TakeCurve> curves)
            {
                for (int i = 0; i < properties.Length; i++)
                    curves.Add(new TakeCurve
                    {
                        type = bindingType, property = properties[i], stepped = stepped || constant, discrete = discrete, angle = angle,
                        constant = constant, constantValue = constantValue, values = constant ? null : values[i],
                    });
            }
        }
    }
}

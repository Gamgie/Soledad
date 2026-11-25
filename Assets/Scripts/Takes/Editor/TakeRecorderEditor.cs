using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Soledad
{
    /// <summary>Take recorder inspector: Record / Stop in Play mode, and for each target a component picker and an "Add member" menu.</summary>
    [CustomEditor(typeof(TakeRecorder))]
    class TakeRecorderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var recorder = (TakeRecorder)target;

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                var color = GUI.backgroundColor;
                if (recorder.IsRecording) GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
                string label = recorder.IsRecording ? $"Stop  ({recorder.RecordedLength:0.0} s)" : "Record";
                if (GUILayout.Button(label, GUILayout.Height(28))) recorder.ToggleTake();
                GUI.backgroundColor = color;
            }
            if (!Application.isPlaying) EditorGUILayout.HelpBox("Takes are recorded in Play mode, on the Timeline that is playing.", MessageType.None);
            EditorGUILayout.Space();

            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Members", EditorStyles.boldLabel);
            for (int i = 0; i < recorder.targets.Count; i++)
            {
                var t = recorder.targets[i];
                if (t.component == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var components = t.component.GetComponents<Component>().Where(c => c != null).ToArray();
                    int index = System.Array.IndexOf(components, t.component);
                    int chosen = EditorGUILayout.Popup(t.component.gameObject.name, index, components.Select(c => c.GetType().Name).ToArray());
                    if (chosen != index && chosen >= 0)
                    {
                        Undo.RecordObject(recorder, "Take Recorder Component");
                        t.component = components[chosen];
                        EditorUtility.SetDirty(recorder);
                    }
                    if (GUILayout.Button("Add member", EditorStyles.popup, GUILayout.Width(110)))
                    {
                        var menu = new GenericMenu();
                        var members = TakeRecorder.RecordableMembers(t.component.GetType());
                        menu.AddItem(new GUIContent("* (all)"), false, () => Add(recorder, t, "*"));
                        foreach (string m in members)
                        {
                            string member = m;
                            bool present = t.channels.Any(c => c.member == member);
                            if (present) menu.AddDisabledItem(new GUIContent(member));
                            else menu.AddItem(new GUIContent(member), false, () => Add(recorder, t, member));
                        }
                        menu.ShowAsContext();
                    }
                }
            }
            if (Application.isPlaying) Repaint();
        }

        static void Add(TakeRecorder recorder, TakeTarget t, string member)
        {
            Undo.RecordObject(recorder, "Take Recorder Member");
            t.channels.Add(new TakeChannel
            {
                member = member,
                mode = member == "rotation" || member.ToLowerInvariant().Contains("rotation") ? TakeChannel.Mode.Angles : TakeChannel.Mode.Record,
            });
            EditorUtility.SetDirty(recorder);
        }
    }
}

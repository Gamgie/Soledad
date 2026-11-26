using UnityEditor;
using UnityEngine;

namespace Soledad
{
    /// <summary>MonolithField's Inspector: its settings and the Spread Monoliths button (the only thing that places them).</summary>
    [CustomEditor(typeof(MonolithField))]
    public class MonolithFieldEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var field = (MonolithField)target;
            int there = field.Monoliths.Count;
            EditorGUILayout.HelpBox(there > 0
                ? $"{there} monoliths in the scene (under \"{MonolithField.ContainerName}\"): move, duplicate or delete them freely, nothing places them again. " +
                  "Spread Monoliths replaces them from the Spread settings."
                : "No monolith yet: Spread Monoliths places them from the Spread settings.", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying || field.monolithPrefab == null))
            {
                if (GUILayout.Button("Spread Monoliths", GUILayout.Height(28)))
                {
                    if (there == 0 || EditorUtility.DisplayDialog("Spread Monoliths",
                            $"Replace the {there} monoliths there (and any hand moves) with a new spread from the Spread settings?",
                            "Spread", "Cancel"))
                        field.Spread();
                }
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}

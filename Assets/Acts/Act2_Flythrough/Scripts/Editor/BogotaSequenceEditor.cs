using UnityEditor;
using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// BogotaSequence's Inspector: its settings, plus a button that hides the desert terrain outside Play mode to work on
    /// Bogota (the switches above only act in Play mode). Kept for the Editor session, never saved in the scene; Play
    /// mode and renders always show the terrain.
    /// </summary>
    [CustomEditor(typeof(BogotaSequence))]
    public class BogotaSequenceEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Editor view", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                bool hidden = FlythroughTerrain.HiddenInEditor;
                if (GUILayout.Button(hidden ? "Show Desert Terrain" : "Hide Desert Terrain"))
                {
                    FlythroughTerrain.HiddenInEditor = !hidden;
                    EditorApplication.QueuePlayerLoopUpdate();
                    SceneView.RepaintAll();
                }
                EditorGUILayout.HelpBox(hidden
                        ? "The desert terrain is hidden in the Editor (not saved, not in Play mode or renders)."
                        : "Hides the desert terrain outside Play mode, to see Bogota (not saved, not in Play mode or renders).",
                    MessageType.None);
            }
        }
    }
}

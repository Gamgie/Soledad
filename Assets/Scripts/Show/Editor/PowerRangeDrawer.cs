using UnityEditor;
using UnityEngine;

namespace Soledad
{
    /// <summary>Slider with a curved response for <see cref="PowerRangeAttribute"/>, plus the exact value field.</summary>
    [CustomPropertyDrawer(typeof(PowerRangeAttribute))]
    class PowerRangeDrawer : PropertyDrawer
    {
        const float FieldWidth = 64f, Spacing = 5f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Float)
            {
                EditorGUI.LabelField(position, label.text, "Use PowerRange with float.");
                return;
            }
            var range = (PowerRangeAttribute)attribute;
            float span = range.max - range.min;
            float power = Mathf.Max(range.power, 0.0001f);

            label = EditorGUI.BeginProperty(position, label, property);
            Rect content = EditorGUI.PrefixLabel(position, label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            Rect sliderRect = new Rect(content.x, content.y, Mathf.Max(0f, content.width - FieldWidth - Spacing), content.height);
            Rect fieldRect = new Rect(content.xMax - FieldWidth, content.y, FieldWidth, content.height);

            float value = property.floatValue;
            float t = span > 0f ? Mathf.Pow(Mathf.Clamp01((value - range.min) / span), 1f / power) : 0f;

            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            float newT = GUI.HorizontalSlider(sliderRect, t, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
                property.floatValue = range.min + span * Mathf.Pow(newT, power);

            EditorGUI.BeginChangeCheck();
            float typed = EditorGUI.FloatField(fieldRect, value);
            if (EditorGUI.EndChangeCheck())
                property.floatValue = Mathf.Clamp(typed, Mathf.Min(range.min, range.max), Mathf.Max(range.min, range.max));
            EditorGUI.showMixedValue = false;

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}

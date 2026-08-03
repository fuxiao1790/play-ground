using PlayGround.Skills;
using UnityEditor;
using UnityEngine;

namespace PlayGround.Editor.Skills
{
    [CustomPropertyDrawer(typeof(ProjectileDefinition))]
    public sealed class ProjectileDefinitionDrawer : PropertyDrawer
    {
        private const string PrefabPropertyName = "prefab";
        private const string ContinuousCollisionPropertyName = "continuousCollision";
        private const string TrackingEnabledPropertyName = "trackingEnabled";
        private const string MissingPrefabMessage = "Projectile Prefab is required.";
        private const string ContinuousTrackingConflictMessage =
            "Continuous Collision and Tracking Enabled cannot both be enabled.";

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return height;

            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool hasMissingPrefab = HasMissingPrefab(property);
            bool hasConflict = HasContinuousTrackingConflict(property);
            bool enterChildren = true;

            while (child.NextVisible(enterChildren)
                   && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                height += EditorGUIUtility.standardVerticalSpacing
                    + EditorGUI.GetPropertyHeight(child, true);

                if (hasMissingPrefab && child.name == PrefabPropertyName)
                    height += EditorGUIUtility.standardVerticalSpacing
                        + EditorGUIUtility.singleLineHeight * 2f;

                if (hasConflict && child.name == TrackingEnabledPropertyName)
                    height += EditorGUIUtility.standardVerticalSpacing
                        + EditorGUIUtility.singleLineHeight * 2f;
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect line = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
            if (!property.isExpanded)
                return;

            EditorGUI.indentLevel++;
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool hasMissingPrefab = HasMissingPrefab(property);
            bool hasConflict = HasContinuousTrackingConflict(property);
            bool enterChildren = true;
            float y = position.y + EditorGUIUtility.singleLineHeight;

            while (child.NextVisible(enterChildren)
                   && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                float childHeight = EditorGUI.GetPropertyHeight(child, true);
                y += EditorGUIUtility.standardVerticalSpacing;
                line.y = y;
                line.height = childHeight;
                EditorGUI.PropertyField(line, child, true);
                y += childHeight;

                if (hasMissingPrefab && child.name == PrefabPropertyName)
                {
                    y += EditorGUIUtility.standardVerticalSpacing;
                    line.y = y;
                    line.height = EditorGUIUtility.singleLineHeight * 2f;
                    EditorGUI.HelpBox(line, MissingPrefabMessage, MessageType.Error);
                    y += line.height;
                }

                if (hasConflict && child.name == TrackingEnabledPropertyName)
                {
                    y += EditorGUIUtility.standardVerticalSpacing;
                    line.y = y;
                    line.height = EditorGUIUtility.singleLineHeight * 2f;
                    EditorGUI.HelpBox(line, ContinuousTrackingConflictMessage, MessageType.Error);
                    y += line.height;
                }
            }

            EditorGUI.indentLevel--;
        }

        private static bool HasMissingPrefab(SerializedProperty property)
        {
            return property.FindPropertyRelative(PrefabPropertyName).objectReferenceValue == null;
        }

        private static bool HasContinuousTrackingConflict(SerializedProperty property)
        {
            SerializedProperty continuousCollision = property.FindPropertyRelative(
                ContinuousCollisionPropertyName);
            SerializedProperty trackingEnabled = property.FindPropertyRelative(
                TrackingEnabledPropertyName);
            return continuousCollision.boolValue && trackingEnabled.boolValue;
        }
    }
}

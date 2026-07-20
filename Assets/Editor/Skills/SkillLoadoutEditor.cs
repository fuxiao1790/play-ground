using PlayGround.Skills;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PlayGround.Editor.Skills
{
    [CustomEditor(typeof(SkillLoadout))]
    public sealed class SkillLoadoutEditor : UnityEditor.Editor
    {
        private SerializedProperty _nodes;
        private SerializedProperty _maxRootSets;
        private ReorderableList _list;
        private int _removeAt = -1;

        private void OnEnable()
        {
            _nodes = serializedObject.FindProperty("nodes");
            _maxRootSets = serializedObject.FindProperty("maxRootSets");
            _list = new ReorderableList(serializedObject, _nodes, true, true, true, false)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Normalized Skill Nodes"),
                elementHeightCallback = _ => EditorGUIUtility.singleLineHeight * 2 + 8,
                drawElementCallback = DrawElement,
                onAddCallback = AddNode,
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(_maxRootSets);
            EditorGUILayout.HelpBox(
                "Each node owns one skill set and its outgoing link to the next node. "
                + "Incoming links make the node triggered-only.",
                MessageType.Info);

            _removeAt = -1;
            _list.DoLayoutList();
            if (_removeAt >= 0)
                _nodes.DeleteArrayElementAtIndex(_removeAt);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty node = _nodes.GetArrayElementAtIndex(index);
            SerializedProperty skillSet = node.FindPropertyRelative("skillSet");
            SerializedProperty triggerToNext = node.FindPropertyRelative("triggerToNext");
            float lineHeight = EditorGUIUtility.singleLineHeight;
            var removeRect = new Rect(rect.xMax - 20, rect.y + lineHeight * 0.5f, 20, lineHeight);
            var fieldRect = new Rect(rect.x, rect.y + 2, rect.width - 24, lineHeight);

            EditorGUI.PropertyField(fieldRect, skillSet, new GUIContent($"Skill {index}"));
            fieldRect.y += lineHeight + 4;
            EditorGUI.PropertyField(fieldRect, triggerToNext, new GUIContent("Trigger To Next"));

            if (GUI.Button(removeRect, "-"))
                _removeAt = index;
        }

        private void AddNode(ReorderableList list)
        {
            _nodes.arraySize++;
        }
    }
}

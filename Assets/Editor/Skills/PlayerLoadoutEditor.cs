using PlayGround.Skills;
using UnityEditor;
using UnityEngine;

namespace PlayGround.Editor.Skills
{
    [CustomEditor(typeof(PlayerLoadout))]
    public sealed class PlayerLoadoutEditor : UnityEditor.Editor
    {
        private SerializedProperty _slots;
        private SerializedProperty _maxRootSets;

        private void OnEnable()
        {
            _slots = serializedObject.FindProperty("slots");
            _maxRootSets = serializedObject.FindProperty("maxRootSets");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_maxRootSets);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Slots", EditorStyles.boldLabel);

            DrawSlotList();
            DrawDropZone();

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Skill Set"))
                AppendSkillSetSlot(null);
            if (GUILayout.Button("+ Trigger Link"))
                AppendTriggerLinkSlot();
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSlotList()
        {
            int removeAt = -1;

            for (int i = 0; i < _slots.arraySize; i++)
            {
                SerializedProperty slot = _slots.GetArrayElementAtIndex(i);
                string typeName = slot.managedReferenceFullTypename;

                EditorGUILayout.BeginHorizontal();

                if (typeName.Contains(nameof(SkillSetSlot)))
                {
                    SerializedProperty skillSetProp = slot.FindPropertyRelative("skillSet");
                    EditorGUILayout.LabelField("Skill", GUILayout.Width(46));
                    EditorGUILayout.ObjectField(skillSetProp, typeof(SkillSet), GUIContent.none);
                }
                else if (typeName.Contains(nameof(TriggerLinkSlot)))
                {
                    SerializedProperty linkProp = slot.FindPropertyRelative("link");
                    EditorGUILayout.LabelField("Trigger", GUILayout.Width(46));
                    EditorGUILayout.ObjectField(linkProp, typeof(TriggerLink), GUIContent.none);
                }
                else
                {
                    EditorGUILayout.LabelField("(null slot)");
                }

                if (GUILayout.Button("-", GUILayout.Width(20)))
                    removeAt = i;

                EditorGUILayout.EndHorizontal();
            }

            if (removeAt >= 0)
                _slots.DeleteArrayElementAtIndex(removeAt);
        }

        private void DrawDropZone()
        {
            Rect zone = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            GUI.Box(zone, "drop SkillSet or TriggerLink here", EditorStyles.helpBox);

            Event e = Event.current;
            if (!zone.Contains(e.mousePosition)) return;

            if (e.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = AnyValidInDrag()
                    ? DragAndDropVisualMode.Copy
                    : DragAndDropVisualMode.Rejected;
                e.Use();
            }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object obj in DragAndDrop.objectReferences)
                {
                    if (obj is SkillSet skillSet) AppendSkillSetSlot(skillSet);
                    else if (obj is TriggerLink trigger) AppendTriggerLinkSlot(trigger);
                }
                e.Use();
            }
        }

        private static bool AnyValidInDrag()
        {
            foreach (Object obj in DragAndDrop.objectReferences)
                if (obj is SkillSet || obj is TriggerLink) return true;
            return false;
        }

        private void AppendSkillSetSlot(SkillSet skillSet)
        {
            _slots.arraySize++;
            SerializedProperty s = _slots.GetArrayElementAtIndex(_slots.arraySize - 1);
            s.managedReferenceValue = new SkillSetSlot { skillSet = skillSet };
        }

        private void AppendTriggerLinkSlot(TriggerLink trigger = null)
        {
            _slots.arraySize++;
            SerializedProperty s = _slots.GetArrayElementAtIndex(_slots.arraySize - 1);
            s.managedReferenceValue = new TriggerLinkSlot { link = trigger };
        }
    }
}

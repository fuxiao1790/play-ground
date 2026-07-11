using PlayGround.Skills;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PlayGround.Editor.Skills
{
    [CustomEditor(typeof(SkillLoadout))]
    public sealed class SkillLoadoutEditor : UnityEditor.Editor
    {
        private SerializedProperty _slots;
        private SerializedProperty _maxRootSets;
        private ReorderableList _list;
        private int _removeAt = -1;

        private void OnEnable()
        {
            _slots = serializedObject.FindProperty("slots");
            _maxRootSets = serializedObject.FindProperty("maxRootSets");

            _list = new ReorderableList(serializedObject, _slots, true, true, false, false)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Slots"),
                elementHeightCallback = _ => EditorGUIUtility.singleLineHeight + 4,
                drawElementCallback = DrawElement,
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_maxRootSets);
            EditorGUILayout.Space(6);

            _removeAt = -1;
            _list.DoLayoutList();
            if (_removeAt >= 0)
                _slots.DeleteArrayElementAtIndex(_removeAt);

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

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty slot = _slots.GetArrayElementAtIndex(index);
            string typeName = slot.managedReferenceFullTypename;

            rect.y += 2;
            rect.height = EditorGUIUtility.singleLineHeight;

            var labelRect = new Rect(rect.x, rect.y, 46, rect.height);
            var fieldRect = new Rect(rect.x + 50, rect.y, rect.width - 50 - 24, rect.height);
            var removeRect = new Rect(rect.xMax - 20, rect.y, 20, rect.height);

            if (typeName.Contains(nameof(SkillSetSlot)))
            {
                SerializedProperty skillSetProp = slot.FindPropertyRelative("skillSet");
                EditorGUI.LabelField(labelRect, "Skill");
                EditorGUI.ObjectField(fieldRect, skillSetProp, typeof(SkillSet), GUIContent.none);
            }
            else if (typeName.Contains(nameof(TriggerLinkSlot)))
            {
                SerializedProperty linkProp = slot.FindPropertyRelative("link");
                EditorGUI.LabelField(labelRect, "Trigger");
                EditorGUI.ObjectField(fieldRect, linkProp, typeof(TriggerLink), GUIContent.none);
            }
            else
            {
                EditorGUI.LabelField(rect, "(null slot)");
                return;
            }

            if (GUI.Button(removeRect, "-"))
                _removeAt = index;
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

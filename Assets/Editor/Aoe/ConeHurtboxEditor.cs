using PlayGround.System.Aoe;
using UnityEditor;
using UnityEngine;

namespace PlayGround.Editor.Aoe
{
    [CustomEditor(typeof(ConeHurtbox))]
    public sealed class ConeHurtboxEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            var cone = (ConeHurtbox)target;
            Transform t = cone.transform;
            Vector3 origin = t.position;
            Vector3 forward = t.up;
            Vector3 leftEdge = Quaternion.AngleAxis(cone.halfAngleDegrees, Vector3.forward) * forward;
            Vector3 rightEdge = Quaternion.AngleAxis(-cone.halfAngleDegrees, Vector3.forward) * forward;

            Handles.color = new Color(0f, 1f, 1f, 0.9f);
            Handles.DrawLine(origin, origin + leftEdge * cone.range);
            Handles.DrawLine(origin, origin + rightEdge * cone.range);
            Handles.DrawWireArc(origin, Vector3.forward, rightEdge, cone.halfAngleDegrees * 2f, cone.range);

            DrawRangeHandle(cone, origin, forward);
            DrawAngleHandle(cone, origin, forward, left: true);
            DrawAngleHandle(cone, origin, forward, left: false);
        }

        private static void DrawRangeHandle(ConeHurtbox cone, Vector3 origin, Vector3 forward)
        {
            Vector3 tipPos = origin + forward * cone.range;
            float size = HandleUtility.GetHandleSize(tipPos) * 0.1f;

            EditorGUI.BeginChangeCheck();
            Vector3 newTip = Handles.Slider(tipPos, forward, size, Handles.DotHandleCap, 0f);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            Undo.RecordObject(cone, "Adjust Cone Range");
            cone.range = Mathf.Max(0f, Vector3.Dot(newTip - origin, forward));
            EditorUtility.SetDirty(cone);
        }

        private static void DrawAngleHandle(ConeHurtbox cone, Vector3 origin, Vector3 forward, bool left)
        {
            float sign = left ? 1f : -1f;
            Vector3 edgeDir = Quaternion.AngleAxis(cone.halfAngleDegrees * sign, Vector3.forward) * forward;
            Vector3 edgeTip = origin + edgeDir * cone.range;
            float size = HandleUtility.GetHandleSize(edgeTip) * 0.1f;

            EditorGUI.BeginChangeCheck();
            Vector3 newTip = Handles.FreeMoveHandle(edgeTip, size, Vector3.zero, Handles.DotHandleCap);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            Vector3 newDir = newTip - origin;
            if (newDir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float angle = Vector3.SignedAngle(forward, newDir.normalized, Vector3.forward);
            Undo.RecordObject(cone, "Adjust Cone Angle");
            cone.halfAngleDegrees = Mathf.Clamp(Mathf.Abs(angle), 1f, 179f);
            EditorUtility.SetDirty(cone);
        }
    }
}

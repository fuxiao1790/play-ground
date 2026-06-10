using UnityEngine;

namespace PlayGround.System.Aoe
{
    public sealed class ConeHurtbox : MonoBehaviour
    {
        [Min(0f)] public float range = 3f;
        [Range(1f, 179f)] public float halfAngleDegrees = 45f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.9f);
            DrawWireGizmo(transform.position, transform.up, range, halfAngleDegrees);
        }

        internal static void DrawWireGizmo(Vector3 origin, Vector3 forward, float range, float halfAngleDegrees)
        {
            Vector3 leftEdge = Quaternion.AngleAxis(halfAngleDegrees, Vector3.forward) * forward;
            Vector3 rightEdge = Quaternion.AngleAxis(-halfAngleDegrees, Vector3.forward) * forward;

            Gizmos.DrawLine(origin, origin + leftEdge * range);
            Gizmos.DrawLine(origin, origin + rightEdge * range);

            const int Segments = 24;
            for (int i = 0; i < Segments; i++)
            {
                float t0 = (float)i / Segments;
                float t1 = (float)(i + 1) / Segments;
                float angle0 = Mathf.Lerp(halfAngleDegrees, -halfAngleDegrees, t0);
                float angle1 = Mathf.Lerp(halfAngleDegrees, -halfAngleDegrees, t1);
                Vector3 p0 = origin + (Quaternion.AngleAxis(angle0, Vector3.forward) * forward) * range;
                Vector3 p1 = origin + (Quaternion.AngleAxis(angle1, Vector3.forward) * forward) * range;
                Gizmos.DrawLine(p0, p1);
            }
        }
    }
}

using UnityEngine;

namespace PlayGround.System.Common
{
    public interface ICombatTarget
    {
        int TargetId { get; }
        Vector2 CombatTargetPosition { get; }
        float CombatTargetRadius { get; }
        Vector2 CombatTargetHalfExtents { get; }
        float CombatTargetRotationRadians { get; }
        CombatShapeType CombatTargetShapeType { get; }
        int CombatTargetMask { get; }
        bool IsCombatTargetActive { get; }
    }
}

using PlayGround.Common;
using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public interface IAoeTarget
    {
        int TargetId { get; }
        Vector2 AoeTargetPosition { get; }
        float AoeTargetRadius { get; }
        Vector2 AoeTargetHalfExtents { get; }
        float AoeTargetRotationRadians { get; }
        CombatShapeType AoeTargetShapeType { get; }
        int AoeTargetMask { get; }
        bool IsAoeTargetActive { get; }
        void ReceiveAoeHit(DamageSnapshot damage);
    }
}

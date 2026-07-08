using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Common
{
    public struct TargetCollisionShape : IComponentData
    {
        public CombatShapeType ShapeType;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public int Mask;
    }
}

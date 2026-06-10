using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public readonly struct AoeShape
    {
        public AoeShape(CombatShapeType shapeType, float radius, Vector2 halfExtents, float rotationRadians)
        {
            ShapeType = shapeType;
            Radius = Mathf.Max(0f, radius);
            HalfExtents = new Vector2(Mathf.Max(0f, halfExtents.x), Mathf.Max(0f, halfExtents.y));
            RotationRadians = rotationRadians;
        }

        public CombatShapeType ShapeType { get; }
        public float Radius { get; }
        public Vector2 HalfExtents { get; }
        public float RotationRadians { get; }
    }
}

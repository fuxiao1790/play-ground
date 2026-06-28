using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Common
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

    public readonly struct AoeSpawnGeometry
    {
        public AoeSpawnGeometry(
            float areaSize,
            CombatShapeType shapeType,
            float radius,
            Vector2 halfExtents,
            float rotationRadians,
            Vector2 visualScale,
            float visualRotationDegrees)
        {
            IsValid = true;
            AreaSize = Mathf.Max(0.01f, areaSize);
            ShapeType = shapeType;
            Radius = Mathf.Max(0f, radius);
            HalfExtents = new Vector2(Mathf.Max(0f, halfExtents.x), Mathf.Max(0f, halfExtents.y));
            RotationRadians = rotationRadians;
            VisualScale = new Vector2(Mathf.Max(0f, visualScale.x), Mathf.Max(0f, visualScale.y));
            math.sincos(math.radians(visualRotationDegrees), out float sin, out float cos);
            VisualRotationSin = sin;
            VisualRotationCos = cos;
        }

        public bool IsValid { get; }
        public float AreaSize { get; }
        public CombatShapeType ShapeType { get; }
        public float Radius { get; }
        public Vector2 HalfExtents { get; }
        public float RotationRadians { get; }
        public Vector2 VisualScale { get; }
        public float VisualRotationSin { get; }
        public float VisualRotationCos { get; }

        public static AoeSpawnGeometry FromTemplate(
            GameObject visualPrefab,
            Collider2D collisionShape,
            float areaSize,
            float visualRotationDegrees)
        {
            float resolvedAreaSize = Mathf.Max(0.01f, areaSize);
            Collider2D collider = collisionShape;
            if (collider == null && visualPrefab != null)
            {
                collider = visualPrefab.GetComponentInChildren<Collider2D>(true);
            }

            AoeShape baseShape = collider != null
                ? new AoeShape(
                    CombatTargetShapeUtility.ShapeType(collider),
                    CombatTargetShapeUtility.Radius(collider),
                    CombatTargetShapeUtility.HalfExtents(collider),
                    CombatTargetShapeUtility.RotationRadians(collider))
                : FallbackShape(visualPrefab);

            Vector2 baseVisualScale = Vector2.one;
            if (visualPrefab != null)
            {
                SpriteRenderer spriteRenderer = visualPrefab.GetComponentInChildren<SpriteRenderer>(true);
                if (spriteRenderer != null)
                {
                    Vector3 scale = spriteRenderer.transform.lossyScale;
                    baseVisualScale = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                }
            }

            return new AoeSpawnGeometry(
                resolvedAreaSize,
                baseShape.ShapeType,
                baseShape.Radius * resolvedAreaSize,
                baseShape.HalfExtents * resolvedAreaSize,
                baseShape.RotationRadians,
                baseVisualScale * resolvedAreaSize,
                visualRotationDegrees);
        }

        private static AoeShape FallbackShape(GameObject prefab)
        {
            float radius = 1f;
            if (prefab != null)
            {
                Vector3 scale = prefab.transform.lossyScale;
                radius = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            }

            return new AoeShape(CombatShapeType.Circle, radius, Vector2.one * radius, 0f);
        }
    }
}

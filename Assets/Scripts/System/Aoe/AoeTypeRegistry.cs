using System.Collections.Generic;
using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public sealed class AoeTypeRegistry
    {
        private readonly Dictionary<int, AoeTypeDefinition> definitionsById = new();
        private readonly Dictionary<int, AoeShape> shapesById = new();
        private readonly Dictionary<int, AoeVisualDefinition> visualsById = new();

        public AoeTypeRegistry()
        {
        }

        public void Register(AoeTypeDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            AoeShape shape = BakeShape(definition);
            definitionsById[definition.TypeId] = definition;
            shapesById[definition.TypeId] = shape;
            if (TryBakeVisual(definition, out AoeVisualDefinition visual))
            {
                visualsById[definition.TypeId] = visual;
            }
        }

        public bool TryGetDefinition(int typeId, out AoeTypeDefinition definition)
        {
            return definitionsById.TryGetValue(typeId, out definition);
        }

        public bool TryGetShape(int typeId, out AoeShape shape)
        {
            return shapesById.TryGetValue(typeId, out shape);
        }

        public bool TryGetVisual(int typeId, out AoeVisualDefinition visual)
        {
            return visualsById.TryGetValue(typeId, out visual);
        }

        private static AoeShape BakeShape(AoeTypeDefinition definition)
        {
            Collider2D collider = definition.CollisionShape;
            if (collider == null && definition.VisualPrefab != null)
            {
                collider = definition.VisualPrefab.GetComponentInChildren<Collider2D>(true);
            }

            if (collider == null)
            {
                float radius = definition.SizeMultiplier;
                return new AoeShape(CombatShapeType.Circle, radius, Vector2.one * radius, 0f);
            }

            float sizeMultiplier = definition.SizeMultiplier;
            return new AoeShape(
                CombatTargetShapeUtility.ShapeType(collider),
                UnscaledRadius(collider) * sizeMultiplier,
                UnscaledHalfExtents(collider) * sizeMultiplier,
                CombatTargetShapeUtility.RotationRadians(collider));
        }

        private static float UnscaledRadius(Collider2D collider)
        {
            if (collider is CircleCollider2D circle)
            {
                return Mathf.Max(0f, circle.radius);
            }

            if (collider is CapsuleCollider2D capsule)
            {
                return capsule.direction == CapsuleDirection2D.Vertical
                    ? Mathf.Max(0f, capsule.size.x * 0.5f)
                    : Mathf.Max(0f, capsule.size.y * 0.5f);
            }

            return 0f;
        }

        private static Vector2 UnscaledHalfExtents(Collider2D collider)
        {
            if (collider is BoxCollider2D box)
            {
                return box.size * 0.5f;
            }

            if (collider is CapsuleCollider2D capsule)
            {
                float radius = UnscaledRadius(capsule);
                float halfSegment = capsule.direction == CapsuleDirection2D.Vertical
                    ? Mathf.Max(0f, (capsule.size.y * 0.5f) - radius)
                    : Mathf.Max(0f, (capsule.size.x * 0.5f) - radius);
                return new Vector2(halfSegment, radius);
            }

            float radiusFallback = UnscaledRadius(collider);
            return new Vector2(radiusFallback, radiusFallback);
        }

        private static bool TryBakeVisual(AoeTypeDefinition definition, out AoeVisualDefinition visual)
        {
            visual = default;
            if (definition.VisualPrefab == null)
            {
                return false;
            }

            SpriteRenderer spriteRenderer = definition.VisualPrefab.GetComponentInChildren<SpriteRenderer>(true);
            if (spriteRenderer == null || spriteRenderer.sprite == null)
            {
                return false;
            }

            visual = new AoeVisualDefinition(
                spriteRenderer.sprite,
                spriteRenderer.sharedMaterial,
                definition.SizeMultiplier,
                definition.VisualRotationDegrees);
            return true;
        }
    }

    public readonly struct AoeVisualDefinition
    {
        public AoeVisualDefinition(Sprite sprite, Material material, float visualScale, float visualRotationDegrees)
        {
            Sprite = sprite;
            Material = material;
            VisualScale = Mathf.Max(0f, visualScale);
            VisualRotationDegrees = visualRotationDegrees;
        }

        public Sprite Sprite { get; }
        public Material Material { get; }
        public float VisualScale { get; }
        public float VisualRotationDegrees { get; }
    }

    [global::System.Serializable]
    public sealed class AoeTypeDefinition
    {
        [SerializeField] private int typeId;
        [SerializeField] private GameObject visualPrefab;
        [SerializeField] private Collider2D collisionShape;
        [SerializeField, Min(0.01f)] private float sizeMultiplier = 1f;
        [SerializeField] private float visualRotationDegrees;
        [SerializeField, Min(0)] private int preloadCount;

        public int TypeId => typeId;
        public GameObject VisualPrefab => visualPrefab;
        public Collider2D CollisionShape => collisionShape;
        public float SizeMultiplier => sizeMultiplier;
        public float VisualRotationDegrees => visualRotationDegrees;
        public int PreloadCount => preloadCount;

        public void Configure(
            int typeId,
            GameObject visualPrefab,
            Collider2D collisionShape,
            float sizeMultiplier = 1f,
            float visualRotationDegrees = 0f,
            int preloadCount = 0)
        {
            this.typeId = typeId;
            this.visualPrefab = visualPrefab;
            this.collisionShape = collisionShape;
            this.sizeMultiplier = Mathf.Max(0.01f, sizeMultiplier);
            this.visualRotationDegrees = visualRotationDegrees;
            this.preloadCount = Mathf.Max(0, preloadCount);
        }
    }
}

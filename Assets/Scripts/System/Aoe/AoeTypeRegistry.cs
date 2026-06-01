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
                return new AoeShape(CombatShapeType.Circle, definition.FallbackRadius, Vector2.one * definition.FallbackRadius, 0f);
            }

            return new AoeShape(
                CombatTargetShapeUtility.ShapeType(collider),
                CombatTargetShapeUtility.Radius(collider, definition.FallbackRadius),
                CombatTargetShapeUtility.HalfExtents(collider, definition.FallbackRadius),
                CombatTargetShapeUtility.RotationRadians(collider));
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
                definition.VisualScale,
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
        [SerializeField, Min(0.01f)] private float fallbackRadius = 1f;
        [SerializeField, Min(0f)] private float visualScale = 1f;
        [SerializeField] private float visualRotationDegrees;
        [SerializeField, Min(0)] private int preloadCount;

        public int TypeId => typeId;
        public GameObject VisualPrefab => visualPrefab;
        public Collider2D CollisionShape => collisionShape;
        public float FallbackRadius => fallbackRadius;
        public float VisualScale => visualScale;
        public float VisualRotationDegrees => visualRotationDegrees;
        public int PreloadCount => preloadCount;

        public void Configure(
            int typeId,
            GameObject visualPrefab,
            Collider2D collisionShape,
            float fallbackRadius = 1f,
            float visualScale = 1f,
            float visualRotationDegrees = 0f,
            int preloadCount = 0)
        {
            this.typeId = typeId;
            this.visualPrefab = visualPrefab;
            this.collisionShape = collisionShape;
            this.fallbackRadius = Mathf.Max(0.01f, fallbackRadius);
            this.visualScale = Mathf.Max(0f, visualScale);
            this.visualRotationDegrees = visualRotationDegrees;
            this.preloadCount = Mathf.Max(0, preloadCount);
        }
    }
}

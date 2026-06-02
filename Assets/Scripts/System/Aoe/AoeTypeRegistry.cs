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
                float radius = definition.SizeMultiplier * PrefabUniformScale(definition.VisualPrefab);
                return new AoeShape(CombatShapeType.Circle, radius, Vector2.one * radius, 0f);
            }

            float sizeMultiplier = definition.SizeMultiplier;
            return new AoeShape(
                CombatTargetShapeUtility.ShapeType(collider),
                CombatTargetShapeUtility.Radius(collider) * sizeMultiplier,
                CombatTargetShapeUtility.HalfExtents(collider) * sizeMultiplier,
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
                VisualScale(spriteRenderer) * definition.SizeMultiplier,
                definition.VisualRotationDegrees);
            return true;
        }

        private static Vector2 VisualScale(SpriteRenderer spriteRenderer)
        {
            Vector3 scale = spriteRenderer.transform.lossyScale;
            return new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }

        private static float PrefabUniformScale(GameObject prefab)
        {
            if (prefab == null)
            {
                return 1f;
            }

            Vector3 scale = prefab.transform.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }
    }

    public readonly struct AoeVisualDefinition
    {
        public AoeVisualDefinition(Sprite sprite, Material material, Vector2 visualScale, float visualRotationDegrees)
        {
            Sprite = sprite;
            Material = material;
            VisualScale = new Vector2(Mathf.Max(0f, visualScale.x), Mathf.Max(0f, visualScale.y));
            VisualRotationDegrees = visualRotationDegrees;
        }

        public Sprite Sprite { get; }
        public Material Material { get; }
        public Vector2 VisualScale { get; }
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

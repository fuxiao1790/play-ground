using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Aoe
{
    public sealed class AoeTypeRegistry
    {
        private readonly Dictionary<int, AoeTypeDefinition> definitionsById = new();
        private readonly Dictionary<int, AoeVisualDefinition> visualsById = new();

        public AoeTypeRegistry()
        {
        }

        public void Register(int typeId, AoeTypeDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            definitionsById[typeId] = definition;
            if (TryBakeVisual(definition, out AoeVisualDefinition visual))
            {
                visualsById[typeId] = visual;
            }
        }

        public bool TryGetDefinition(int typeId, out AoeTypeDefinition definition)
        {
            return definitionsById.TryGetValue(typeId, out definition);
        }

        public bool TryGetVisual(int typeId, out AoeVisualDefinition visual)
        {
            return visualsById.TryGetValue(typeId, out visual);
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
                Vector2.one,
                0f);
            return true;
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
        [SerializeField] private GameObject visualPrefab;
        [SerializeField] private Collider2D collisionShape;
        [SerializeField] private float visualRotationDegrees;
        [SerializeField, Min(0)] private int preloadCount;
        [SerializeField] private VisualEffectAsset spawnEffect;
        [SerializeField] private VisualEffectAsset hitEffect;
        [SerializeField] private VisualEffectAsset expireEffect;
        [SerializeField] private VisualEffectAsset pulseEffect;
        [SerializeField] private VisualEffectAsset armingEffect;

        public GameObject VisualPrefab => visualPrefab;
        public Collider2D CollisionShape => collisionShape;
        public float VisualRotationDegrees => visualRotationDegrees;
        public int PreloadCount => preloadCount;
        public VisualEffectAsset SpawnEffect => spawnEffect;
        public VisualEffectAsset HitEffect => hitEffect;
        public VisualEffectAsset ExpireEffect => expireEffect;
        public VisualEffectAsset PulseEffect => pulseEffect;
        public VisualEffectAsset ArmingEffect => armingEffect;

        public void Configure(
            GameObject visualPrefab,
            Collider2D collisionShape,
            float visualRotationDegrees = 0f,
            int preloadCount = 0,
            VisualEffectAsset spawnEffect = null,
            VisualEffectAsset hitEffect = null,
            VisualEffectAsset expireEffect = null,
            VisualEffectAsset pulseEffect = null,
            VisualEffectAsset armingEffect = null)
        {
            this.visualPrefab = visualPrefab;
            this.collisionShape = collisionShape;
            this.visualRotationDegrees = visualRotationDegrees;
            this.preloadCount = Mathf.Max(0, preloadCount);
            this.spawnEffect = spawnEffect;
            this.hitEffect = hitEffect;
            this.expireEffect = expireEffect;
            this.pulseEffect = pulseEffect;
            this.armingEffect = armingEffect;
        }
    }
}

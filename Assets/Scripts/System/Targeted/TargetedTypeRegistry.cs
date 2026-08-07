using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Combat.Targeted
{
    public sealed class TargetedTypeRegistry
    {
        private readonly Dictionary<int, TargetedTypeDefinition> definitionsById = new();
        private readonly Dictionary<int, TargetedVisualDefinition> visualsById = new();

        public void Register(int typeId, TargetedTypeDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            definitionsById[typeId] = definition;
            if (TryBakeVisual(definition, out TargetedVisualDefinition visual))
            {
                visualsById[typeId] = visual;
            }
            else
            {
                visualsById.Remove(typeId);
            }
        }

        public bool TryGetDefinition(int typeId, out TargetedTypeDefinition definition) =>
            definitionsById.TryGetValue(typeId, out definition);

        public bool TryGetVisual(int typeId, out TargetedVisualDefinition visual) =>
            visualsById.TryGetValue(typeId, out visual);

        public void SetVfxIds(int typeId, TargetedVfxIds vfxIds)
        {
            if (definitionsById.TryGetValue(typeId, out TargetedTypeDefinition definition))
            {
                definition.SetVfxIds(vfxIds);
            }
        }

        private static bool TryBakeVisual(TargetedTypeDefinition definition, out TargetedVisualDefinition visual)
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

            visual = new TargetedVisualDefinition(
                spriteRenderer.sprite,
                spriteRenderer.sharedMaterial,
                Vector2.one,
                definition.VisualRotationDegrees);
            return true;
        }
    }

    public readonly struct TargetedVisualDefinition
    {
        public TargetedVisualDefinition(Sprite sprite, Material material, Vector2 visualScale, float visualRotationDegrees)
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
    public sealed class TargetedTypeDefinition
    {
        [SerializeField] private GameObject visualPrefab;
        [SerializeField] private float visualRotationDegrees;
        [SerializeField, Min(0)] private int preloadCount;
        [SerializeField] private VisualEffectAsset spawnEffect;
        [SerializeField] private VisualEffectAsset hitEffect;
        [SerializeField] private VisualEffectAsset expireEffect;
        [SerializeField] private VisualEffectAsset linkEffect;
        [SerializeField] private VisualEffectAsset armingEffect;
        [SerializeField, Min(0f)] private float effectSize = 1f;
        [SerializeField, Min(0f)] private float linkWidth = 1f;

        public GameObject VisualPrefab => visualPrefab;
        public float VisualRotationDegrees => visualRotationDegrees;
        public int PreloadCount => preloadCount;
        public VisualEffectAsset SpawnEffect => spawnEffect;
        public VisualEffectAsset HitEffect => hitEffect;
        public VisualEffectAsset ExpireEffect => expireEffect;
        public VisualEffectAsset LinkEffect => linkEffect;
        public VisualEffectAsset ArmingEffect => armingEffect;
        public float EffectSize => effectSize;
        public float LinkWidth => linkWidth;
        public TargetedVfxIds VfxIds { get; private set; }

        public void Configure(
            GameObject visualPrefab,
            float visualRotationDegrees = 0f,
            int preloadCount = 0,
            VisualEffectAsset spawnEffect = null,
            VisualEffectAsset hitEffect = null,
            VisualEffectAsset expireEffect = null,
            VisualEffectAsset linkEffect = null,
            VisualEffectAsset armingEffect = null,
            float effectSize = 1f,
            float linkWidth = 1f)
        {
            this.visualPrefab = visualPrefab;
            this.visualRotationDegrees = visualRotationDegrees;
            this.preloadCount = Mathf.Max(0, preloadCount);
            this.spawnEffect = spawnEffect;
            this.hitEffect = hitEffect;
            this.expireEffect = expireEffect;
            this.linkEffect = linkEffect;
            this.armingEffect = armingEffect;
            this.effectSize = Mathf.Max(0f, effectSize);
            this.linkWidth = Mathf.Max(0f, linkWidth);
        }

        public void SetVfxIds(TargetedVfxIds vfxIds)
        {
            VfxIds = vfxIds;
        }
    }
}

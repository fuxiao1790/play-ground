using PlayGround.System.Combat.Vfx;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Skills
{
    public sealed class TargetedPrefab : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private VisualEffectAsset spawnEffect;
        [SerializeField] private VfxDataShape spawnEffectShape = VfxDataShape.Circular;
        [SerializeField] private VisualEffectAsset hitEffect;
        [SerializeField] private VfxDataShape hitEffectShape = VfxDataShape.Circular;
        [SerializeField] private VisualEffectAsset expireEffect;
        [SerializeField] private VfxDataShape expireEffectShape = VfxDataShape.Circular;
        [SerializeField] private VisualEffectAsset linkEffect;
        [SerializeField] private VfxDataShape linkEffectShape = VfxDataShape.LineSegment;
        [SerializeField] private VisualEffectAsset armingEffect;
        [SerializeField] private VfxDataShape armingEffectShape = VfxDataShape.Circular;
        [SerializeField, Min(0f)] private float vfxEffectSize = 1f;
        [SerializeField, Min(0f)] private float linkWidth = 1f;

        public SpriteRenderer SpriteRenderer => spriteRenderer;
        public Sprite Sprite => spriteRenderer != null ? spriteRenderer.sprite : null;
        public float VisualRotationDegrees => spriteRenderer != null ? spriteRenderer.transform.eulerAngles.z : 0f;
        public VisualEffectAsset SpawnEffect => spawnEffect;
        public VfxDataShape SpawnEffectShape => spawnEffectShape;
        public VisualEffectAsset HitEffect => hitEffect;
        public VfxDataShape HitEffectShape => hitEffectShape;
        public VisualEffectAsset ExpireEffect => expireEffect;
        public VfxDataShape ExpireEffectShape => expireEffectShape;
        public VisualEffectAsset LinkEffect => linkEffect;
        public VfxDataShape LinkEffectShape => linkEffectShape;
        public VisualEffectAsset ArmingEffect => armingEffect;
        public VfxDataShape ArmingEffectShape => armingEffectShape;
        public float VfxEffectSize => vfxEffectSize;
        public float LinkWidth => linkWidth;

        private void Awake()
        {
            if (!IsValidTemplate(out string reason))
                throw new MissingReferenceException($"{nameof(TargetedPrefab)} on {name} has invalid targeted template setup: {reason}.");
        }

        private void OnValidate()
        {
            spriteRenderer ??= FindChildComponent<SpriteRenderer>("Visual");
        }

        public void Configure(
            SpriteRenderer renderer,
            VisualEffectAsset spawnVisualEffect = null,
            VisualEffectAsset hitVisualEffect = null,
            VisualEffectAsset expireVisualEffect = null,
            VisualEffectAsset linkVisualEffect = null,
            VisualEffectAsset armingVisualEffect = null,
            VfxDataShape linkShape = VfxDataShape.LineSegment,
            float effectSize = 1f,
            float authoredLinkWidth = 1f)
        {
            spriteRenderer = renderer;
            spawnEffect = spawnVisualEffect;
            hitEffect = hitVisualEffect;
            expireEffect = expireVisualEffect;
            linkEffect = linkVisualEffect;
            armingEffect = armingVisualEffect;
            linkEffectShape = linkShape;
            vfxEffectSize = Mathf.Max(0f, effectSize);
            linkWidth = Mathf.Max(0f, authoredLinkWidth);
        }

        public bool IsValidTemplate(out string reason)
        {
            if (transform.Find("Hurtbox") != null)
            {
                reason = "targeted template contains a Hurtbox child; remove copied physics setup";
                return false;
            }

            if (linkEffectShape != VfxDataShape.LineSegment)
            {
                reason = "link effect must use LineSegment shape";
                return false;
            }

            if (spriteRenderer == null || spriteRenderer.sprite == null)
            {
                reason = string.Empty;
                return true;
            }

            if (spriteRenderer.gameObject.name != "Visual")
            {
                reason = "sprite renderer must be on a child named Visual";
                return false;
            }

            Material material = spriteRenderer.sharedMaterial;
            if (material == null) { reason = "Visual sprite renderer has no assigned Material"; return false; }
            if (material.mainTexture == null && spriteRenderer.sprite.texture == null) { reason = "Visual Material has no texture"; return false; }
            if (!material.enableInstancing) { reason = "Visual Material does not have GPU instancing enabled"; return false; }
            if (material.shader == null || !material.shader.isSupported) { reason = "Visual Material shader is not supported"; return false; }
            reason = string.Empty;
            return true;
        }

        private T FindChildComponent<T>(string childName) where T : Component
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                if (child.name == childName && child.TryGetComponent(out T component)) return component;
            return null;
        }
    }
}

using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Skills
{
    public sealed class BasicAoePrefab : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Collider2D hurtbox;
        [SerializeField] private VisualEffectAsset spawnEffect;
        [SerializeField] private VfxDataShape spawnEffectShape = VfxDataShape.Circular;
        [SerializeField] private VisualEffectAsset hitEffect;
        [SerializeField] private VfxDataShape hitEffectShape = VfxDataShape.Circular;
        [SerializeField] private VisualEffectAsset expireEffect;
        [SerializeField] private VfxDataShape expireEffectShape = VfxDataShape.Circular;
        [SerializeField] private VisualEffectAsset armingEffect;
        [SerializeField] private VfxDataShape armingEffectShape = VfxDataShape.Circular;

        public SpriteRenderer SpriteRenderer => spriteRenderer;
        public Collider2D Hurtbox => hurtbox;
        public VisualEffectAsset SpawnEffect => spawnEffect;
        public VfxDataShape SpawnEffectShape => spawnEffectShape;
        public VisualEffectAsset HitEffect => hitEffect;
        public VfxDataShape HitEffectShape => hitEffectShape;
        public VisualEffectAsset ExpireEffect => expireEffect;
        public VfxDataShape ExpireEffectShape => expireEffectShape;
        public VisualEffectAsset ArmingEffect => armingEffect;
        public VfxDataShape ArmingEffectShape => armingEffectShape;
        public Sprite Sprite => spriteRenderer != null ? spriteRenderer.sprite : null;
        public Material Material => spriteRenderer != null ? spriteRenderer.sharedMaterial : null;
        public float VisualRotationDegrees => spriteRenderer != null ? spriteRenderer.transform.eulerAngles.z : 0f;

        private void Awake()
        {
            ValidateReferences();
        }

        private void OnValidate()
        {
            spriteRenderer ??= FindChildComponent<SpriteRenderer>("Visual");
            hurtbox ??= FindChildComponent<Collider2D>("Hurtbox");
        }

        public void Configure(
            SpriteRenderer renderer,
            Collider2D hurtboxShape,
            VisualEffectAsset spawnVisualEffect = null,
            VisualEffectAsset hitVisualEffect = null,
            VisualEffectAsset expireVisualEffect = null,
            VisualEffectAsset armingVisualEffect = null,
            VfxDataShape spawnShape = VfxDataShape.Circular,
            VfxDataShape hitShape = VfxDataShape.Circular,
            VfxDataShape expireShape = VfxDataShape.Circular,
            VfxDataShape armingShape = VfxDataShape.Circular)
        {
            spriteRenderer = renderer;
            hurtbox = hurtboxShape;
            spawnEffect = spawnVisualEffect;
            spawnEffectShape = spawnShape;
            hitEffect = hitVisualEffect;
            hitEffectShape = hitShape;
            expireEffect = expireVisualEffect;
            expireEffectShape = expireShape;
            armingEffect = armingVisualEffect;
            armingEffectShape = armingShape;
        }

        public bool IsValidTemplate(out string reason)
        {
            reason = string.Empty;

            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                if (spriteRenderer.gameObject.name != "Visual")
                {
                    reason = "sprite renderer must be on a child named Visual";
                    return false;
                }

                Material mat = spriteRenderer.sharedMaterial;
                if (mat == null)
                {
                    reason = "Visual sprite renderer has no assigned Material";
                    return false;
                }

                if (mat.mainTexture == null && (spriteRenderer.sprite == null || spriteRenderer.sprite.texture == null))
                {
                    reason = "Visual Material has no main texture assigned and sprite has no texture";
                    return false;
                }

                if (!mat.enableInstancing)
                {
                    reason = "Visual Material does not have GPU instancing enabled";
                    return false;
                }

                if (mat.shader == null || !mat.shader.isSupported)
                {
                    reason = $"Visual Material shader is not supported: {mat.shader?.name ?? "(none)"}";
                    return false;
                }
            }

            if (hurtbox == null)
            {
                reason = "missing Hurtbox child collider";
                return false;
            }

            if (hurtbox.gameObject.name != "Hurtbox")
            {
                reason = "hurtbox collider must be on a child named Hurtbox";
                return false;
            }

            if (!CombatTargetShapeUtility.IsSupportedShape(hurtbox))
            {
                reason = $"unsupported hurtbox collider type {hurtbox.GetType().Name}";
                return false;
            }

            return true;
        }

        private void ValidateReferences()
        {
            if (!IsValidTemplate(out string reason))
            {
                throw new MissingReferenceException($"{nameof(BasicAoePrefab)} on {name} has invalid AOE template setup: {reason}.");
            }
        }

        private T FindChildComponent<T>(string childName)
            where T : Component
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name != childName)
                {
                    continue;
                }

                if (children[i].TryGetComponent(out T component))
                {
                    return component;
                }
            }

            return null;
        }
    }
}

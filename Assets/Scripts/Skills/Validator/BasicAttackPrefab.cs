using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Projectiles;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Skills
{
    public sealed class BasicAttackPrefab : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Collider2D hurtbox;
        [SerializeField] private VisualEffectAsset spawnEffect;
        [SerializeField] private VisualEffectAsset hitEffect;
        [SerializeField] private VisualEffectAsset expireEffect;
        [SerializeField] private VisualEffectAsset armingEffect;

        public VisualEffectAsset SpawnEffect => spawnEffect;
        public VisualEffectAsset HitEffect => hitEffect;
        public VisualEffectAsset ExpireEffect => expireEffect;
        public VisualEffectAsset ArmingEffect => armingEffect;

        public Sprite Sprite => spriteRenderer != null ? spriteRenderer.sprite : null;
        public Material Material => spriteRenderer != null ? spriteRenderer.sharedMaterial : null;
        public float VisualScale => spriteRenderer != null
            ? Mathf.Max(Mathf.Abs(spriteRenderer.transform.lossyScale.x), Mathf.Abs(spriteRenderer.transform.lossyScale.y))
            : 1f;
        public float VisualRotationDegrees => spriteRenderer != null ? spriteRenderer.transform.eulerAngles.z : 0f;
        public float Radius => ProjectileTargetShapeUtility.Radius(hurtbox);
        public Vector2 HalfExtents => ProjectileTargetShapeUtility.HalfExtents(hurtbox);
        public float RotationRadians => ProjectileTargetShapeUtility.RotationRadians(hurtbox);
        public CombatShapeType ShapeType => ProjectileTargetShapeUtility.ShapeType(hurtbox);

        private void Awake()
        {
            ValidateReferences();
        }

        private void OnValidate()
        {
            spriteRenderer ??= FindChildComponent<SpriteRenderer>("Visual");
            hurtbox ??= FindChildComponent<Collider2D>("Hurtbox");
        }

        public void Configure(SpriteRenderer renderer, Collider2D hurtboxShape)
        {
            spriteRenderer = renderer;
            hurtbox = hurtboxShape;
        }

        public bool IsValidTemplate(out string reason)
        {
            reason = string.Empty;

            if (spriteRenderer == null)
            {
                reason = "missing Visual child sprite renderer";
                return false;
            }

            if (spriteRenderer.gameObject.name != "Visual")
            {
                reason = "sprite renderer must be on a child named Visual";
                return false;
            }

            if (spriteRenderer.sprite == null)
            {
                reason = "sprite renderer has no sprite";
                return false;
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

            if (!ProjectileTargetShapeUtility.IsSupportedShape(hurtbox))
            {
                reason = $"unsupported hurtbox collider type {hurtbox.GetType().Name}";
                return false;
            }

            // Material validation: ensure material supports instancing and has either a main texture or the sprite provides a texture
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

            return true;
        }

        private void ValidateReferences()
        {
            if (!IsValidTemplate(out string reason))
            {
                throw new MissingReferenceException($"{nameof(BasicAttackPrefab)} on {name} has invalid projectile template setup: {reason}.");
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

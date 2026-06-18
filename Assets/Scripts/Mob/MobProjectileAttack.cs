using PlayGround.Common;
using PlayGround.Skills;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using UnityEngine;

namespace PlayGround.Mob
{
    public sealed class MobProjectileAttack
    {
        private readonly MobRoot owner;
        private readonly CombatRoot projectileRoot;
        private readonly CombatVfxRoot vfxRoot;
        private readonly float cooldownSeconds;
        private readonly float range;
        private readonly float spawnOffset;
        private readonly float speed;
        private readonly float lifetimeSeconds;
        private readonly float damage;
        private readonly float radius;
        private readonly CombatShapeType shapeType;
        private readonly BasicAttackPrefab basicPrefab;
        private float cooldownRemaining;

        public MobProjectileAttack(
            MobRoot owner,
            CombatRoot projectileRoot,
            float cooldownSeconds,
            float range,
            float spawnOffset,
            float speed,
            float lifetimeSeconds,
            float damage,
            float radius,
            CombatShapeType shapeType,
            BasicAttackPrefab basicPrefab = null,
            CombatVfxRoot vfxRoot = null)
        {
            this.owner = owner;
            this.projectileRoot = projectileRoot;
            this.cooldownSeconds = Mathf.Max(0.01f, cooldownSeconds);
            this.range = Mathf.Max(1f, range);
            this.spawnOffset = spawnOffset;
            this.speed = Mathf.Max(1f, speed);
            this.lifetimeSeconds = Mathf.Max(0.05f, lifetimeSeconds);
            this.damage = Mathf.Max(0f, damage);
            this.radius = Mathf.Max(0.01f, radius);
            this.shapeType = shapeType;
            this.basicPrefab = basicPrefab;
            this.vfxRoot = vfxRoot;
            if (basicPrefab != null)
            {
                int typeId = projectileRoot.RegisterTemplate(basicPrefab);
                if (vfxRoot != null)
                {
                    vfxRoot.Register(typeId, 0, basicPrefab.SpawnEffect);
                    vfxRoot.Register(typeId, 1, basicPrefab.HitEffect);
                    vfxRoot.Register(typeId, 2, basicPrefab.ExpireEffect);
                }
            }
        }

        public void Update(float deltaTime, Transform target)
        {
            cooldownRemaining = Mathf.Max(0f, cooldownRemaining - deltaTime);
            if (projectileRoot == null || cooldownRemaining > 0f || target == null)
            {
                return;
            }

            Vector2 aim = (Vector2)target.position - (Vector2)owner.transform.position;
            float distance = aim.magnitude;
            if (distance <= 0.001f || distance > range)
            {
                return;
            }

            Vector2 direction = aim / distance;
            Vector2 spawnPosition = (Vector2)owner.transform.position + direction * spawnOffset;
            projectileRoot.Spawn(new ProjectileSpawnRequest(
                spawnPosition,
                direction,
                speed,
                lifetimeSeconds,
                ProjectileRadius,
                ProjectileHalfExtents,
                ProjectileRotationRadians,
                new DamageSnapshot(damage),
                ProjectileShape,
                ProjectileTypeId,
                targetMask: projectileRoot.TargetMask,
                sourceNodeId: owner.ProjectileHitNodeId));
            cooldownRemaining = cooldownSeconds;
        }

        private float ProjectileRadius => basicPrefab != null ? basicPrefab.Radius : radius;
        private Vector2 ProjectileHalfExtents => basicPrefab != null ? basicPrefab.HalfExtents : new Vector2(radius, radius);
        private float ProjectileRotationRadians => basicPrefab != null ? basicPrefab.RotationRadians : 0f;
        private CombatShapeType ProjectileShape => basicPrefab != null ? basicPrefab.ShapeType : shapeType;
        private int ProjectileTypeId => basicPrefab != null ? projectileRoot.RegisterTemplate(basicPrefab) : 0;
    }
}

using PlayGround.Common;
using PlayGround.Skills.Runtime;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Skills
{
    public static class SkillSpawnTranslator
    {
        private const int MaxAoeOnHitSpawnDepth = AoeOnHitSpawnSnapshot.MaxStackChainLinks;

        public static void Spawn(
            RuntimeSkillDefinition def,
            Vector2 origin,
            Vector2 aimDir,
            Vector2 aimWorldPos,
            CombatRoot combatRoot)
        {
            Spawn(def, origin, aimDir, aimWorldPos, combatRoot, default);
        }

        private static void Spawn(
            RuntimeSkillDefinition def,
            Vector2 origin,
            Vector2 aimDir,
            Vector2 aimWorldPos,
            CombatRoot combatRoot,
            StackEffectSnapshot stackEffect)
        {
            if (def is RuntimeProjectileDefinition proj)
                SpawnProjectile(proj, origin, aimDir, combatRoot, stackEffect);
            else if (def is RuntimeAoeDefinition aoe)
                SpawnAoe(aoe, origin, aimWorldPos, combatRoot, stackEffect);
        }

        private static void SpawnProjectile(
            RuntimeProjectileDefinition def,
            Vector2 origin,
            Vector2 aimDir,
            CombatRoot root,
            StackEffectSnapshot stackEffect)
        {
            if (root == null || def.Prefab == null || def.TypeId < 0) return;

            BasicAttackPrefab prefab = def.Prefab;
            DamageSnapshot damage = new(Mathf.Max(0f, def.Damage));
            Vector2 baseDir = aimDir.sqrMagnitude > 0f ? aimDir.normalized : Vector2.right;
            int targetMask = root.TargetMask;

            StackEffectSnapshot effectiveStackEffect = BuildApplicatorStackEffectSnapshot(def, root, stackEffect);
            ProjectileImpactAoeSnapshot impactAoe = BuildImpactAoeSnapshot(def, root);
            ProjectileImpactProjectileSnapshot impactProjectile = BuildImpactProjectileSnapshot(def, root);
            ProjectileChildSpawnConfig childSpawn = ProjectileChildSpawnFromSetup(def.ChildSpawnSetup);
            TimedSpawnComponent timedSpawn = ProjectileTimedSpawnFromSetup(def.ChildSpawnSetup);
            TimedSpawnComponent aoeTimedSpawn = ProjectileAoeTimedSpawnFromSetup(def.AoeIntervalSpawnSetup);
            if (IsTimedSpawnEnabled(aoeTimedSpawn))
            {
                timedSpawn = aoeTimedSpawn;
            }
            IntervalChildKind childKind = timedSpawn.ChildKind == IntervalChildKind.Aoe
                ? IntervalChildKind.Aoe
                : IntervalChildKind.Projectile;

            root.Spawn(new ProjectileSpawnRequest(
                origin,
                baseDir,
                def.Speed,
                def.Lifetime,
                prefab.Radius,
                prefab.HalfExtents,
                prefab.RotationRadians,
                damage,
                prefab.ShapeType,
                def.TypeId,
                targetMask,
                def.PierceCount,
                def.RepeatHitCooldown,
                def.Tracking,
                childSpawn,
                def.DirectDamageEnabled,
                default,
                impactAoe,
                effectiveStackEffect,
                impactProjectile,
                def.CritChance,
                def.CritMultiplier,
                def.Count,
                def.SpreadDegrees,
                def.JitterDegrees,
                timedSpawn,
                childKind));
        }

        private static void SpawnAoe(
            RuntimeAoeDefinition def,
            Vector2 origin,
            Vector2 aimWorldPos,
            CombatRoot root,
            StackEffectSnapshot stackEffect)
        {
            if (root == null || def.TypeId < 0) return;

            Vector2 center = aimWorldPos;
            DamageSnapshot damage = new(Mathf.Max(0f, def.Damage));
            int count = Mathf.Max(1, def.Count);
            AoeSpawnGeometry geometry = def.CreateSpawnGeometry();
            StackEffectSnapshot effectiveStackEffect = BuildApplicatorStackEffectSnapshot(def, root, stackEffect);
            TimedSpawnComponent timedSpawn = AoeTimedSpawnFromSetup(def);
            bool hasTimedSpawner = IsTimedSpawnEnabled(timedSpawn);

            for (int i = 0; i < count; i++)
            {
                root.Spawn(new AoeSpawnRequest(
                    def.TypeId,
                    center,
                    root.TargetMask,
                    damage,
                    def.LifetimeSeconds,
                    def.TickIntervalSeconds,
                    geometry,
                    projectileBurst: def.OnHitProjectileSpawnDefinition != null
                        ? BuildProjectileDetonationBurstSnapshot(def.OnHitProjectileSpawnDefinition, root.TargetMask)
                        : default,
                    aoeSpawn: BuildAoeOnHitSpawnSnapshot(def.OnHitAoeSpawnDefinition, root, MaxAoeOnHitSpawnDepth),
                    critChance: def.CritChance,
                    critMultiplier: def.CritMultiplier,
                    stackEffect: effectiveStackEffect,
                    hasTimedSpawner: hasTimedSpawner,
                    timedSpawn: timedSpawn));
            }
        }

        private static TimedSpawnComponent ProjectileTimedSpawnFromSetup(RuntimeChildSpawnSetup setup)
        {
            RuntimeProjectileDefinition child = setup?.ChildDefinition;
            if (setup == null
                || child == null
                || child.TypeId < 0
                || child.Prefab == null
                || IsDefault(setup.TemplateKey))
            {
                return default;
            }

            return new TimedSpawnComponent
            {
                ChildKind = IntervalChildKind.Projectile,
                JitterSeed = setup.JitterSeed,
                IntervalSeconds = Mathf.Max(0.01f, setup.IntervalSeconds),
                IntervalJitterSeconds = Mathf.Max(0f, setup.IntervalJitterSeconds),
                TemplateKey = setup.TemplateKey
            };
        }

        private static TimedSpawnComponent ProjectileAoeTimedSpawnFromSetup(
            RuntimeAoeIntervalSpawnSetup setup)
        {
            RuntimeAoeDefinition child = setup?.ChildDefinition;
            if (setup == null || child == null || child.TypeId < 0 || IsDefault(setup.TemplateKey))
            {
                return default;
            }

            return new TimedSpawnComponent
            {
                ChildKind = IntervalChildKind.Aoe,
                JitterSeed = setup.JitterSeed,
                IntervalSeconds = Mathf.Max(0.01f, setup.IntervalSeconds),
                IntervalJitterSeconds = Mathf.Max(0f, setup.IntervalJitterSeconds),
                TemplateKey = setup.TemplateKey
            };
        }

        private static TimedSpawnComponent AoeTimedSpawnFromSetup(
            RuntimeAoeDefinition def)
        {
            RuntimeChildSpawnSetup projectileSetup = def.ChildSpawnSetup;
            RuntimeProjectileDefinition projectileChild = projectileSetup?.ChildDefinition;
            if (projectileSetup != null
                && projectileChild != null
                && projectileChild.TypeId >= 0
                && projectileChild.Prefab != null
                && !IsDefault(projectileSetup.TemplateKey))
            {
                return new TimedSpawnComponent
                {
                    JitterSeed = projectileSetup.JitterSeed,
                    ChildKind = IntervalChildKind.Projectile,
                    IntervalSeconds = Mathf.Max(0.01f, projectileSetup.IntervalSeconds),
                    IntervalJitterSeconds = Mathf.Max(0f, projectileSetup.IntervalJitterSeconds),
                    TemplateKey = projectileSetup.TemplateKey
                };
            }

            RuntimeAoeIntervalSpawnSetup aoeSetup = def.AoeIntervalSpawnSetup;
            RuntimeAoeDefinition aoeChild = aoeSetup?.ChildDefinition;
            if (aoeSetup == null || aoeChild == null || aoeChild.TypeId < 0 || IsDefault(aoeSetup.TemplateKey))
            {
                return default;
            }

            return new TimedSpawnComponent
            {
                JitterSeed = aoeSetup.JitterSeed,
                ChildKind = IntervalChildKind.Aoe,
                IntervalSeconds = Mathf.Max(0.01f, aoeSetup.IntervalSeconds),
                IntervalJitterSeconds = Mathf.Max(0f, aoeSetup.IntervalJitterSeconds),
                TemplateKey = aoeSetup.TemplateKey
            };
        }

        private static bool IsTimedSpawnEnabled(TimedSpawnComponent timedSpawn) =>
            timedSpawn.JitterSeed > 0
            && timedSpawn.IntervalSeconds > 0f
            && !IsDefault(timedSpawn.TemplateKey);

        private static ProjectileChildSpawnConfig ProjectileChildSpawnFromSetup(RuntimeChildSpawnSetup setup)
        {
            RuntimeProjectileDefinition child = setup?.ChildDefinition;
            if (setup == null
                || child == null
                || child.TypeId < 0
                || child.Prefab == null
                || IsDefault(setup.TemplateKey))
            {
                return ProjectileChildSpawnConfig.Disabled;
            }

            return new ProjectileChildSpawnConfig(
                setup.JitterSeed,
                child.TypeId,
                Mathf.Max(0.01f, setup.IntervalSeconds),
                Mathf.Max(0f, setup.IntervalJitterSeconds),
                speed: 0f,
                lifetime: 0f,
                radius: 0f,
                halfExtents: default,
                shapeType: CombatShapeType.Circle,
                rotationRadians: 0f,
                damage: default,
                templateKey: setup.TemplateKey);
        }

        private static bool IsDefault(Unity.Entities.Hash128 key) => key.Equals(default(Unity.Entities.Hash128));

        private static ProjectileImpactAoeSnapshot BuildImpactAoeSnapshot(
            RuntimeProjectileDefinition def,
            CombatRoot root)
        {
            RuntimeAoeDefinition impact = def.ImpactAoeDefinition;
            if (impact == null || impact.TypeId < 0)
                return default;

            int targetMask = root != null ? root.TargetMask : 0;
            return new ProjectileImpactAoeSnapshot(
                impact.TypeId,
                targetMask,
                Mathf.Max(0f, impact.Damage),
                impact.LifetimeSeconds,
                impact.TickIntervalSeconds,
                impact.CreateSpawnGeometry(),
                impact.CritChance,
                impact.CritMultiplier,
                stackEffect: BuildApplicatorStackEffectSnapshot(impact, root),
                aoeSpawn: BuildAoeOnHitSpawnSnapshot(impact.OnHitAoeSpawnDefinition, root, MaxAoeOnHitSpawnDepth));
        }

        private static ProjectileImpactProjectileSnapshot BuildImpactProjectileSnapshot(
            RuntimeProjectileDefinition def,
            CombatRoot root)
        {
            RuntimeProjectileDefinition impact = def.ImpactProjectileDefinition;
            if (impact == null || impact.TypeId < 0)
                return default;

            BasicAttackPrefab prefab = impact.Prefab;
            if (prefab == null)
                return default;

            int targetMask = root != null ? root.TargetMask : 0;
            return new ProjectileImpactProjectileSnapshot(
                impact.TypeId,
                targetMask,
                Mathf.Max(1, impact.Count),
                impact.SpreadDegrees,
                impact.Speed,
                impact.Lifetime,
                prefab.Radius,
                prefab.HalfExtents,
                prefab.RotationRadians,
                prefab.ShapeType,
                new DamageSnapshot(Mathf.Max(0f, impact.Damage)),
                impact.DirectDamageEnabled,
                impact.PierceCount,
                impact.RepeatHitCooldown,
                impact.Tracking,
                BuildImpactAoeSnapshot(impact, root),
                BuildApplicatorStackEffectSnapshot(impact, root),
                visualScale: prefab.VisualScale,
                visualRotationDegrees: prefab.VisualRotationDegrees);
        }

        private static StackEffectSnapshot BuildApplicatorStackEffectSnapshot(
            RuntimeSkillDefinition def,
            CombatRoot root,
            StackEffectSnapshot fallback = default)
        {
            StackEffectSnapshot stackEffect = default;
            if (def is RuntimeProjectileDefinition projectile)
                stackEffect = BuildStackEffectSnapshot(projectile.StackingDetonation, root);
            else if (def is RuntimeAoeDefinition aoe)
                stackEffect = BuildStackEffectSnapshot(aoe.StackingDetonation, root);

            return stackEffect.Enabled ? stackEffect : fallback;
        }

        private static StackEffectSnapshot BuildStackEffectSnapshot(
            RuntimeStackingDetonation stacking,
            CombatRoot root)
        {
            if (stacking == null || root == null || stacking.DebuffKey < 0)
                return default;

            int threshold = Mathf.Max(1, stacking.StackThreshold);
            if (stacking.Detonation is RuntimeAoeDefinition aoe && aoe.TypeId >= 0)
                return BuildAoeStackEffectSnapshot(stacking, root, aoe, threshold);

            if (stacking.Detonation is RuntimeProjectileDefinition projectile
                && projectile.TypeId >= 0
                && projectile.Prefab != null)
            {
                return BuildProjectileStackEffectSnapshot(stacking, root, projectile, threshold);
            }

            return default;
        }

        private static StackEffectSnapshot BuildAoeStackEffectSnapshot(
            RuntimeStackingDetonation stacking,
            CombatRoot root,
            RuntimeAoeDefinition aoe,
            int threshold)
        {
            AoeSpawnGeometry geometry = aoe.CreateSpawnGeometry();
            AoeOnHitSpawnSnapshot onHitSpawn = BuildAoeOnHitSpawnSnapshot(
                aoe.OnHitAoeSpawnDefinition,
                root,
                MaxAoeOnHitSpawnDepth);
            float stacksPerHit = Mathf.Max(1, stacking.StacksPerHit);
            return new StackEffectSnapshot
            {
                DebuffKey = stacking.DebuffKey,
                Threshold = threshold,
                Lifetime = Mathf.Max(0f, stacking.DebuffLifetimeSeconds),
                Contribution = new StackContribution
                {
                    Damage = Mathf.Max(0f, aoe.Damage) * stacksPerHit / threshold,
                    ProjectileCount = 0,
                    AreaSize = Mathf.Max(0.01f, aoe.AreaSize) * stacksPerHit / threshold
                },
                Detonation = new DetonationSnapshot
                {
                    Kind = StackDetonationKind.Aoe,
                    Faction = root.Faction,
                    TargetMask = root.TargetMask,
                    TypeId = aoe.TypeId,
                    LifetimeSeconds = aoe.LifetimeSeconds,
                    TickIntervalSeconds = aoe.TickIntervalSeconds,
                    AoeGeometry = geometry,
                    CritChance = aoe.CritChance,
                    CritMultiplier = aoe.CritMultiplier,
                    AoeOnHitSpawn = onHitSpawn
                }
            };
        }

        private static StackEffectSnapshot BuildProjectileStackEffectSnapshot(
            RuntimeStackingDetonation stacking,
            CombatRoot root,
            RuntimeProjectileDefinition projectile,
            int threshold)
        {
            float stacksPerHit = Mathf.Max(1, stacking.StacksPerHit);
            return new StackEffectSnapshot
            {
                DebuffKey = stacking.DebuffKey,
                Threshold = threshold,
                Lifetime = Mathf.Max(0f, stacking.DebuffLifetimeSeconds),
                Contribution = new StackContribution
                {
                    Damage = Mathf.Max(0f, projectile.Damage) * stacksPerHit / threshold,
                    ProjectileCount = Mathf.Max(1, Mathf.RoundToInt(projectile.Count * stacksPerHit)),
                    AreaSize = 0f
                },
                Detonation = new DetonationSnapshot
                {
                    Kind = StackDetonationKind.Projectile,
                    Faction = root.Faction,
                    TargetMask = root.TargetMask,
                    TypeId = projectile.TypeId,
                    LifetimeSeconds = projectile.Lifetime,
                    TickIntervalSeconds = 0f,
                    AoeGeometry = default,
                    ProjectileBurst = BuildProjectileDetonationBurstSnapshot(projectile, root.TargetMask),
                    CritChance = 0f,
                    CritMultiplier = 1.5f,
                    AoeOnHitSpawn = default
                }
            };
        }

        private static AoeProjectileBurstSnapshot BuildProjectileDetonationBurstSnapshot(
            RuntimeProjectileDefinition projectile,
            int targetMask)
        {
            BasicAttackPrefab prefab = projectile.Prefab;
            if (prefab == null || projectile.TypeId < 0)
                return default;

            return new AoeProjectileBurstSnapshot(
                projectile.TypeId,
                targetMask,
                Mathf.Max(1, projectile.Count),
                projectile.SpreadDegrees,
                projectile.Speed,
                projectile.Lifetime,
                prefab.Radius,
                prefab.HalfExtents,
                prefab.RotationRadians,
                prefab.ShapeType,
                new DamageSnapshot(Mathf.Max(0f, projectile.Damage)),
                projectile.DirectDamageEnabled,
                projectile.PierceCount,
                projectile.RepeatHitCooldown,
                prefab.VisualScale,
                prefab.VisualRotationDegrees);
        }

        private static AoeOnHitSpawnSnapshot BuildAoeOnHitSpawnSnapshot(
            RuntimeSkillDefinition def,
            CombatRoot root,
            int remainingLinks)
        {
            if (def == null || root == null || remainingLinks <= 0)
                return default;

            if (def is RuntimeAoeDefinition aoe)
                return BuildPlainAoeOnHitSpawnSnapshot(
                    aoe,
                    root,
                    BuildStackPayloadFor(aoe.StackingDetonation, root));

            return default;
        }

        private static AoeOnHitSpawnTailSnapshot BuildAoeOnHitSpawnTailSnapshot(
            RuntimeSkillDefinition def,
            CombatRoot root)
        {
            if (def == null || root == null)
                return default;

            if (def is RuntimeAoeDefinition aoe)
                return BuildPlainAoeOnHitSpawnTailSnapshot(
                    aoe,
                    root,
                    BuildStackPayloadFor(aoe.StackingDetonation, root));

            return default;
        }

        private static AoeOnHitSpawnSnapshot BuildPlainAoeOnHitSpawnSnapshot(
            RuntimeAoeDefinition aoe,
            CombatRoot root,
            StackPayload stackPayload,
            AoeOnHitSpawnTailSnapshot tail = default)
        {
            if (aoe == null || root == null || aoe.TypeId < 0)
                return default;

            return new AoeOnHitSpawnSnapshot(
                aoe.TypeId,
                root.TargetMask,
                Mathf.Max(0f, aoe.Damage),
                aoe.DirectDamageEnabled,
                aoe.LifetimeSeconds,
                aoe.TickIntervalSeconds,
                aoe.CreateSpawnGeometry(),
                aoe.CritChance,
                aoe.CritMultiplier,
                stackPayload.DebuffKey,
                stackPayload.Threshold,
                stackPayload.Lifetime,
                stackPayload.Contribution,
                stackPayload.DetonationKind,
                stackPayload.DetonationTypeId,
                stackPayload.DetonationLifetimeSeconds,
                stackPayload.DetonationTickIntervalSeconds,
                stackPayload.DetonationAoeGeometry,
                stackPayload.DetonationCritChance,
                stackPayload.DetonationCritMultiplier,
                tail);
        }

        private static AoeOnHitSpawnTailSnapshot BuildPlainAoeOnHitSpawnTailSnapshot(
            RuntimeAoeDefinition aoe,
            CombatRoot root,
            StackPayload stackPayload)
        {
            if (aoe == null || root == null || aoe.TypeId < 0)
                return default;

            return new AoeOnHitSpawnTailSnapshot(
                aoe.TypeId,
                root.TargetMask,
                Mathf.Max(0f, aoe.Damage),
                aoe.DirectDamageEnabled,
                aoe.LifetimeSeconds,
                aoe.TickIntervalSeconds,
                aoe.CreateSpawnGeometry(),
                aoe.CritChance,
                aoe.CritMultiplier,
                stackPayload.DebuffKey,
                stackPayload.Threshold,
                stackPayload.Lifetime,
                stackPayload.Contribution,
                stackPayload.DetonationKind,
                stackPayload.DetonationTypeId,
                stackPayload.DetonationLifetimeSeconds,
                stackPayload.DetonationTickIntervalSeconds,
                stackPayload.DetonationAoeGeometry,
                stackPayload.DetonationCritChance,
                stackPayload.DetonationCritMultiplier);
        }

        private static StackPayload BuildStackPayloadFor(
            RuntimeStackingDetonation stacking,
            CombatRoot root)
        {
            if (stacking == null
                || root == null
                || stacking.DebuffKey < 0
                || stacking.Detonation is not RuntimeAoeDefinition detonation
                || detonation.TypeId < 0)
            {
                return default;
            }

            int threshold = Mathf.Max(1, stacking.StackThreshold);
            float stacksPerHit = Mathf.Max(1, stacking.StacksPerHit);
            return new StackPayload
            {
                DebuffKey = stacking.DebuffKey,
                Threshold = threshold,
                Lifetime = Mathf.Max(0f, stacking.DebuffLifetimeSeconds),
                Contribution = new StackContribution
                {
                    Damage = Mathf.Max(0f, detonation.Damage) * stacksPerHit / threshold,
                    ProjectileCount = 0,
                    AreaSize = Mathf.Max(0.01f, detonation.AreaSize) * stacksPerHit / threshold
                },
                DetonationKind = StackDetonationKind.Aoe,
                DetonationTypeId = detonation.TypeId,
                DetonationLifetimeSeconds = detonation.LifetimeSeconds,
                DetonationTickIntervalSeconds = detonation.TickIntervalSeconds,
                DetonationAoeGeometry = detonation.CreateSpawnGeometry(),
                DetonationCritChance = detonation.CritChance,
                DetonationCritMultiplier = detonation.CritMultiplier
            };
        }

        private struct StackPayload
        {
            public int DebuffKey;
            public int Threshold;
            public float Lifetime;
            public StackContribution Contribution;
            public StackDetonationKind DetonationKind;
            public int DetonationTypeId;
            public float DetonationLifetimeSeconds;
            public float DetonationTickIntervalSeconds;
            public AoeSpawnGeometry DetonationAoeGeometry;
            public float DetonationCritChance;
            public float DetonationCritMultiplier;
        }
    }
}

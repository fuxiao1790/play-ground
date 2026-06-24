using PlayGround.Common;
using PlayGround.Skills.Runtime;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using UnityEngine;
using AoeSourceIntervalSpawnerComponent = PlayGround.System.Aoe.AoeIntervalSpawnerComponent;
using ProjectileAoeIntervalSpawnerComponent = PlayGround.System.Projectile.AoeIntervalSpawnerComponent;

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
            ProjectileChildSpawnConfig childSpawn = def.BuildChildSpawnConfig(
                BuildApplicatorStackEffectSnapshot(def.ChildSpawnSetup?.ChildDefinition, root));
            ProjectileAoeIntervalSpawnerComponent aoeIntervalSpawner =
                BuildProjectileSourceAoeIntervalSpawner(def, root);
            IntervalChildKind childKind = IsProjectileAoeIntervalSpawnerEnabled(aoeIntervalSpawner)
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
                aoeIntervalSpawner,
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
            AoeSourceIntervalSpawnerComponent intervalSpawner = BuildAoeSourceIntervalSpawner(def, root);
            bool hasIntervalSpawner = IsAoeSourceIntervalSpawnerEnabled(intervalSpawner);

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
                    aoeSpawn: BuildAoeOnHitSpawnSnapshot(def.OnHitAoeSpawnDefinition, root, MaxAoeOnHitSpawnDepth),
                    critChance: def.CritChance,
                    critMultiplier: def.CritMultiplier,
                    stackEffect: effectiveStackEffect,
                    hasIntervalSpawner: hasIntervalSpawner,
                    intervalSpawner: intervalSpawner));
            }
        }

        private static ProjectileAoeIntervalSpawnerComponent BuildProjectileSourceAoeIntervalSpawner(
            RuntimeProjectileDefinition def,
            CombatRoot root)
        {
            RuntimeAoeIntervalSpawnSetup setup = def.AoeIntervalSpawnSetup;
            RuntimeAoeDefinition child = setup?.ChildDefinition;
            if (setup == null || child == null || child.TypeId < 0)
            {
                return default;
            }

            return new ProjectileAoeIntervalSpawnerComponent
            {
                SpawnerId = setup.SpawnerId,
                IntervalSeconds = Mathf.Max(0.01f, setup.IntervalSeconds),
                IntervalJitterSeconds = Mathf.Max(0f, setup.IntervalJitterSeconds),
                Child = BuildIntervalAoeChild(
                    child,
                    Mathf.Max(1, setup.Count),
                    root,
                    BuildApplicatorStackEffectSnapshot(child, root))
            };
        }

        private static AoeSourceIntervalSpawnerComponent BuildAoeSourceIntervalSpawner(
            RuntimeAoeDefinition def,
            CombatRoot root)
        {
            RuntimeChildSpawnSetup projectileSetup = def.ChildSpawnSetup;
            RuntimeProjectileDefinition projectileChild = projectileSetup?.ChildDefinition;
            if (projectileSetup != null
                && projectileChild != null
                && projectileChild.TypeId >= 0
                && projectileChild.Prefab != null)
            {
                return new AoeSourceIntervalSpawnerComponent
                {
                    SpawnerId = projectileSetup.SpawnerId,
                    ChildKind = IntervalChildKind.Projectile,
                    IntervalSeconds = Mathf.Max(0.01f, projectileSetup.IntervalSeconds),
                    IntervalJitterSeconds = Mathf.Max(0f, projectileSetup.IntervalJitterSeconds),
                    ProjectileChild = BuildIntervalProjectileChild(
                        projectileChild,
                        projectileSetup.Behavior,
                        root,
                        BuildApplicatorStackEffectSnapshot(projectileChild, root))
                };
            }

            RuntimeAoeIntervalSpawnSetup aoeSetup = def.AoeIntervalSpawnSetup;
            RuntimeAoeDefinition aoeChild = aoeSetup?.ChildDefinition;
            if (aoeSetup == null || aoeChild == null || aoeChild.TypeId < 0)
            {
                return default;
            }

            return new AoeSourceIntervalSpawnerComponent
            {
                SpawnerId = aoeSetup.SpawnerId,
                ChildKind = IntervalChildKind.Aoe,
                IntervalSeconds = Mathf.Max(0.01f, aoeSetup.IntervalSeconds),
                IntervalJitterSeconds = Mathf.Max(0f, aoeSetup.IntervalJitterSeconds),
                AoeChild = BuildIntervalAoeChild(
                    aoeChild,
                    Mathf.Max(1, aoeSetup.Count),
                    root,
                    BuildApplicatorStackEffectSnapshot(aoeChild, root))
            };
        }

        private static IntervalProjectileChild BuildIntervalProjectileChild(
            RuntimeProjectileDefinition child,
            ProjectileChildSpawnBehavior behavior,
            CombatRoot root,
            StackEffectSnapshot stackEffect)
        {
            BasicAttackPrefab prefab = child.Prefab;
            float radians = prefab.VisualRotationDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new IntervalProjectileChild
            {
                TypeId = child.TypeId,
                ChildCountPerTick = Mathf.Max(1, behavior.Count),
                SpawnPatternType = behavior.PatternType,
                SideSpreadDegrees = behavior.SpreadDegrees,
                Speed = child.Speed,
                Lifetime = child.Lifetime,
                Radius = prefab.Radius,
                HalfExtents = new Unity.Mathematics.float2(prefab.HalfExtents.x, prefab.HalfExtents.y),
                RotationRadians = prefab.RotationRadians,
                ShapeType = prefab.ShapeType,
                DamageAmount = Mathf.Max(0f, child.Damage),
                DirectDamageEnabled = child.DirectDamageEnabled,
                PierceCount = child.PierceCount,
                RepeatHitCooldownSeconds = child.RepeatHitCooldown,
                VisualScale = prefab.VisualScale > 0f ? prefab.VisualScale : 1f,
                VisualRotationSin = sin,
                VisualRotationCos = cos,
                TrackingEnabled = child.Tracking.Enabled,
                TrackingTurnSpeedRadians = child.Tracking.TurnSpeedDegrees * Mathf.Deg2Rad,
                TrackingQueryIntervalSeconds = child.Tracking.QueryIntervalSeconds,
                TrackingInitialQueryDelaySeconds = child.Tracking.InitialQueryDelaySeconds,
                SourceNodeId = default,
                ImpactAoe = BuildImpactAoeSnapshot(child, root),
                StackEffect = stackEffect,
                ImpactProjectile = BuildImpactProjectileSnapshot(child, root)
            };
        }

        private static IntervalAoeChild BuildIntervalAoeChild(
            RuntimeAoeDefinition child,
            int count,
            CombatRoot root,
            StackEffectSnapshot stackEffect)
        {
            AoeSpawnGeometry geometry = child.CreateSpawnGeometry();
            return new IntervalAoeChild
            {
                TypeId = child.TypeId,
                Lifetime = child.LifetimeSeconds,
                RepeatHitCooldownSeconds = child.TickIntervalSeconds,
                Radius = geometry.Radius,
                HalfExtents = new Unity.Mathematics.float2(geometry.HalfExtents.x, geometry.HalfExtents.y),
                RotationRadians = geometry.RotationRadians,
                ShapeType = geometry.ShapeType,
                AreaSize = geometry.AreaSize,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = Mathf.Max(0f, child.Damage),
                    CritChance = child.CritChance,
                    CritMultiplier = child.CritMultiplier,
                    DirectDamageEnabled = child.DirectDamageEnabled,
                    SourceNodeId = default,
                    StackEffect = stackEffect
                },
                ProjectileBurst = default,
                AoeSpawn = BuildAoeOnHitSpawnSnapshot(child.OnHitAoeSpawnDefinition, root, MaxAoeOnHitSpawnDepth),
                Render = AoeRenderComponentFor(geometry),
                Count = Mathf.Max(1, count)
            };
        }

        private static CombatRenderComponent AoeRenderComponentFor(AoeSpawnGeometry geometry)
        {
            if (geometry.VisualScale.x <= 0f && geometry.VisualScale.y <= 0f)
            {
                return default;
            }

            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 0,
                VisualScale = new Unity.Mathematics.float2(geometry.VisualScale.x, geometry.VisualScale.y),
                VisualRotationSin = geometry.VisualRotationSin,
                VisualRotationCos = geometry.VisualRotationCos,
                RenderZ = CombatRoot.AoeRenderZ
            };
        }

        private static bool IsProjectileAoeIntervalSpawnerEnabled(ProjectileAoeIntervalSpawnerComponent spawner) =>
            spawner.SpawnerId > 0 && spawner.IntervalSeconds > 0f;

        private static bool IsAoeSourceIntervalSpawnerEnabled(AoeSourceIntervalSpawnerComponent spawner) =>
            spawner.SpawnerId > 0 && spawner.IntervalSeconds > 0f;

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

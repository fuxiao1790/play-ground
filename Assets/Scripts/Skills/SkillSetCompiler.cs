using System.Collections.Generic;
using PlayGround.Skills.Modifiers;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Projectiles;
using UnityEngine;

namespace PlayGround.Skills
{
    public static class SkillSetCompiler
    {
        private static int nextChildJitterSeed;

        public static RuntimeSkillDefinition Compile(
            IReadOnlyList<LoadoutSlot> slots,
            int slotIndex,
            TriggerChain[] allChains,
            SkillStatSnapshot snapshot)
        {
            SkillSet set = GetSkillSet(slots, slotIndex);
            if (set == null || set.Skill == null) return null;

            CompileDefinitionResult compiled = CompileDefinition(set.Skill.Definition, set.Supports, snapshot);
            RuntimeSkillDefinition runtime = compiled.Runtime;
            if (runtime == null) return null;

            if (runtime is RuntimeStackingDetonation rootStackingDetonation)
                rootStackingDetonation.DebuffName = set.Skill.name;

            runtime.RecoveryTime = ResolveRecoveryTime(set.Skill.BaseRate, compiled.Modifiers);

            // A stacking-detonation set wraps its spawned definition in a
            // RuntimeStackingDetonation. The wrapper itself is never spawned, so its
            // outgoing triggers (including a downstream StackTrigger) must attach to
            // the inner detonation. That inner projectile/AOE is the entity that
            // spawns and hits targets, and thus the applicator for the next link.
            RuntimeSkillDefinition triggerHost = runtime is RuntimeStackingDetonation stackingHost
                ? stackingHost.Detonation
                : runtime;

            // Adjacency is forward-only (i -> i+2); recursion terminates by strictly
            // increasing slot index. No cycle is possible, so no recursion guard is needed.
            foreach (TriggerChain chain in allChains)
            {
                if (chain == null || chain.causeIndex != slotIndex || chain.link == null) continue;

                if (chain.link is ProjectileIntervalSpawnTrigger childTrigger)
                {
                    ApplyChildSpawn(triggerHost, childTrigger, slots, chain.effectIndex, allChains, snapshot);
                    continue;
                }

                if (chain.link is AoeIntervalSpawnTrigger aoeIntervalTrigger)
                {
                    ApplyAoeIntervalSpawn(triggerHost, aoeIntervalTrigger, slots, chain.effectIndex, allChains, snapshot);
                    continue;
                }

                if (chain.link is OnImpactAoeTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = Compile(slots, chain.effectIndex, allChains, snapshot);
                    if (compiledTarget is RuntimeAoeDefinition aoeTarget)
                    {
                        if (triggerHost is RuntimeProjectileDefinition projDef)
                            projDef.ImpactAoeDefinition = aoeTarget;
                        else if (triggerHost is RuntimeAoeDefinition sourceAoeDef)
                            sourceAoeDef.OnHitAoeSpawnDefinition = aoeTarget;
                    }
                    continue;
                }

                if (chain.link is OnImpactProjectileTrigger impactProjTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = Compile(slots, chain.effectIndex, allChains, snapshot);
                    if (compiledTarget is RuntimeProjectileDefinition impactProjDef)
                    {
                        impactProjDef.Count = Mathf.Max(1, impactProjDef.Count + impactProjTrigger.spawnCount);
                        impactProjDef.SpreadDegrees = impactProjTrigger.spreadDegrees;

                        if (triggerHost is RuntimeProjectileDefinition projDef)
                            projDef.ImpactProjectileDefinition = impactProjDef;
                        else if (triggerHost is RuntimeAoeDefinition aoeSourceDef)
                            aoeSourceDef.OnHitProjectileSpawnDefinition = impactProjDef;
                    }
                    continue;
                }

                if (chain.link is OnAoeHitSpawnTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = Compile(slots, chain.effectIndex, allChains, snapshot);
                    if (triggerHost is RuntimeAoeDefinition aoeDef)
                        aoeDef.OnHitAoeSpawnDefinition = compiledTarget;
                }

                if (chain.link is StackTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = Compile(slots, chain.effectIndex, allChains, snapshot);
                    if (compiledTarget is not RuntimeStackingDetonation stackingDetonation)
                        continue;

                    if (triggerHost is RuntimeProjectileDefinition projDef)
                        projDef.StackingDetonation = stackingDetonation;
                    else if (triggerHost is RuntimeAoeDefinition aoeDef)
                        aoeDef.StackingDetonation = stackingDetonation;
                }
            }

            return runtime;
        }

        private static CompileDefinitionResult CompileDefinition(
            SkillDefinition definition,
            IReadOnlyList<SkillSupport> supports,
            SkillStatSnapshot snapshot)
        {
            if (definition == null)
                return default;

            SkillDefinition defCopy = definition.DeepCopy();
            var modifiers = new StatModifierAccumulator();
            SnapshotModifiers.Contribute(modifiers, snapshot);
            CollectSupportModifiers(modifiers, supports);
            ApplySupportBehaviors(defCopy, supports);

            RuntimeSkillDefinition runtime = ApplyConversionSupports(
                definition,
                BuildRuntime(defCopy, modifiers, snapshot),
                supports,
                snapshot);

            return new CompileDefinitionResult(runtime, modifiers);
        }

        private static void CollectSupportModifiers(
            StatModifierAccumulator modifiers,
            IReadOnlyList<SkillSupport> supports)
        {
            if (modifiers == null || supports == null)
                return;

            for (int i = 0; i < supports.Count; i++)
            {
                SkillSupport support = supports[i];
                if (support == null)
                    continue;

                if (support is IBaseValueModifier baseValue)
                    baseValue.CollectAdded(new AddedSink(modifiers));

                if (support is IIncreasedModifier increased)
                    increased.CollectIncreases(new IncreasedSink(modifiers));

                if (support is IMultiplierModifier multiplier)
                    multiplier.CollectMultipliers(new MultiplierSink(modifiers));
            }
        }

        private static void ApplySupportBehaviors(
            SkillDefinition definition,
            IReadOnlyList<SkillSupport> supports)
        {
            if (definition == null || supports == null)
                return;

            for (int i = 0; i < supports.Count; i++)
            {
                SkillSupport support = supports[i];
                if (support == null)
                    continue;

                if (definition is ProjectileDefinition projectile
                    && support is IProjectileBehaviorModifier projectileModifier)
                {
                    projectileModifier.ApplyToProjectile(new ProjectileBehaviorContext(projectile));
                }
                else if (definition is AoeDefinitionBase aoe
                         && support is IAoeBehaviorModifier aoeModifier)
                {
                    aoeModifier.ApplyToAoe(new AoeBehaviorContext(aoe));
                }
            }
        }

        private static RuntimeSkillDefinition ApplyConversionSupports(
            SkillDefinition definition,
            RuntimeSkillDefinition runtime,
            IReadOnlyList<SkillSupport> supports,
            SkillStatSnapshot snapshot)
        {
            if (runtime == null || supports == null)
                return runtime;

            for (int i = 0; i < supports.Count; i++)
            {
                if (supports[i] is ConversionSupport conversion)
                    runtime = conversion.Compile(definition, runtime, snapshot);

                if (runtime == null)
                    return null;
            }

            return runtime;
        }

        private static float ResolveRecoveryTime(
            float baseRate,
            StatModifierAccumulator modifiers)
        {
            float rate = modifiers != null
                ? modifiers.Resolve(SkillStat.Rate, baseRate)
                : baseRate;
            return 1f / Mathf.Max(0.01f, rate);
        }

        private static RuntimeSkillDefinition BuildRuntime(
            SkillDefinition def,
            StatModifierAccumulator modifiers,
            SkillStatSnapshot snapshot)
        {
            if (def is ProjectileDefinition p)
            {
                return new RuntimeProjectileDefinition
                {
                    Prefab = p.prefab,
                    Speed = modifiers.Resolve(SkillStat.ProjectileSpeed, p.speed),
                    Lifetime = modifiers.Resolve(SkillStat.ProjectileLifetime, p.lifetime),
                    Damage = Mathf.Max(0f, modifiers.Resolve(SkillStat.Damage, p.damage)),
                    Count = Mathf.Max(1, p.count),
                    SpreadDegrees = p.spreadDegrees,
                    JitterDegrees = p.jitterDegrees,
                    PierceCount = Mathf.Max(0, Mathf.RoundToInt(modifiers.Resolve(SkillStat.PierceCount, p.pierceCount))),
                    RepeatHitCooldown = Mathf.Max(0f, p.repeatHitCooldown),
                    ArmSeconds = Mathf.Max(0f, p.armSeconds),
                    DirectDamageEnabled = p.directDamageEnabled,
                    Tracking = p.GetTrackingConfig(),
                    CritChance = snapshot.CritChance,
                    CritMultiplier = snapshot.CritMultiplier,
                };
            }

            if (def is AoeDefinitionBase a)
            {
                float lifetimeSeconds = 0f;
                float tickIntervalSeconds = 0f;
                if (a is LingeringAoeDefinition lingering)
                {
                    lifetimeSeconds = lingering.lifetimeSeconds;
                    tickIntervalSeconds = lingering.tickIntervalSeconds;
                }

                return new RuntimeAoeDefinition
                {
                    VisualPrefab = a.VisualPrefab,
                    CollisionShape = a.CollisionShape,
                    AreaSize = Mathf.Max(0.01f, modifiers.Resolve(SkillStat.AreaSize, a.baseAreaSize)),
                    VisualRotationDegrees = a.VisualRotationDegrees,
                    SpawnEffect = a.SpawnEffect,
                    HitEffect = a.HitEffect,
                    ExpireEffect = a.ExpireEffect,
                    PulseEffect = a.PulseEffect,
                    ArmingEffect = a.ArmingEffect,
                    Damage = Mathf.Max(0f, modifiers.Resolve(SkillStat.Damage, a.damage)),
                    LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds),
                    TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds),
                    ArmSeconds = Mathf.Max(0f, a.armSeconds),
                    EchoCount = Mathf.Max(1, a.echoCount),
                    ScatterRadius = Mathf.Max(0f, a.scatterRadius),
                    DirectDamageEnabled = a.directDamageEnabled,
                    CritChance = snapshot.CritChance,
                    CritMultiplier = snapshot.CritMultiplier,
                };
            }

            return null;
        }

        private readonly struct CompileDefinitionResult
        {
            public CompileDefinitionResult(RuntimeSkillDefinition runtime, StatModifierAccumulator modifiers)
            {
                Runtime = runtime;
                Modifiers = modifiers;
            }

            public RuntimeSkillDefinition Runtime { get; }
            public StatModifierAccumulator Modifiers { get; }
        }

        private static class SnapshotModifiers
        {
            public static void Contribute(StatModifierAccumulator modifiers, SkillStatSnapshot snapshot)
            {
                modifiers.AddIncreased(SkillStat.Rate, snapshot.IncreasedRatePercent);
                modifiers.AddMultiplier(SkillStat.Damage, snapshot.DamageMultiplier, MultiplierTiming.Post);
                modifiers.AddMultiplier(SkillStat.AreaSize, snapshot.AreaSizeMultiplier, MultiplierTiming.Post);
            }
        }

        private static void ApplyChildSpawn(
            RuntimeSkillDefinition parent,
            ProjectileIntervalSpawnTrigger trigger,
            IReadOnlyList<LoadoutSlot> slots,
            int effectIndex,
            TriggerChain[] allChains,
            SkillStatSnapshot snapshot)
        {
            if (parent is not RuntimeProjectileDefinition and not RuntimeAoeDefinition)
                return;

            if (parent is RuntimeAoeDefinition { LifetimeSeconds: <= 0f })
                return;

            RuntimeSkillDefinition compiledChild = Compile(slots, effectIndex, allChains, snapshot);
            if (compiledChild is not RuntimeProjectileDefinition childDef) return;

            float intervalSeconds = Mathf.Max(0.01f, trigger.intervalSeconds);
            var setup = new RuntimeChildSpawnSetup
            {
                JitterSeed = ++nextChildJitterSeed,
                ChildDefinition = childDef,
                IntervalSeconds = intervalSeconds,
                IntervalJitterSeconds = intervalSeconds * Mathf.Clamp(trigger.intervalJitterPercent, 0f, 100f) * 0.01f,
                Behavior = new ProjectileChildSpawnBehavior(
                    Mathf.Max(1, childDef.Count + trigger.projectileCount),
                    ProjectileChildSpawnPatternType.SideSpray,
                    trigger.sideSpreadDegrees),
            };

            if (parent is RuntimeProjectileDefinition projectileParent)
                projectileParent.ChildSpawnSetup = setup;
            else if (parent is RuntimeAoeDefinition aoeParent)
                aoeParent.ChildSpawnSetup = setup;
        }

        private static void ApplyAoeIntervalSpawn(
            RuntimeSkillDefinition parent,
            AoeIntervalSpawnTrigger trigger,
            IReadOnlyList<LoadoutSlot> slots,
            int effectIndex,
            TriggerChain[] allChains,
            SkillStatSnapshot snapshot)
        {
            if (parent is not RuntimeProjectileDefinition and not RuntimeAoeDefinition)
                return;

            if (parent is RuntimeAoeDefinition { LifetimeSeconds: <= 0f })
                return;

            RuntimeSkillDefinition compiledChild = Compile(slots, effectIndex, allChains, snapshot);
            if (compiledChild is not RuntimeAoeDefinition childDef) return;

            float intervalSeconds = Mathf.Max(0.01f, trigger.intervalSeconds);
            var setup = new RuntimeAoeIntervalSpawnSetup
            {
                JitterSeed = ++nextChildJitterSeed,
                ChildDefinition = childDef,
                IntervalSeconds = intervalSeconds,
                IntervalJitterSeconds = intervalSeconds * Mathf.Clamp(trigger.intervalJitterPercent, 0f, 100f) * 0.01f,
                Count = Mathf.Max(1, childDef.EchoCount + trigger.echoCount),
                ScatterRadius = Mathf.Max(0f, trigger.scatterRadius),
            };

            if (parent is RuntimeProjectileDefinition projectileParent)
                projectileParent.AoeIntervalSpawnSetup = setup;
            else if (parent is RuntimeAoeDefinition aoeParent)
                aoeParent.AoeIntervalSpawnSetup = setup;
        }

        private static SkillSet GetSkillSet(IReadOnlyList<LoadoutSlot> slots, int slotIndex)
        {
            if (slots == null || slotIndex < 0 || slotIndex >= slots.Count)
                return null;

            return slots[slotIndex] is SkillSetSlot slot ? slot.skillSet : null;
        }
    }
}

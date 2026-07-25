using System.Collections.Generic;
using PlayGround.Skills.Modifiers;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Vfx;
using UnityEngine;

namespace PlayGround.Skills
{
    public static class SkillSetCompiler
    {
        private static int nextChildJitterSeed;

        // Temporary test-fixture compatibility while the existing EditMode cases
        // move from managed-reference slots to normalized nodes.
        [global::System.Obsolete("Tests must use SkillLoadoutNode lists.")]
        public static RuntimeSkillDefinition Compile(
            IReadOnlyList<LoadoutSlot> slots,
            int slotIndex,
            TriggerChain[] ignoredChains,
            SkillStatSnapshot snapshot)
        {
            int nodeIndex = 0;
            for (int i = 0; i < slotIndex && i < slots.Count; i++)
                if (slots[i] is SkillSetSlot) nodeIndex++;

            return Compile(CreateNodesFromLegacySlots(slots), nodeIndex, snapshot);
        }

        public static RuntimeSkillDefinition Compile(
            IReadOnlyList<SkillLoadoutNode> nodes,
            int nodeIndex,
            SkillStatSnapshot snapshot)
        {
            SkillSet set = GetSkillSet(nodes, nodeIndex);
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

            // Adjacency is forward-only (i -> i + 1); recursion terminates by
            // strictly increasing node index. No cycle is possible.
            TriggerLink link = nodeIndex >= 0 && nodeIndex < nodes.Count
                ? nodes[nodeIndex]?.TriggerToNext
                : null;
            int targetNodeIndex = nodeIndex + 1;
            if (link != null && targetNodeIndex < nodes.Count)
            {

                if (link is ProjectileIntervalSpawnTrigger childTrigger)
                {
                    ApplyChildSpawn(triggerHost, childTrigger, nodes, targetNodeIndex, snapshot);
                }
                else if (link is AoeIntervalSpawnTrigger aoeIntervalTrigger)
                {
                    ApplyAoeIntervalSpawn(triggerHost, aoeIntervalTrigger, nodes, targetNodeIndex, snapshot);
                }
                else if (link is OnImpactAoeTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = Compile(nodes, targetNodeIndex, snapshot);
                    if (compiledTarget is RuntimeAoeDefinition aoeTarget)
                    {
                        if (triggerHost is RuntimeProjectileDefinition projDef)
                            projDef.ImpactAoeDefinition = aoeTarget;
                        else if (triggerHost is RuntimeAoeDefinition sourceAoeDef)
                            sourceAoeDef.OnHitAoeSpawnDefinition = aoeTarget;
                    }
                }
                else if (link is OnImpactProjectileTrigger impactProjTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = Compile(nodes, targetNodeIndex, snapshot);
                    if (compiledTarget is RuntimeProjectileDefinition impactProjDef)
                    {
                        impactProjDef.Count = Mathf.Max(1, impactProjDef.Count + impactProjTrigger.spawnCount);
                        impactProjDef.SpreadDegrees = impactProjTrigger.spreadDegrees;

                        if (triggerHost is RuntimeProjectileDefinition projDef)
                            projDef.ImpactProjectileDefinition = impactProjDef;
                        else if (triggerHost is RuntimeAoeDefinition aoeSourceDef)
                            aoeSourceDef.OnHitProjectileSpawnDefinition = impactProjDef;
                    }
                }
                else if (link is OnAoeHitSpawnTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = Compile(nodes, targetNodeIndex, snapshot);
                    if (triggerHost is RuntimeAoeDefinition aoeDef)
                        aoeDef.OnHitAoeSpawnDefinition = compiledTarget;
                }
                else if (link is StackTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = Compile(nodes, targetNodeIndex, snapshot);
                    if (compiledTarget is RuntimeStackingDetonation stackingDetonation)
                    {
                        if (triggerHost is RuntimeProjectileDefinition projDef)
                            projDef.StackingDetonation = stackingDetonation;
                        else if (triggerHost is RuntimeAoeDefinition aoeDef)
                            aoeDef.StackingDetonation = stackingDetonation;
                    }
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
                    ManaCost = Mathf.Max(0f, modifiers.Resolve(SkillStat.ManaCost, p.manaCost)),
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
                    SpawnEffectShape = a.SpawnEffectShape,
                    HitEffectShape = a.HitEffectShape,
                    ExpireEffectShape = a.ExpireEffectShape,
                    PulseEffectShape = a.PulseEffectShape,
                    ArmingEffectShape = a.ArmingEffectShape,
                    Damage = Mathf.Max(0f, modifiers.Resolve(SkillStat.Damage, a.damage)),
                    LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds),
                    TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds),
                    ManaCost = Mathf.Max(0f, modifiers.Resolve(SkillStat.ManaCost, a.manaCost)),
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
            IReadOnlyList<SkillLoadoutNode> nodes,
            int targetNodeIndex,
            SkillStatSnapshot snapshot)
        {
            if (parent is not RuntimeProjectileDefinition and not RuntimeAoeDefinition)
                return;

            if (parent is RuntimeAoeDefinition { LifetimeSeconds: <= 0f })
                return;

            RuntimeSkillDefinition compiledChild = Compile(nodes, targetNodeIndex, snapshot);
            if (compiledChild is not RuntimeProjectileDefinition childDef) return;

            float energyThreshold = trigger.ManaToEnergyCost(childDef.ManaCost);
            var setup = new RuntimeChildSpawnSetup
            {
                JitterSeed = ++nextChildJitterSeed,
                ChildDefinition = childDef,
                EnergyPerSecond = Mathf.Max(0.01f, trigger.energyPerSecond),
                EnergyThreshold = energyThreshold,
                EnergyThresholdJitter = energyThreshold * Mathf.Clamp(trigger.energyJitterPercent, 0f, 100f) * 0.01f,
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
            IReadOnlyList<SkillLoadoutNode> nodes,
            int targetNodeIndex,
            SkillStatSnapshot snapshot)
        {
            if (parent is not RuntimeProjectileDefinition and not RuntimeAoeDefinition)
                return;

            if (parent is RuntimeAoeDefinition { LifetimeSeconds: <= 0f })
                return;

            RuntimeSkillDefinition compiledChild = Compile(nodes, targetNodeIndex, snapshot);
            if (compiledChild is not RuntimeAoeDefinition childDef) return;

            float energyThreshold = trigger.ManaToEnergyCost(childDef.ManaCost);
            var setup = new RuntimeAoeIntervalSpawnSetup
            {
                JitterSeed = ++nextChildJitterSeed,
                ChildDefinition = childDef,
                EnergyPerSecond = Mathf.Max(0.01f, trigger.energyPerSecond),
                EnergyThreshold = energyThreshold,
                EnergyThresholdJitter = energyThreshold * Mathf.Clamp(trigger.energyJitterPercent, 0f, 100f) * 0.01f,
                Count = Mathf.Max(1, childDef.EchoCount + trigger.echoCount),
                ScatterRadius = Mathf.Max(0f, trigger.scatterRadius),
            };

            if (parent is RuntimeProjectileDefinition projectileParent)
                projectileParent.AoeIntervalSpawnSetup = setup;
            else if (parent is RuntimeAoeDefinition aoeParent)
                aoeParent.AoeIntervalSpawnSetup = setup;
        }

        private static SkillSet GetSkillSet(IReadOnlyList<SkillLoadoutNode> nodes, int nodeIndex)
        {
            if (nodes == null || nodeIndex < 0 || nodeIndex >= nodes.Count)
                return null;

            return nodes[nodeIndex]?.SkillSet;
        }

        private static List<SkillLoadoutNode> CreateNodesFromLegacySlots(IReadOnlyList<LoadoutSlot> slots)
        {
            var nodes = new List<SkillLoadoutNode>();
            if (slots == null) return nodes;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] is not SkillSetSlot skillSlot) continue;

                TriggerLink trigger = i + 2 < slots.Count
                    && slots[i + 1] is TriggerLinkSlot triggerSlot
                    && slots[i + 2] is SkillSetSlot
                    ? triggerSlot.link
                    : null;
                nodes.Add(new SkillLoadoutNode(skillSlot.skillSet, trigger));
            }

            return nodes;
        }
    }
}

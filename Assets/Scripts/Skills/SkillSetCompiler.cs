using System.Collections.Generic;
using PlayGround.Skills.Runtime;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Skills
{
    public static class SkillSetCompiler
    {
        private static int nextChildSpawnerId;

        public static RuntimeSkillDefinition Compile(
            IReadOnlyList<LoadoutSlot> slots,
            int slotIndex,
            TriggerChain[] allChains,
            PlayerStatSnapshot snapshot)
        {
            SkillSet set = GetSkillSet(slots, slotIndex);
            if (set == null || set.Skill == null) return null;

            RuntimeSkillDefinition runtime = CompileDefinition(set.Skill.Definition, set.Supports, snapshot);
            if (runtime == null) return null;

            if (runtime is RuntimeStackingDetonation rootStackingDetonation)
                rootStackingDetonation.DebuffName = set.Skill.name;

            runtime.RecoveryTime = Mathf.Max(0.01f, set.Skill.BaseRecoveryTime * snapshot.CastSpeedMultiplier);

            // Adjacency is forward-only (i -> i+2); recursion terminates by strictly
            // increasing slot index. No cycle is possible, so no recursion guard is needed.
            foreach (TriggerChain chain in allChains)
            {
                if (chain == null || chain.causeIndex != slotIndex || chain.link == null) continue;

                if (chain.link is ProjectileIntervalSpawnTrigger childTrigger)
                {
                    if (runtime is RuntimeProjectileDefinition projDef)
                        ApplyChildSpawn(projDef, childTrigger, slots, chain.effectIndex, allChains, snapshot);
                    continue;
                }

                if (chain.link is OnImpactAoeTrigger)
                {
                    if (runtime is RuntimeProjectileDefinition projDef)
                    {
                        RuntimeSkillDefinition compiledTarget = Compile(slots, chain.effectIndex, allChains, snapshot);
                        if (compiledTarget is RuntimeAoeDefinition aoeDef)
                            projDef.ImpactAoeDefinition = aoeDef;
                    }
                    continue;
                }

                if (chain.link is OnImpactProjectileTrigger impactProjTrigger)
                {
                    if (runtime is RuntimeProjectileDefinition projDef)
                    {
                        RuntimeSkillDefinition compiledTarget = Compile(slots, chain.effectIndex, allChains, snapshot);
                        if (compiledTarget is RuntimeProjectileDefinition impactProjDef)
                        {
                            projDef.ImpactProjectileDefinition = impactProjDef;
                            impactProjDef.Count = Mathf.Max(1, impactProjDef.Count + impactProjTrigger.spawnCount);
                            impactProjDef.SpreadDegrees = impactProjTrigger.spreadDegrees;
                            if (impactProjDef.ImpactProjectileDefinition != null)
                            {
                                SkillSet effectSet = GetSkillSet(slots, chain.effectIndex);
                                Debug.LogWarning($"[SkillSetCompiler] '{effectSet?.Skill?.name}' has OnImpactProjectileTrigger but is itself used as an impact-projectile target. The nested OnImpactProjectile chain will not fire - C# value-type structs cannot be recursive. Restructure the loadout to avoid proj->proj->proj nesting.");
                            }
                        }
                    }
                    continue;
                }

                if (chain.link is OnAoeHitSpawnTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = Compile(slots, chain.effectIndex, allChains, snapshot);
                    if (runtime is RuntimeAoeDefinition aoeDef)
                        aoeDef.OnHitAoeSpawnDefinition = compiledTarget;
                }

                if (chain.link is StackTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = Compile(slots, chain.effectIndex, allChains, snapshot);
                    if (compiledTarget is not RuntimeStackingDetonation stackingDetonation)
                        continue;

                    if (runtime is RuntimeProjectileDefinition projDef)
                        projDef.StackingDetonation = stackingDetonation;
                    else if (runtime is RuntimeAoeDefinition aoeDef)
                        aoeDef.StackingDetonation = stackingDetonation;
                }
            }

            return runtime;
        }

        private static RuntimeSkillDefinition CompileDefinition(
            SkillDefinition definition,
            IReadOnlyList<SkillSupport> supports,
            PlayerStatSnapshot snapshot)
        {
            if (definition == null)
                return null;

            SkillDefinition defCopy = definition.DeepCopy();
            ApplySupports(defCopy, supports);
            return ApplyConversionSupports(
                definition,
                BuildRuntime(defCopy, snapshot),
                supports,
                snapshot);
        }

        private static void ApplySupports(
            SkillDefinition definition,
            IReadOnlyList<SkillSupport> supports)
        {
            if (definition == null || supports == null)
                return;

            for (int i = 0; i < supports.Count; i++)
            {
                if (supports[i] is AdditiveSupport additive)
                    additive.Apply(definition);
            }
        }

        private static RuntimeSkillDefinition ApplyConversionSupports(
            SkillDefinition definition,
            RuntimeSkillDefinition runtime,
            IReadOnlyList<SkillSupport> supports,
            PlayerStatSnapshot snapshot)
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

        private static RuntimeSkillDefinition BuildRuntime(SkillDefinition def, PlayerStatSnapshot snapshot)
        {
            if (def is ProjectileDefinition p)
            {
                return new RuntimeProjectileDefinition
                {
                    Prefab = p.prefab,
                    Speed = p.speed,
                    Lifetime = p.lifetime,
                    Damage = Mathf.Max(0f, p.damage * snapshot.DamageMultiplier),
                    Count = Mathf.Max(1, p.count),
                    SpreadDegrees = p.spreadDegrees,
                    JitterDegrees = p.jitterDegrees,
                    PierceCount = Mathf.Max(0, p.pierceCount),
                    RepeatHitCooldown = Mathf.Max(0f, p.repeatHitCooldown),
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
                    AreaSize = Mathf.Max(0.01f, a.baseAreaSize * snapshot.AreaSizeMultiplier),
                    VisualRotationDegrees = a.VisualRotationDegrees,
                    SpawnEffect = a.SpawnEffect,
                    HitEffect = a.HitEffect,
                    ExpireEffect = a.ExpireEffect,
                    PulseEffect = a.PulseEffect,
                    Damage = Mathf.Max(0f, a.damage * snapshot.DamageMultiplier),
                    LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds),
                    TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds),
                    Count = Mathf.Max(1, a.count),
                    DirectDamageEnabled = a.directDamageEnabled,
                    CritChance = snapshot.CritChance,
                    CritMultiplier = snapshot.CritMultiplier,
                };
            }

            return null;
        }

        private static void ApplyChildSpawn(
            RuntimeProjectileDefinition parent,
            ProjectileIntervalSpawnTrigger trigger,
            IReadOnlyList<LoadoutSlot> slots,
            int effectIndex,
            TriggerChain[] allChains,
            PlayerStatSnapshot snapshot)
        {
            RuntimeSkillDefinition compiledChild = Compile(slots, effectIndex, allChains, snapshot);
            if (compiledChild is not RuntimeProjectileDefinition childDef) return;

            float intervalSeconds = Mathf.Max(0.01f, trigger.intervalSeconds);
            parent.ChildSpawnSetup = new RuntimeChildSpawnSetup
            {
                SpawnerId = ++nextChildSpawnerId,
                ChildDefinition = childDef,
                IntervalSeconds = intervalSeconds,
                IntervalJitterSeconds = intervalSeconds * Mathf.Clamp(trigger.intervalJitterPercent, 0f, 100f) * 0.01f,
                Behavior = new ProjectileChildSpawnBehavior(
                    Mathf.Max(1, childDef.Count + trigger.spawnCount),
                    ProjectileChildSpawnPatternType.SideSpray,
                    trigger.sideSpreadDegrees),
            };
        }

        private static SkillSet GetSkillSet(IReadOnlyList<LoadoutSlot> slots, int slotIndex)
        {
            if (slots == null || slotIndex < 0 || slotIndex >= slots.Count)
                return null;

            return slots[slotIndex] is SkillSetSlot slot ? slot.skillSet : null;
        }
    }
}

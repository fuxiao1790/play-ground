using PlayGround.Skills.Runtime;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Skills
{
    public static class SkillSetCompiler
    {
        private static int nextChildSpawnerId;

        public static RuntimeSkillDefinition Compile(
            SkillSet set,
            TriggerChain[] allChains,
            PlayerStatSnapshot snapshot)
        {
            if (set == null || set.Skill == null) return null;

            SkillDefinition defCopy = set.Skill.Definition.DeepCopy();
            foreach (AdditiveSupport support in set.Supports)
                support?.Apply(defCopy);

            RuntimeSkillDefinition runtime = BuildRuntime(defCopy, snapshot);
            if (runtime == null) return null;

            runtime.RecoveryTime = Mathf.Max(0.01f, set.BaseRecoveryTime * snapshot.CastSpeedMultiplier);

            foreach (TriggerChain chain in allChains)
            {
                if (chain == null || chain.cause != set || chain.link == null || chain.effect == null) continue;
                if (chain.effect == set) continue;

                if (chain.link is ChildSpawnTrigger childTrigger)
                {
                    if (runtime is RuntimeProjectileDefinition projDef)
                        ApplyChildSpawn(projDef, childTrigger, chain.effect, allChains, snapshot);
                    continue;
                }

                if (chain.link is OnImpactAoeTrigger)
                {
                    if (runtime is RuntimeProjectileDefinition projDef)
                    {
                        RuntimeSkillDefinition compiledTarget = Compile(chain.effect, allChains, snapshot);
                        if (compiledTarget is RuntimeAoeDefinition aoeDef)
                            projDef.ImpactAoeDefinition = aoeDef;
                    }
                    continue;
                }

                if (chain.link is OnImpactProjectileTrigger impactProjTrigger)
                {
                    if (runtime is RuntimeProjectileDefinition projDef)
                    {
                        RuntimeSkillDefinition compiledTarget = Compile(chain.effect, allChains, snapshot);
                        if (compiledTarget is RuntimeProjectileDefinition impactProjDef)
                        {
                            projDef.ImpactProjectileDefinition = impactProjDef;
                            impactProjDef.SpreadDegrees = impactProjTrigger.spreadDegrees;
                            if (impactProjDef.ImpactProjectileDefinition != null)
                                Debug.LogWarning($"[SkillSetCompiler] '{chain.effect?.Skill?.name}' has OnImpactProjectileTrigger but is itself used as an impact-projectile target. The nested OnImpactProjectile chain will not fire — C# value-type structs cannot be recursive. Restructure the loadout to avoid proj→proj→proj nesting.");
                        }
                    }
                    continue;
                }

                if (chain.link is OnStackTrigger stackTrigger)
                {
                    if (runtime is RuntimeProjectileDefinition projDef)
                    {
                        RuntimeSkillDefinition compiledTarget = Compile(chain.effect, allChains, snapshot);
                        if (compiledTarget is RuntimeAoeDefinition aoeDef)
                        {
                            projDef.StackTriggerSetup = new RuntimeStackTriggerSetup
                            {
                                DebuffStatusId = (int)stackTrigger.debuffStatus,
                                StacksPerHit = Mathf.Max(1, stackTrigger.stacksPerHit),
                                StackThreshold = Mathf.Max(1, stackTrigger.stackThreshold),
                                AoeDefinition = aoeDef,
                            };
                        }
                    }
                    continue;
                }
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
                    SpawnAtAimPosition = a.spawnAtAimPosition,
                    DirectDamageEnabled = a.directDamageEnabled,
                    CritChance = snapshot.CritChance,
                    CritMultiplier = snapshot.CritMultiplier,
                };
            }

            return null;
        }

        private static void ApplyChildSpawn(
            RuntimeProjectileDefinition parent,
            ChildSpawnTrigger trigger,
            SkillSet effectSet,
            TriggerChain[] allChains,
            PlayerStatSnapshot snapshot)
        {
            RuntimeSkillDefinition compiledChild = Compile(effectSet, allChains, snapshot);
            if (compiledChild is not RuntimeProjectileDefinition childDef) return;

            float intervalSeconds = Mathf.Max(0.01f, trigger.intervalSeconds);
            parent.ChildSpawnSetup = new RuntimeChildSpawnSetup
            {
                SpawnerId = ++nextChildSpawnerId,
                ChildDefinition = childDef,
                IntervalSeconds = intervalSeconds,
                IntervalJitterSeconds = intervalSeconds * Mathf.Clamp(trigger.intervalJitterPercent, 0f, 100f) * 0.01f,
                Behavior = new ProjectileChildSpawnBehavior(
                    Mathf.Max(1, trigger.spawnCount),
                    ProjectileChildSpawnPatternType.SideSpray,
                    trigger.sideSpreadDegrees),
            };
        }
    }
}

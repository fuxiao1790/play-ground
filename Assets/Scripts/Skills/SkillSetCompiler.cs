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
            SkillSet set,
            IReadOnlyList<TriggerLink> allLinks,
            PlayerStatSnapshot snapshot)
        {
            if (set == null || set.Skill == null) return null;

            SkillDefinition defCopy = set.Skill.Definition.DeepCopy();
            foreach (AdditiveSupport support in set.Supports)
                support?.Apply(defCopy);

            RuntimeSkillDefinition runtime = BuildRuntime(defCopy, snapshot);
            if (runtime == null) return null;

            runtime.RecoveryTime = Mathf.Max(0.01f, set.BaseRecoveryTime * snapshot.CastSpeedMultiplier);

            foreach (TriggerLink link in allLinks)
            {
                if (link == null || link.source != set || link.target == null) continue;

                if (link is ChildSpawnTrigger childTrigger)
                {
                    if (runtime is RuntimeProjectileDefinition projDef)
                        ApplyChildSpawn(projDef, childTrigger, allLinks, snapshot);
                    continue;
                }

                if (link is OnImpactAoeTrigger impactTrigger)
                {
                    if (runtime is RuntimeProjectileDefinition projDef)
                    {
                        RuntimeSkillDefinition compiledTarget = Compile(impactTrigger.target, allLinks, snapshot);
                        if (compiledTarget is RuntimeAoeDefinition aoeDef)
                            projDef.ImpactAoeDefinition = aoeDef;
                    }
                    continue;
                }

                if (link is OnStackTrigger stackTrigger)
                {
                    if (runtime is RuntimeProjectileDefinition projDef)
                    {
                        RuntimeSkillDefinition compiledTarget = Compile(stackTrigger.target, allLinks, snapshot);
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
                };
            }

            if (def is AoeDefinition a)
            {
                return new RuntimeAoeDefinition
                {
                    Prefab = a.prefab,
                    SizeMultiplier = Mathf.Max(0.01f, a.sizeMultiplier),
                    Damage = Mathf.Max(0f, a.damage * snapshot.DamageMultiplier),
                    LifetimeSeconds = Mathf.Max(0f, a.lifetimeSeconds),
                    TickIntervalSeconds = Mathf.Max(0f, a.tickIntervalSeconds),
                    Count = Mathf.Max(1, a.count),
                    SpawnAtAimPosition = a.spawnAtAimPosition,
                    DirectDamageEnabled = a.directDamageEnabled,
                };
            }

            return null;
        }

        private static void ApplyChildSpawn(
            RuntimeProjectileDefinition parent,
            ChildSpawnTrigger trigger,
            IReadOnlyList<TriggerLink> allLinks,
            PlayerStatSnapshot snapshot)
        {
            RuntimeSkillDefinition compiledChild = Compile(trigger.target, allLinks, snapshot);
            if (compiledChild is not RuntimeProjectileDefinition childDef) return;

            parent.ChildSpawnSetup = new RuntimeChildSpawnSetup
            {
                SpawnerId = ++nextChildSpawnerId,
                ChildDefinition = childDef,
                IntervalSeconds = Mathf.Max(0.01f, trigger.intervalSeconds),
                IntervalJitterSeconds = 0f,
                Behavior = new ProjectileChildSpawnBehavior(
                    Mathf.Max(1, trigger.spawnCount),
                    ProjectileChildSpawnPatternType.SideSpray,
                    trigger.sideSpreadDegrees),
            };
        }
    }
}

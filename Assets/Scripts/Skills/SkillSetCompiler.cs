using System.Collections.Generic;
using PlayGround.Skills.Modifiers;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Vfx;
using UnityEngine;

namespace PlayGround.Skills
{
    public static class SkillSetCompiler
    {
        private const float NominalTickSeconds = 1f / 60f;
        private const float SmallestExpectedTargetRadius = 0.35f;
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
            return CompileInternal(nodes, nodeIndex, snapshot, includeTriggeredManaCosts: true);
        }

        private static RuntimeSkillDefinition CompileInternal(
            IReadOnlyList<SkillLoadoutNode> nodes,
            int nodeIndex,
            SkillStatSnapshot snapshot,
            bool includeTriggeredManaCosts)
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

                if (link is IntervalSpawnTrigger intervalTrigger)
                {
                    ApplyIntervalSpawn(triggerHost, intervalTrigger, nodes, targetNodeIndex, snapshot);
                }
                else if (link is OnImpactAoeTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = CompileInternal(
                        nodes, targetNodeIndex, snapshot, includeTriggeredManaCosts: false);
                    if (compiledTarget is RuntimeAoeDefinition aoeTarget)
                    {
                        if (triggerHost is RuntimeProjectileDefinition projDef)
                        {
                            projDef.ImpactAoeDefinition = aoeTarget;
                            ApplyIncomingTriggerManaCostMultiplier(aoeTarget, link);
                        }
                        else if (triggerHost is RuntimeAoeDefinition sourceAoeDef)
                        {
                            sourceAoeDef.OnHitAoeSpawnDefinition = aoeTarget;
                            ApplyIncomingTriggerManaCostMultiplier(aoeTarget, link);
                        }
                    }
                }
                else if (link is OnImpactTargetedTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = CompileInternal(
                        nodes, targetNodeIndex, snapshot, includeTriggeredManaCosts: false);
                    if (compiledTarget is RuntimeTargetedDefinition targetedTarget)
                    {
                        if (triggerHost is RuntimeProjectileDefinition projectileSource)
                        {
                            projectileSource.ImpactTargetedDefinition = targetedTarget;
                            ApplyIncomingTriggerManaCostMultiplier(targetedTarget, link);
                        }
                        else if (triggerHost is RuntimeAoeDefinition aoeSource)
                        {
                            aoeSource.OnHitTargetedSpawnDefinition = targetedTarget;
                            ApplyIncomingTriggerManaCostMultiplier(targetedTarget, link);
                        }
                    }
                }
                else if (link is OnImpactProjectileTrigger impactProjTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = CompileInternal(
                        nodes, targetNodeIndex, snapshot, includeTriggeredManaCosts: false);
                    if (compiledTarget is RuntimeProjectileDefinition impactProjDef)
                    {
                        impactProjDef.Count = Mathf.Max(1, impactProjDef.Count + impactProjTrigger.spawnCount);
                        impactProjDef.SpreadDegrees = impactProjTrigger.spreadDegrees;

                        if (triggerHost is RuntimeProjectileDefinition projDef)
                        {
                            projDef.ImpactProjectileDefinition = impactProjDef;
                            ApplyIncomingTriggerManaCostMultiplier(impactProjDef, link);
                        }
                        else if (triggerHost is RuntimeAoeDefinition aoeSourceDef)
                        {
                            aoeSourceDef.OnHitProjectileSpawnDefinition = impactProjDef;
                            ApplyIncomingTriggerManaCostMultiplier(impactProjDef, link);
                        }
                    }
                }
                else if (link is OnAoeHitSpawnTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = CompileInternal(
                        nodes, targetNodeIndex, snapshot, includeTriggeredManaCosts: false);
                    if (triggerHost is RuntimeAoeDefinition aoeDef)
                    {
                        aoeDef.OnHitAoeSpawnDefinition = compiledTarget;
                        ApplyIncomingTriggerManaCostMultiplier(compiledTarget, link);
                    }
                }
                else if (link is StackTrigger)
                {
                    RuntimeSkillDefinition compiledTarget = CompileInternal(
                        nodes, targetNodeIndex, snapshot, includeTriggeredManaCosts: false);
                    if (compiledTarget is RuntimeStackingDetonation stackingDetonation)
                    {
                        if (triggerHost is RuntimeProjectileDefinition projDef)
                        {
                            projDef.StackingDetonation = stackingDetonation;
                            ApplyIncomingTriggerManaCostMultiplier(stackingDetonation, link);
                        }
                        else if (triggerHost is RuntimeAoeDefinition aoeDef)
                        {
                            aoeDef.StackingDetonation = stackingDetonation;
                            ApplyIncomingTriggerManaCostMultiplier(stackingDetonation, link);
                        }
                        else if (triggerHost is RuntimeTargetedDefinition targetedDef)
                        {
                            targetedDef.StackingDetonation = stackingDetonation;
                            ApplyIncomingTriggerManaCostMultiplier(stackingDetonation, link);
                        }
                    }
                }
            }

            if (includeTriggeredManaCosts)
                AddTriggeredManaCostsToActiveSkill(runtime);

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

                if (support is IDamageModifiers.IBaseValueModifier damageAdded)
                    damageAdded.CollectAdded(new AddedSink(modifiers));

                if (support is IPierceCountModifiers.IBaseValueModifier pierceCountAdded)
                    pierceCountAdded.CollectAdded(new AddedSink(modifiers));

                if (support is IAreaSizeModifiers.IIncreasedModifier areaSizeIncreased)
                    areaSizeIncreased.CollectIncreases(new IncreasedSink(modifiers));

                if (support is IAreaSizeModifiers.IMultiplierModifier areaSizeMultiplier)
                    areaSizeMultiplier.CollectMultipliers(new MultiplierSink(modifiers));

                if (support is IProjectileSpeedModifiers.IMultiplierModifier speedMultiplier)
                    speedMultiplier.CollectMultipliers(new MultiplierSink(modifiers));

                if (support is IProjectileLifetimeModifiers.IMultiplierModifier lifetimeMultiplier)
                    lifetimeMultiplier.CollectMultipliers(new MultiplierSink(modifiers));

                if (support is IRateModifiers.IIncreasedModifier rateIncreased)
                    rateIncreased.CollectIncreases(new IncreasedSink(modifiers));

                if (support is IManaModifiers.IBaseValueModifier manaAdded)
                    manaAdded.CollectAdded(new AddedSink(modifiers));

                if (support is IManaModifiers.IIncreasedModifier manaIncreased)
                    manaIncreased.CollectIncreases(new IncreasedSink(modifiers));

                if (support is IManaModifiers.IMultiplierModifier manaMultiplier)
                    manaMultiplier.CollectMultipliers(new MultiplierSink(modifiers));
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
                else if (definition is TargetedDefinition targeted
                         && support is ITargetedBehaviorModifier targetedModifier)
                {
                    targetedModifier.ApplyToTargeted(new TargetedBehaviorContext(targeted));
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
                var runtime = new RuntimeProjectileDefinition
                {
                    Prefab = p.prefab,
                    SpawnSound = p.SpawnSound,
                    SpawnSoundRadius = p.SpawnSoundRadius,
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
                    ContinuousCollision = p.continuousCollision,
                    Tracking = p.GetTrackingConfig(),
                    CritChance = snapshot.CritChance,
                    CritMultiplier = snapshot.CritMultiplier,
                };

                runtime.SpawnBlocked = runtime.ContinuousCollision && runtime.Tracking.Enabled;
                float alongHalf = p.prefab != null
                    ? Mathf.Min(p.prefab.HalfExtents.x, p.prefab.HalfExtents.y)
                    : 0f;
                float travel = runtime.Speed * 2f * NominalTickSeconds;
                float gapFree = 2f * (alongHalf + SmallestExpectedTargetRadius);
                runtime.TrackingMayTunnel = runtime.Tracking.Enabled && travel > gapFree;
                return runtime;
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
                    SpawnSound = a.SpawnSound,
                    SpawnSoundRadius = a.SpawnSoundRadius,
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

            if (def is TargetedDefinition targeted)
            {
                TargetedPrefab prefab = targeted.Prefab;
                float chainDistance = Mathf.Max(
                    0f, modifiers.Resolve(SkillStat.AreaSize, targeted.chainDistance));
                int chainCount = Mathf.Clamp(targeted.chainCount, 1, 32);
                float chainDelay = Mathf.Max(0f, targeted.chainDelay);
                return new RuntimeTargetedDefinition
                {
                    Prefab = prefab,
                    SpawnSound = targeted.SpawnSound,
                    SpawnSoundRadius = targeted.SpawnSoundRadius,
                    EchoCount = Mathf.Max(1, targeted.echoCount),
                    ChainCount = chainCount,
                    ChainDistance = chainDistance,
                    ChainDamageFalloff = Mathf.Max(0f, targeted.chainDamageFalloff),
                    ChainDelay = chainDelay,
                    // Derived, never authored: the walk owns the instance's life, and this only
                    // catches an instance that somehow stops walking.
                    LifetimeSeconds = RuntimeTargetedDefinition.LifetimeFor(chainCount, chainDelay),
                    ManaCost = Mathf.Max(0f, modifiers.Resolve(SkillStat.ManaCost, targeted.manaCost)),
                    ArmSeconds = Mathf.Max(0f, targeted.armSeconds),
                    DirectDamageEnabled = targeted.directDamageEnabled,
                    VfxSize = new TargetedVfxSizeComponent
                    {
                        EffectSize = prefab != null ? prefab.VfxEffectSize : 0f,
                        LinkWidth = prefab != null ? prefab.LinkWidth : 0f
                    },
                    CritChance = snapshot.CritChance,
                    CritMultiplier = snapshot.CritMultiplier,
                    Damage = Mathf.Max(0f, modifiers.Resolve(SkillStat.Damage, targeted.damage)),
                    SpawnBlocked = chainDistance <= 0f
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
                modifiers.AddIncreasedPercent(SkillStat.Rate, snapshot.IncreasedRatePercent);
                modifiers.AddMultiplier(SkillStat.Damage, snapshot.DamageMultiplier);
                modifiers.AddMultiplier(SkillStat.AreaSize, snapshot.AreaSizeMultiplier);
            }
        }

        private static void ApplyIntervalSpawn(
            RuntimeSkillDefinition parent,
            IntervalSpawnTrigger trigger,
            IReadOnlyList<SkillLoadoutNode> nodes,
            int targetNodeIndex,
            SkillStatSnapshot snapshot)
        {
            if (parent is not RuntimeProjectileDefinition and not RuntimeAoeDefinition)
                return;

            if (parent is RuntimeAoeDefinition { LifetimeSeconds: <= 0f })
                return;

            RuntimeSkillDefinition compiledChild = CompileInternal(
                nodes, targetNodeIndex, snapshot, includeTriggeredManaCosts: false);
            switch (compiledChild)
            {
                case RuntimeProjectileDefinition childDef:
                {
                    var setup = new RuntimeChildSpawnSetup
                    {
                        JitterSeed = ++nextChildJitterSeed,
                        ChildDefinition = childDef,
                        EnergyPerSecond = trigger.ResolveEnergyPerSecond(snapshot),
                        EnergyThreshold = trigger.ManaToEnergyCost(childDef.ManaCost),
                        Behavior = new ProjectileChildSpawnBehavior(
                            Mathf.Max(1, childDef.Count + trigger.projectileCount),
                            ProjectileChildSpawnPatternType.SideSpray,
                            trigger.sideSpreadDegrees),
                    };
                    if (parent is RuntimeProjectileDefinition projectileParent)
                        projectileParent.ChildSpawnSetup = setup;
                    else if (parent is RuntimeAoeDefinition aoeParent)
                        aoeParent.ChildSpawnSetup = setup;
                    ApplyIncomingTriggerManaCostMultiplier(childDef, trigger);
                    break;
                }

                case RuntimeAoeDefinition childDef:
                {
                    var setup = new RuntimeAoeIntervalSpawnSetup
                    {
                        JitterSeed = ++nextChildJitterSeed,
                        ChildDefinition = childDef,
                        EnergyPerSecond = trigger.ResolveEnergyPerSecond(snapshot),
                        EnergyThreshold = trigger.ManaToEnergyCost(childDef.ManaCost),
                        Count = Mathf.Max(1, childDef.EchoCount + trigger.echoCount),
                        ScatterRadius = Mathf.Max(0f, trigger.scatterRadius),
                    };
                    if (parent is RuntimeProjectileDefinition projectileParent)
                        projectileParent.AoeIntervalSpawnSetup = setup;
                    else if (parent is RuntimeAoeDefinition aoeParent)
                        aoeParent.AoeIntervalSpawnSetup = setup;
                    ApplyIncomingTriggerManaCostMultiplier(childDef, trigger);
                    break;
                }

                case RuntimeTargetedDefinition childDef:
                {
                    var setup = new RuntimeTargetedIntervalSpawnSetup
                    {
                        JitterSeed = ++nextChildJitterSeed,
                        ChildDefinition = childDef,
                        EnergyPerSecond = trigger.ResolveEnergyPerSecond(snapshot),
                        EnergyThreshold = trigger.ManaToEnergyCost(childDef.ManaCost),
                        EchoCount = Mathf.Max(1, childDef.EchoCount + trigger.echoCount),
                    };
                    if (parent is RuntimeProjectileDefinition projectileParent)
                        projectileParent.TargetedIntervalSpawnSetup = setup;
                    else if (parent is RuntimeAoeDefinition aoeParent)
                        aoeParent.TargetedIntervalSpawnSetup = setup;
                    ApplyIncomingTriggerManaCostMultiplier(childDef, trigger);
                    break;
                }
            }
        }

        // Follow the compiled trigger tree after every link has been validated and
        // attached. A triggered skill never submits its own external request, so
        // its cost must be charged once by the player-cast skill that started it.
        // Child definitions retain their own cost because interval triggers use it
        // as their energy threshold rather than as a second mana spend.
        private static void AddTriggeredManaCostsToActiveSkill(RuntimeSkillDefinition activeSkill)
        {
            float totalManaCost = GetOwnManaCost(activeSkill)
                * GetManaCostMultiplier(activeSkill, includeCurrent: false)
                + SumTriggeredSkillManaCosts(activeSkill, isTriggeredSkill: false);
            SetManaCost(activeSkill, totalManaCost);
        }

        private static void ApplyIncomingTriggerManaCostMultiplier(
            RuntimeSkillDefinition triggeredDefinition,
            TriggerLink triggerLink)
        {
            if (triggeredDefinition == null || triggerLink == null)
                return;

            triggeredDefinition.IncomingManaCostFactor =
                Mathf.Max(0f, triggerLink.ResolveManaCostFactor());
        }

        private static float GetOwnManaCost(RuntimeSkillDefinition definition)
        {
            switch (definition)
            {
                case RuntimeStackingDetonation stacking:
                    return GetOwnManaCost(stacking.Detonation);

                case RuntimeProjectileDefinition projectile:
                    return projectile.ManaCost;

                case RuntimeAoeDefinition aoe:
                    return aoe.ManaCost;

                case RuntimeTargetedDefinition targeted:
                    return targeted.ManaCost;

                default:
                    return 0f;
            }
        }

        private static float GetManaCostMultiplier(
            RuntimeSkillDefinition definition,
            bool includeCurrent)
        {
            if (definition is RuntimeStackingDetonation stacking)
            {
                float stackingMultiplier = includeCurrent
                    ? stacking.IncomingManaCostFactor
                    : 1f;
                return stackingMultiplier * GetManaCostMultiplier(
                    stacking.Detonation, includeCurrent: false);
            }

            float multiplier = includeCurrent
                ? definition?.IncomingManaCostFactor ?? 1f
                : 1f;

            if (definition is RuntimeProjectileDefinition projectile)
            {
                return multiplier
                    * GetManaCostMultiplier(projectile.ChildSpawnSetup?.ChildDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(projectile.AoeIntervalSpawnSetup?.ChildDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(projectile.TargetedIntervalSpawnSetup?.ChildDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(projectile.ImpactAoeDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(projectile.ImpactTargetedDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(projectile.ImpactProjectileDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(projectile.StackingDetonation, includeCurrent: true);
            }

            if (definition is RuntimeAoeDefinition aoe)
            {
                return multiplier
                    * GetManaCostMultiplier(aoe.ChildSpawnSetup?.ChildDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(aoe.AoeIntervalSpawnSetup?.ChildDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(aoe.TargetedIntervalSpawnSetup?.ChildDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(aoe.OnHitAoeSpawnDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(aoe.OnHitTargetedSpawnDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(aoe.OnHitProjectileSpawnDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(aoe.StackingDetonation, includeCurrent: true);
            }

            if (definition is RuntimeTargetedDefinition targeted)
            {
                return multiplier
                    * GetManaCostMultiplier(targeted.OnHitAoeSpawnDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(targeted.OnHitProjectileSpawnDefinition, includeCurrent: true)
                    * GetManaCostMultiplier(targeted.StackingDetonation, includeCurrent: true);
            }

            return multiplier;
        }

        private static float SumTriggeredSkillManaCosts(
            RuntimeSkillDefinition definition,
            bool isTriggeredSkill)
        {
            if (definition == null)
                return 0f;

            if (definition is RuntimeStackingDetonation stacking)
            {
                float stackingManaCost = isTriggeredSkill
                    ? GetOwnManaCost(stacking) * stacking.IncomingManaCostFactor
                    : 0f;
                return stackingManaCost
                    + SumTriggeredSkillManaCosts(stacking.Detonation, isTriggeredSkill: false);
            }

            float manaCost = isTriggeredSkill
                ? GetOwnManaCost(definition) * definition.IncomingManaCostFactor
                : 0f;

            if (definition is RuntimeProjectileDefinition projectile)
            {
                return manaCost
                    + SumTriggeredSkillManaCosts(projectile.ChildSpawnSetup?.ChildDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(projectile.AoeIntervalSpawnSetup?.ChildDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(projectile.TargetedIntervalSpawnSetup?.ChildDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(projectile.ImpactAoeDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(projectile.ImpactTargetedDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(projectile.ImpactProjectileDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(projectile.StackingDetonation, isTriggeredSkill: true);
            }

            if (definition is RuntimeAoeDefinition aoe)
            {
                return manaCost
                    + SumTriggeredSkillManaCosts(aoe.ChildSpawnSetup?.ChildDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(aoe.AoeIntervalSpawnSetup?.ChildDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(aoe.TargetedIntervalSpawnSetup?.ChildDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(aoe.OnHitAoeSpawnDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(aoe.OnHitTargetedSpawnDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(aoe.OnHitProjectileSpawnDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(aoe.StackingDetonation, isTriggeredSkill: true);
            }

            if (definition is RuntimeTargetedDefinition targeted)
            {
                return manaCost
                    + SumTriggeredSkillManaCosts(targeted.OnHitAoeSpawnDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(targeted.OnHitProjectileSpawnDefinition, isTriggeredSkill: true)
                    + SumTriggeredSkillManaCosts(targeted.StackingDetonation, isTriggeredSkill: true);
            }

            return manaCost;
        }

        private static void SetManaCost(RuntimeSkillDefinition definition, float manaCost)
        {
            float resolvedManaCost = Mathf.Max(0f, manaCost);
            switch (definition)
            {
                case RuntimeStackingDetonation stacking:
                    SetManaCost(stacking.Detonation, resolvedManaCost);
                    break;

                case RuntimeProjectileDefinition projectile:
                    projectile.ManaCost = resolvedManaCost;
                    break;

                case RuntimeAoeDefinition aoe:
                    aoe.ManaCost = resolvedManaCost;
                    break;

                case RuntimeTargetedDefinition targeted:
                    targeted.ManaCost = resolvedManaCost;
                    break;
            }
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

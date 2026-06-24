using System;
using System.Collections.Generic;
using PlayGround.Audio;
using PlayGround.Common;
using PlayGround.Skills.Runtime;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using UnityEngine;

namespace PlayGround.Skills
{
    public sealed class PlayerSkillDriver : MonoBehaviour
    {
        [SerializeField] private PlayerLoadout loadout;
        [SerializeField] private CombatRoot combatRoot;
        [SerializeField] private CombatVfxRoot vfxRoot;
        [SerializeField] private AudioManager audioManager;

        private RuntimeSkillDefinition[] compiledSlots;
        private SkillSlotState[] slotStates;
        private SkillValidationWarning[] validationWarnings = Array.Empty<SkillValidationWarning>();
        private static int nextStackingDebuffKey;
        private int activeSlotCount;

        public int SlotCount => activeSlotCount;
        public IReadOnlyList<SkillValidationWarning> ValidationWarnings => validationWarnings;
        public SkillSlotState GetSlotState(int index) => slotStates?[index];

        private void Awake()
        {
            if (combatRoot == null)
                combatRoot = FindRootByTag<CombatRoot>(GameplayTags.PlayerProjectileRoot);

            audioManager ??= AudioManager.Instance ?? FindAnyObjectByType<AudioManager>();
        }

        private void Start()
        {
            if (vfxRoot != null && combatRoot != null)
                vfxRoot.BindFaction(combatRoot.Faction);
            CompileAndRegister();
        }

        public void Tick(bool attackHeld, Vector2 aimDir, Vector2 aimWorldPos)
        {
            if (compiledSlots == null) return;

            for (int i = 0; i < activeSlotCount; i++)
                slotStates[i].Tick(Time.deltaTime);

            if (!attackHeld) return;

            for (int i = 0; i < activeSlotCount; i++)
            {
                if (!slotStates[i].IsReady) continue;
                
                SkillSpawnTranslator.Spawn(
                    compiledSlots[i],
                    transform.position,
                    aimDir,
                    aimWorldPos,
                    combatRoot);

                slotStates[i].ResetOnFire();
            }
        }

        public void BindCombatRoot(CombatRoot root)
        {
            if (combatRoot == root) return;
            combatRoot = root;
            if (vfxRoot != null && combatRoot != null)
                vfxRoot.BindFaction(combatRoot.Faction);
            RegisterProjectileTypes();
            RegisterAoeTypes();
            RegisterIntervalTemplates();
        }

        // --- private ---

        private void CompileAndRegister()
        {
            if (loadout == null) return;

            validationWarnings = SkillLoadoutValidator.Validate(loadout);

            PlayerStatSnapshot snapshot = PlayerStatAggregator.Aggregate(loadout);
            IReadOnlyList<LoadoutSlot> slots = loadout.Slots;

            TriggerChain[] chains = ParseChains(slots);

            var effectIndices = new HashSet<int>();
            foreach (TriggerChain chain in chains)
                effectIndices.Add(chain.effectIndex);

            var rootSlotIndices = new List<int>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] is not SkillSetSlot skillSlot || skillSlot.skillSet == null) continue;
                if (!effectIndices.Contains(i))
                {
                    if (HasTriggeredOnlyConversionSupport(skillSlot.skillSet))
                        continue;

                    rootSlotIndices.Add(i);
                }
            }

            int maxSlots = Mathf.Min(rootSlotIndices.Count, loadout.MaxRootSets);
            compiledSlots = new RuntimeSkillDefinition[maxSlots];
            slotStates = new SkillSlotState[maxSlots];
            activeSlotCount = 0;

            for (int i = 0; i < maxSlots; i++)
            {
                RuntimeSkillDefinition def = SkillSetCompiler.Compile(slots, rootSlotIndices[i], chains, snapshot);
                if (def == null) continue;

                compiledSlots[activeSlotCount] = def;
                slotStates[activeSlotCount] = new SkillSlotState();
                slotStates[activeSlotCount].SetRecoveryTime(def.RecoveryTime);
                activeSlotCount++;
            }

            RegisterProjectileTypes();
            RegisterAoeTypes();
            RegisterIntervalTemplates();
        }

        private static TriggerChain[] ParseChains(IReadOnlyList<LoadoutSlot> slots)
        {
            var chains = new List<TriggerChain>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] is not SkillSetSlot causeSlot || causeSlot.skillSet == null) continue;
                if (i + 2 < slots.Count
                    && slots[i + 1] is TriggerLinkSlot triggerSlot && triggerSlot.link != null
                    && slots[i + 2] is SkillSetSlot effectSlot && effectSlot.skillSet != null)
                {
                    chains.Add(new TriggerChain
                    {
                        causeIndex = i,
                        link = triggerSlot.link,
                        effectIndex = i + 2,
                    });
                }
            }
            return chains.ToArray();
        }

        private static bool HasTriggeredOnlyConversionSupport(SkillSet set)
        {
            if (set == null)
                return false;

            SkillSupport[] supports = set.Supports;
            if (supports == null)
                return false;

            for (int i = 0; i < supports.Length; i++)
            {
                if (supports[i] is ConversionSupport { ConvertsToTriggeredOnly: true })
                    return true;
            }

            return false;
        }

        private void RegisterProjectileTypes()
        {
            if (combatRoot == null || compiledSlots == null) return;
            for (int i = 0; i < activeSlotCount; i++)
                RegisterProjectileTypesRecursive(compiledSlots[i]);
        }

        private void RegisterProjectileTypesRecursive(RuntimeSkillDefinition def)
        {
            if (def == null) return;

            if (def is RuntimeStackingDetonation stackingDetonation)
            {
                EnsureStackingDetonationDebuffKey(stackingDetonation);
                RegisterProjectileTypesRecursive(stackingDetonation.Detonation);
                return;
            }

            if (def is RuntimeAoeDefinition aoeDef)
            {
                if (aoeDef.ChildSpawnSetup?.ChildDefinition != null)
                    RegisterProjectileTypesRecursive(aoeDef.ChildSpawnSetup.ChildDefinition);
                if (aoeDef.AoeIntervalSpawnSetup?.ChildDefinition != null)
                    RegisterProjectileTypesRecursive(aoeDef.AoeIntervalSpawnSetup.ChildDefinition);
                if (aoeDef.OnHitAoeSpawnDefinition != null)
                    RegisterProjectileTypesRecursive(aoeDef.OnHitAoeSpawnDefinition);
                if (aoeDef.StackingDetonation != null)
                    RegisterProjectileTypesRecursive(aoeDef.StackingDetonation);
            }

            if (def is RuntimeProjectileDefinition projDef && projDef.Prefab != null && projDef.TypeId < 0)
            {
                projDef.TypeId = combatRoot.RegisterTemplate(projDef.Prefab);
                if (vfxRoot != null)
                {
                    vfxRoot.Register(projDef.TypeId, 0, projDef.Prefab.SpawnEffect);
                    vfxRoot.Register(projDef.TypeId, 1, projDef.Prefab.HitEffect);
                    vfxRoot.Register(projDef.TypeId, 2, projDef.Prefab.ExpireEffect);
                }
            }

            if (def is RuntimeProjectileDefinition p)
            {
                if (p.ChildSpawnSetup?.ChildDefinition != null)
                    RegisterProjectileTypesRecursive(p.ChildSpawnSetup.ChildDefinition);
                if (p.AoeIntervalSpawnSetup?.ChildDefinition != null)
                    RegisterProjectileTypesRecursive(p.AoeIntervalSpawnSetup.ChildDefinition);
                if (p.ImpactAoeDefinition != null)
                {
                    RegisterAoeTypeDefinition(p.ImpactAoeDefinition);
                    RegisterProjectileTypesRecursive(p.ImpactAoeDefinition);
                }
                if (p.ImpactProjectileDefinition != null)
                    RegisterProjectileTypesRecursive(p.ImpactProjectileDefinition);
                if (p.StackingDetonation != null)
                    RegisterProjectileTypesRecursive(p.StackingDetonation);
            }
        }

        private void RegisterAoeTypes()
        {
            if (combatRoot == null || compiledSlots == null) return;
            for (int i = 0; i < activeSlotCount; i++)
                RegisterAoeTypesRecursive(compiledSlots[i]);
        }

        private void RegisterIntervalTemplates()
        {
            if (combatRoot == null || compiledSlots == null) return;

            for (int i = 0; i < activeSlotCount; i++)
                RegisterIntervalTemplatesRecursive(compiledSlots[i]);
        }

        private void RegisterIntervalTemplatesRecursive(RuntimeSkillDefinition def)
        {
            if (def == null) return;

            if (def is RuntimeStackingDetonation stackingDetonation)
            {
                EnsureStackingDetonationDebuffKey(stackingDetonation);
                RegisterIntervalTemplatesRecursive(stackingDetonation.Detonation);
                return;
            }

            if (def is RuntimeAoeDefinition aoeDef)
            {
                if (aoeDef.ChildSpawnSetup?.ChildDefinition != null)
                {
                    RegisterProjectileIntervalTemplate(aoeDef.ChildSpawnSetup);
                    RegisterIntervalTemplatesRecursive(aoeDef.ChildSpawnSetup.ChildDefinition);
                }

                if (aoeDef.AoeIntervalSpawnSetup?.ChildDefinition != null)
                {
                    RegisterAoeIntervalTemplate(aoeDef.AoeIntervalSpawnSetup);
                    RegisterIntervalTemplatesRecursive(aoeDef.AoeIntervalSpawnSetup.ChildDefinition);
                }

                if (aoeDef.OnHitAoeSpawnDefinition != null)
                    RegisterIntervalTemplatesRecursive(aoeDef.OnHitAoeSpawnDefinition);
                if (aoeDef.StackingDetonation != null)
                    RegisterIntervalTemplatesRecursive(aoeDef.StackingDetonation);
            }

            if (def is RuntimeProjectileDefinition projDef)
            {
                if (projDef.ChildSpawnSetup?.ChildDefinition != null)
                {
                    RegisterProjectileIntervalTemplate(projDef.ChildSpawnSetup);
                    RegisterIntervalTemplatesRecursive(projDef.ChildSpawnSetup.ChildDefinition);
                }

                if (projDef.AoeIntervalSpawnSetup?.ChildDefinition != null)
                {
                    RegisterAoeIntervalTemplate(projDef.AoeIntervalSpawnSetup);
                    RegisterIntervalTemplatesRecursive(projDef.AoeIntervalSpawnSetup.ChildDefinition);
                }

                if (projDef.ImpactAoeDefinition != null)
                    RegisterIntervalTemplatesRecursive(projDef.ImpactAoeDefinition);
                if (projDef.ImpactProjectileDefinition != null)
                    RegisterIntervalTemplatesRecursive(projDef.ImpactProjectileDefinition);
                if (projDef.StackingDetonation != null)
                    RegisterIntervalTemplatesRecursive(projDef.StackingDetonation);
            }
        }

        private void RegisterProjectileIntervalTemplate(RuntimeChildSpawnSetup setup)
        {
            RuntimeProjectileDefinition child = setup?.ChildDefinition;
            if (setup == null || child == null || child.TypeId < 0 || child.Prefab == null)
                return;

            StackEffectSnapshot stackEffect =
                SkillIntervalTemplateBuilder.BuildApplicatorStackEffectSnapshot(child, combatRoot);
            ProjectileSpawnTemplateData data =
                SkillIntervalTemplateBuilder.BuildProjectileTemplateData(child, setup.Behavior, combatRoot, stackEffect);

            setup.TemplateData = data;
            setup.TemplateKey = combatRoot.RegisterTimedSpawnTemplate(in data);
            setup.SpawnConfig = SkillIntervalTemplateBuilder.BuildProjectileChildSpawnConfig(setup, data);
        }

        private void RegisterAoeIntervalTemplate(RuntimeAoeIntervalSpawnSetup setup)
        {
            RuntimeAoeDefinition child = setup?.ChildDefinition;
            if (setup == null || child == null || child.TypeId < 0)
                return;

            StackEffectSnapshot stackEffect =
                SkillIntervalTemplateBuilder.BuildApplicatorStackEffectSnapshot(child, combatRoot);
            AoeSpawnTemplateData data =
                SkillIntervalTemplateBuilder.BuildAoeTemplateData(
                    child,
                    Mathf.Max(1, setup.Count),
                    combatRoot,
                    stackEffect);

            setup.TemplateData = data;
            setup.TemplateKey = combatRoot.RegisterTimedSpawnTemplate(in data);
        }

        private void RegisterAoeTypesRecursive(RuntimeSkillDefinition def)
        {
            if (def == null) return;

            if (def is RuntimeStackingDetonation stackingDetonation)
            {
                EnsureStackingDetonationDebuffKey(stackingDetonation);
                RegisterAoeTypesRecursive(stackingDetonation.Detonation);
                return;
            }

            if (def is RuntimeAoeDefinition aoeDef)
            {
                RegisterAoeTypeDefinition(aoeDef);
                if (aoeDef.ChildSpawnSetup?.ChildDefinition != null)
                    RegisterAoeTypesRecursive(aoeDef.ChildSpawnSetup.ChildDefinition);
                if (aoeDef.AoeIntervalSpawnSetup?.ChildDefinition != null)
                    RegisterAoeTypesRecursive(aoeDef.AoeIntervalSpawnSetup.ChildDefinition);
                if (aoeDef.OnHitAoeSpawnDefinition != null)
                    RegisterAoeTypesRecursive(aoeDef.OnHitAoeSpawnDefinition);
                if (aoeDef.StackingDetonation != null)
                    RegisterAoeTypesRecursive(aoeDef.StackingDetonation);
            }

            if (def is RuntimeProjectileDefinition projDef)
            {
                if (projDef.ChildSpawnSetup?.ChildDefinition != null)
                    RegisterAoeTypesRecursive(projDef.ChildSpawnSetup.ChildDefinition);
                if (projDef.AoeIntervalSpawnSetup?.ChildDefinition != null)
                    RegisterAoeTypesRecursive(projDef.AoeIntervalSpawnSetup.ChildDefinition);
                if (projDef.ImpactAoeDefinition != null)
                    RegisterAoeTypesRecursive(projDef.ImpactAoeDefinition);
                if (projDef.ImpactProjectileDefinition != null)
                    RegisterAoeTypesRecursive(projDef.ImpactProjectileDefinition);
                if (projDef.StackingDetonation != null)
                    RegisterAoeTypesRecursive(projDef.StackingDetonation);
            }
        }

        private static void EnsureStackingDetonationDebuffKey(RuntimeStackingDetonation stackingDef)
        {
            if (stackingDef == null || stackingDef.DebuffKey >= 0)
                return;

            stackingDef.DebuffKey = ++nextStackingDebuffKey;
        }

        private void RegisterAoeTypeDefinition(RuntimeAoeDefinition aoeDef)
        {
            if (aoeDef == null || combatRoot == null || aoeDef.TypeId >= 0) return;
            AoeTypeDefinition definition = aoeDef.CreateTypeDefinition();
            aoeDef.TypeId = combatRoot.RegisterType(definition);
            if (vfxRoot != null)
            {
                vfxRoot.Register(aoeDef.TypeId, 0, definition.SpawnEffect, requireAreaSizeContract: true);
                vfxRoot.Register(aoeDef.TypeId, 1, definition.HitEffect, requireAreaSizeContract: true);
                vfxRoot.Register(aoeDef.TypeId, 2, definition.ExpireEffect, requireAreaSizeContract: true);
                vfxRoot.Register(aoeDef.TypeId, 3, definition.PulseEffect, requireAreaSizeContract: true);
            }
        }

        private static T FindRootByTag<T>(string tag) where T : Component
        {
            try
            {
                GameObject[] objects = GameObject.FindGameObjectsWithTag(tag);
                for (int i = 0; i < objects.Length; i++)
                {
                    if (objects[i].TryGetComponent(out T component))
                        return component;
                }
            }
            catch (UnityException) { }
            return null;
        }
    }

    internal static class SkillIntervalTemplateBuilder
    {
        private const int MaxAoeOnHitSpawnDepth = AoeOnHitSpawnSnapshot.MaxStackChainLinks;

        public static ProjectileSpawnTemplateData BuildProjectileTemplateData(
            RuntimeProjectileDefinition child,
            ProjectileChildSpawnBehavior behavior,
            CombatRoot root,
            StackEffectSnapshot stackEffect)
        {
            BasicAttackPrefab prefab = child.Prefab;
            float radians = prefab.VisualRotationDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new ProjectileSpawnTemplateData
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
                ImpactAoe = BuildImpactAoeSnapshot(child, root),
                StackEffect = stackEffect,
                ImpactProjectile = BuildImpactProjectileSnapshot(child, root)
            };
        }

        public static AoeSpawnTemplateData BuildAoeTemplateData(
            RuntimeAoeDefinition child,
            int count,
            CombatRoot root,
            StackEffectSnapshot stackEffect)
        {
            AoeSpawnGeometry geometry = child.CreateSpawnGeometry();
            return new AoeSpawnTemplateData
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

        public static ProjectileChildSpawnConfig BuildProjectileChildSpawnConfig(
            RuntimeChildSpawnSetup setup,
            ProjectileSpawnTemplateData data)
        {
            RuntimeProjectileDefinition child = setup.ChildDefinition;
            BasicAttackPrefab prefab = child.Prefab;
            return new ProjectileChildSpawnConfig(
                setup.JitterSeed,
                data.TypeId,
                Mathf.Max(0.01f, setup.IntervalSeconds),
                setup.IntervalJitterSeconds,
                data.Speed,
                data.Lifetime,
                data.Radius,
                prefab.HalfExtents,
                data.ShapeType,
                data.RotationRadians,
                new DamageSnapshot(data.DamageAmount),
                0,
                data.DirectDamageEnabled,
                data.PierceCount,
                data.RepeatHitCooldownSeconds,
                data.VisualScale,
                prefab.VisualRotationDegrees,
                child.Tracking,
                setup.Behavior,
                data.ImpactAoe,
                data.StackEffect,
                data.ImpactProjectile,
                setup.TemplateKey);
        }

        public static IntervalProjectileChild ToIntervalProjectileChild(
            ProjectileSpawnTemplateData data,
            EntityId sourceNodeId = default)
        {
            return new IntervalProjectileChild
            {
                TypeId = data.TypeId,
                ChildCountPerTick = data.ChildCountPerTick,
                SpawnPatternType = data.SpawnPatternType,
                SideSpreadDegrees = data.SideSpreadDegrees,
                Speed = data.Speed,
                Lifetime = data.Lifetime,
                Radius = data.Radius,
                HalfExtents = data.HalfExtents,
                RotationRadians = data.RotationRadians,
                ShapeType = data.ShapeType,
                DamageAmount = data.DamageAmount,
                DirectDamageEnabled = data.DirectDamageEnabled,
                PierceCount = data.PierceCount,
                RepeatHitCooldownSeconds = data.RepeatHitCooldownSeconds,
                VisualScale = data.VisualScale,
                VisualRotationSin = data.VisualRotationSin,
                VisualRotationCos = data.VisualRotationCos,
                TrackingEnabled = data.TrackingEnabled,
                TrackingTurnSpeedRadians = data.TrackingTurnSpeedRadians,
                TrackingQueryIntervalSeconds = data.TrackingQueryIntervalSeconds,
                TrackingInitialQueryDelaySeconds = data.TrackingInitialQueryDelaySeconds,
                SourceNodeId = sourceNodeId,
                ImpactAoe = data.ImpactAoe,
                StackEffect = data.StackEffect,
                ImpactProjectile = data.ImpactProjectile
            };
        }

        public static IntervalAoeChild ToIntervalAoeChild(AoeSpawnTemplateData data)
        {
            return new IntervalAoeChild
            {
                TypeId = data.TypeId,
                Lifetime = data.Lifetime,
                RepeatHitCooldownSeconds = data.RepeatHitCooldownSeconds,
                Radius = data.Radius,
                HalfExtents = data.HalfExtents,
                RotationRadians = data.RotationRadians,
                ShapeType = data.ShapeType,
                AreaSize = data.AreaSize,
                HitPayload = data.HitPayload,
                ProjectileBurst = data.ProjectileBurst,
                AoeSpawn = data.AoeSpawn,
                Render = data.Render,
                Count = data.Count
            };
        }

        public static StackEffectSnapshot BuildApplicatorStackEffectSnapshot(
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

        public static ProjectileImpactAoeSnapshot BuildImpactAoeSnapshot(
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

        public static ProjectileImpactProjectileSnapshot BuildImpactProjectileSnapshot(
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

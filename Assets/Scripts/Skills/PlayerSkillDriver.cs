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
                    combatRoot,
                    CombatFaction.Player);

                slotStates[i].ResetOnFire();
            }
        }

        public void BindCombatRoot(CombatRoot root)
        {
            if (combatRoot == root) return;
            combatRoot = root;
            RegisterProjectileTypes();
            RegisterAoeTypes();
            RegisterSpawnTemplates();
        }

        // --- private ---

        private void CompileAndRegister()
        {
            if (loadout == null) return;

            var warnings = new List<SkillValidationWarning>(SkillLoadoutValidator.Validate(loadout));

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
            RegisterSpawnTemplates(warnings);
            validationWarnings = warnings.ToArray();
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
                if (aoeDef.OnHitProjectileSpawnDefinition != null)
                    RegisterProjectileTypesRecursive(aoeDef.OnHitProjectileSpawnDefinition);
                if (aoeDef.StackingDetonation != null)
                    RegisterProjectileTypesRecursive(aoeDef.StackingDetonation);
            }

            if (def is RuntimeProjectileDefinition projDef && projDef.Prefab != null && projDef.TypeId < 0)
            {
                projDef.TypeId = combatRoot.RegisterTemplate(projDef.Prefab);
                projDef.RenderId = combatRoot.ProjectileRenderId(projDef.TypeId);
                if (vfxRoot != null)
                {
                    vfxRoot.Register(projDef.TypeId, 0, projDef.Prefab.SpawnEffect);
                    vfxRoot.Register(projDef.TypeId, 1, projDef.Prefab.HitEffect);
                    vfxRoot.Register(projDef.TypeId, 2, projDef.Prefab.ExpireEffect);
                    vfxRoot.Register(projDef.TypeId, 4, projDef.Prefab.ArmingEffect);
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

        private void RegisterSpawnTemplates(List<SkillValidationWarning> warnings = null)
        {
            if (combatRoot == null || compiledSlots == null) return;

            for (int i = 0; i < activeSlotCount; i++)
                RegisterSpawnTemplatesRecursive(compiledSlots[i], 1, warnings, i);
        }

        private bool RegisterSpawnTemplatesRecursive(
            RuntimeSkillDefinition def,
            int depth,
            List<SkillValidationWarning> warnings,
            int slotIndex,
            ProjectileChildSpawnPatternType selfPattern = ProjectileChildSpawnPatternType.Forward)
        {
            if (def == null) return false;
            if (depth > CombatRoot.MaxSpawnChainDepth)
            {
                warnings?.Add(new SkillValidationWarning(
                    SkillValidationWarningCode.SpawnChainDepthExceeded,
                    slotIndex,
                    $"Spawn chain exceeds max depth {CombatRoot.MaxSpawnChainDepth}. Overflow link will be ignored."));
                return false;
            }

            if (def is RuntimeStackingDetonation stackingDetonation)
            {
                EnsureStackingDetonationDebuffKey(stackingDetonation);
                // A projectile detonation fires as a radial nova from the detonation point,
                // so its own template is built with the radial pattern (its children, if any,
                // keep the default forward pattern).
                return RegisterSpawnTemplatesRecursive(
                    stackingDetonation.Detonation, depth, warnings, slotIndex,
                    ProjectileChildSpawnPatternType.Radial);
            }

            if (def is RuntimeAoeDefinition aoeDef)
            {
                if (aoeDef.ChildSpawnSetup?.ChildDefinition != null)
                {
                    if (RegisterSpawnTemplatesRecursive(
                            aoeDef.ChildSpawnSetup.ChildDefinition,
                            depth + 1,
                            warnings,
                            slotIndex))
                    {
                        RegisterProjectileIntervalTemplate(aoeDef.ChildSpawnSetup);
                    }
                }

                if (aoeDef.AoeIntervalSpawnSetup?.ChildDefinition != null)
                {
                    if (RegisterSpawnTemplatesRecursive(
                            aoeDef.AoeIntervalSpawnSetup.ChildDefinition,
                            depth + 1,
                            warnings,
                            slotIndex))
                    {
                        RegisterAoeIntervalTemplate(aoeDef.AoeIntervalSpawnSetup);
                    }
                }

                if (aoeDef.OnHitAoeSpawnDefinition != null)
                    RegisterSpawnTemplatesRecursive(aoeDef.OnHitAoeSpawnDefinition, depth + 1, warnings, slotIndex);
                if (aoeDef.OnHitProjectileSpawnDefinition != null)
                    RegisterSpawnTemplatesRecursive(aoeDef.OnHitProjectileSpawnDefinition, depth + 1, warnings, slotIndex);
                if (aoeDef.StackingDetonation != null)
                    RegisterSpawnTemplatesRecursive(aoeDef.StackingDetonation, depth + 1, warnings, slotIndex);

                StackEffectSnapshot stackEffect =
                    SkillIntervalTemplateBuilder.BuildApplicatorStackEffectSnapshot(aoeDef, combatRoot);
                AoeSpawnCommand template =
                    SkillIntervalTemplateBuilder.BuildAoeTemplate(
                        aoeDef,
                        Mathf.Max(1, aoeDef.EchoCount),
                        combatRoot,
                        stackEffect,
                        BuildOnHitSpawnRef(aoeDef),
                        AoeTimedSpawnFromDefinition(aoeDef));
                aoeDef.SpawnTemplateKey = combatRoot.RegisterSpawnTemplate(in template);
                return true;
            }

            if (def is RuntimeProjectileDefinition projDef)
            {
                if (projDef.ChildSpawnSetup?.ChildDefinition != null)
                {
                    if (RegisterSpawnTemplatesRecursive(
                            projDef.ChildSpawnSetup.ChildDefinition,
                            depth + 1,
                            warnings,
                            slotIndex))
                    {
                        RegisterProjectileIntervalTemplate(projDef.ChildSpawnSetup);
                    }
                }

                if (projDef.AoeIntervalSpawnSetup?.ChildDefinition != null)
                {
                    if (RegisterSpawnTemplatesRecursive(
                            projDef.AoeIntervalSpawnSetup.ChildDefinition,
                            depth + 1,
                            warnings,
                            slotIndex))
                    {
                        RegisterAoeIntervalTemplate(projDef.AoeIntervalSpawnSetup);
                    }
                }

                if (projDef.ImpactAoeDefinition != null)
                    RegisterSpawnTemplatesRecursive(projDef.ImpactAoeDefinition, depth + 1, warnings, slotIndex);
                if (projDef.ImpactProjectileDefinition != null)
                    RegisterSpawnTemplatesRecursive(projDef.ImpactProjectileDefinition, depth + 1, warnings, slotIndex);
                if (projDef.StackingDetonation != null)
                    RegisterSpawnTemplatesRecursive(projDef.StackingDetonation, depth + 1, warnings, slotIndex);

                StackEffectSnapshot stackEffect =
                    SkillIntervalTemplateBuilder.BuildApplicatorStackEffectSnapshot(projDef, combatRoot);
                ProjectileSpawnCommand template =
                    SkillIntervalTemplateBuilder.BuildProjectileTemplate(
                        projDef,
                        new ProjectileChildSpawnBehavior(
                            Mathf.Max(1, projDef.Count),
                            selfPattern,
                            projDef.SpreadDegrees),
                        combatRoot,
                        stackEffect,
                        BuildOnHitSpawnRef(projDef),
                        ProjectileTimedSpawnFromDefinition(projDef),
                        projDef.JitterDegrees);
                projDef.SpawnTemplateKey = combatRoot.RegisterSpawnTemplate(in template);
                return true;
            }

            return false;
        }

        private void RegisterProjectileIntervalTemplate(RuntimeChildSpawnSetup setup)
        {
            RuntimeProjectileDefinition child = setup?.ChildDefinition;
            if (setup == null || child == null || child.TypeId < 0 || child.Prefab == null)
                return;

            StackEffectSnapshot stackEffect =
                SkillIntervalTemplateBuilder.BuildApplicatorStackEffectSnapshot(child, combatRoot);
            ProjectileSpawnCommand template =
                SkillIntervalTemplateBuilder.BuildProjectileTemplate(
                    child,
                    setup.Behavior,
                    combatRoot,
                    stackEffect,
                    BuildOnHitSpawnRef(child),
                    ProjectileTimedSpawnFromDefinition(child),
                    child.JitterDegrees);

            setup.TemplateKey = combatRoot.RegisterTimedSpawnTemplate(in template);
        }

        private void RegisterAoeIntervalTemplate(RuntimeAoeIntervalSpawnSetup setup)
        {
            RuntimeAoeDefinition child = setup?.ChildDefinition;
            if (setup == null || child == null || child.TypeId < 0)
                return;

            StackEffectSnapshot stackEffect =
                SkillIntervalTemplateBuilder.BuildApplicatorStackEffectSnapshot(child, combatRoot);
            AoeSpawnCommand template =
                SkillIntervalTemplateBuilder.BuildAoeTemplate(
                    child,
                    Mathf.Max(1, setup.Count),
                    combatRoot,
                    stackEffect,
                    BuildOnHitSpawnRef(child),
                    AoeTimedSpawnFromDefinition(child),
                    scatterRadiusOverride: setup.ScatterRadius);

            setup.TemplateKey = combatRoot.RegisterTimedSpawnTemplate(in template);
        }

        private static OnHitSpawnRef BuildOnHitSpawnRef(RuntimeProjectileDefinition def)
        {
            if (def == null)
                return default;

            if (def.ImpactProjectileDefinition != null
                && !IsDefault(def.ImpactProjectileDefinition.SpawnTemplateKey))
            {
                return new OnHitSpawnRef
                {
                    Kind = IntervalChildKind.Projectile,
                    TemplateKey = def.ImpactProjectileDefinition.SpawnTemplateKey
                };
            }

            if (def.ImpactAoeDefinition != null
                && !IsDefault(def.ImpactAoeDefinition.SpawnTemplateKey))
            {
                return new OnHitSpawnRef
                {
                    Kind = AoeVariant.AoeChildKindFor(def.ImpactAoeDefinition.LifetimeSeconds),
                    TemplateKey = def.ImpactAoeDefinition.SpawnTemplateKey
                };
            }

            return default;
        }

        private static OnHitSpawnRef BuildOnHitSpawnRef(RuntimeAoeDefinition def)
        {
            if (def == null)
                return default;

            if (def.OnHitProjectileSpawnDefinition != null
                && !IsDefault(def.OnHitProjectileSpawnDefinition.SpawnTemplateKey))
            {
                return new OnHitSpawnRef
                {
                    Kind = IntervalChildKind.Projectile,
                    TemplateKey = def.OnHitProjectileSpawnDefinition.SpawnTemplateKey
                };
            }

            if (def.OnHitAoeSpawnDefinition is RuntimeAoeDefinition onHitAoe
                && !IsDefault(onHitAoe.SpawnTemplateKey))
            {
                return new OnHitSpawnRef
                {
                    Kind = AoeVariant.AoeChildKindFor(onHitAoe.LifetimeSeconds),
                    TemplateKey = onHitAoe.SpawnTemplateKey
                };
            }

            return default;
        }

        private static TimedSpawnComponent ProjectileTimedSpawnFromDefinition(RuntimeProjectileDefinition def)
        {
            TimedSpawnComponent timedSpawn = ProjectileTimedSpawnFromSetup(def.ChildSpawnSetup);
            TimedSpawnComponent aoeTimedSpawn = AoeTimedSpawnFromSetup(def.AoeIntervalSpawnSetup);
            return IsTimedSpawnEnabled(aoeTimedSpawn) ? aoeTimedSpawn : timedSpawn;
        }

        private static TimedSpawnComponent AoeTimedSpawnFromDefinition(RuntimeAoeDefinition def)
        {
            TimedSpawnComponent timedSpawn = ProjectileTimedSpawnFromSetup(def.ChildSpawnSetup);
            TimedSpawnComponent aoeTimedSpawn = AoeTimedSpawnFromSetup(def.AoeIntervalSpawnSetup);
            return IsTimedSpawnEnabled(aoeTimedSpawn) ? aoeTimedSpawn : timedSpawn;
        }

        private static TimedSpawnComponent ProjectileTimedSpawnFromSetup(RuntimeChildSpawnSetup setup)
        {
            if (setup == null || IsDefault(setup.TemplateKey))
                return default;

            return new TimedSpawnComponent
            {
                ChildKind = IntervalChildKind.Projectile,
                JitterSeed = setup.JitterSeed,
                IntervalSeconds = Mathf.Max(0.01f, setup.IntervalSeconds),
                IntervalJitterSeconds = Mathf.Max(0f, setup.IntervalJitterSeconds),
                TemplateKey = setup.TemplateKey
            };
        }

        private static TimedSpawnComponent AoeTimedSpawnFromSetup(RuntimeAoeIntervalSpawnSetup setup)
        {
            if (setup == null || IsDefault(setup.TemplateKey))
                return default;

            return new TimedSpawnComponent
            {
                ChildKind = AoeVariant.AoeChildKindFor(setup.ChildDefinition.LifetimeSeconds),
                JitterSeed = setup.JitterSeed,
                IntervalSeconds = Mathf.Max(0.01f, setup.IntervalSeconds),
                IntervalJitterSeconds = Mathf.Max(0f, setup.IntervalJitterSeconds),
                TemplateKey = setup.TemplateKey
            };
        }

        private static bool IsTimedSpawnEnabled(TimedSpawnComponent timedSpawn) =>
            timedSpawn.JitterSeed > 0
            && timedSpawn.IntervalSeconds > 0f
            && !IsDefault(timedSpawn.TemplateKey);

        private static bool IsDefault(Unity.Entities.Hash128 key) =>
            key.Equals(default(Unity.Entities.Hash128));

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
                if (aoeDef.OnHitProjectileSpawnDefinition != null)
                    RegisterAoeTypesRecursive(aoeDef.OnHitProjectileSpawnDefinition);
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
            aoeDef.RenderId = combatRoot.AoeRenderId(aoeDef.TypeId);
            if (vfxRoot != null)
            {
                vfxRoot.Register(aoeDef.TypeId, 0, definition.SpawnEffect, requireAreaSizeContract: true);
                vfxRoot.Register(aoeDef.TypeId, 1, definition.HitEffect, requireAreaSizeContract: true);
                vfxRoot.Register(aoeDef.TypeId, 2, definition.ExpireEffect, requireAreaSizeContract: true);
                vfxRoot.Register(aoeDef.TypeId, 3, definition.PulseEffect, requireAreaSizeContract: true);
                vfxRoot.Register(aoeDef.TypeId, 4, definition.ArmingEffect, requireAreaSizeContract: true);
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
        public static ProjectileSpawnCommand BuildProjectileTemplate(
            RuntimeProjectileDefinition child,
            ProjectileChildSpawnBehavior behavior,
            CombatRoot root,
            StackEffectSnapshot stackEffect,
            OnHitSpawnRef onHitSpawn = default,
            TimedSpawnComponent timedSpawn = default,
            float jitterDegrees = 0f)
        {
            BasicAttackPrefab prefab = child.Prefab;
            bool hasTimedSpawner = IsTimedSpawnEnabled(timedSpawn);
            CombatRenderComponent render;
            CombatRenderAuthoring authoring;
            if (root != null)
            {
                render = root.ProjectileTemplateRenderComponent(child.RenderId);
                authoring = root.ProjectileTemplateAuthoring(child.RenderId);
            }
            else
            {
                render = ProjectileRenderComponentFor(child.RenderId);
                authoring = ProjectileAuthoringFor(prefab);
            }

            return new ProjectileSpawnCommand
            {
                TypeId = child.TypeId,
                RenderTypeId = child.RenderId,
                HasTimedSpawner = hasTimedSpawner ? 1 : 0,
                Speed = child.Speed,
                Count = Mathf.Max(1, behavior.Count),
                SpreadDegrees = behavior.SpreadDegrees,
                JitterDegrees = Mathf.Max(0f, jitterDegrees),
                SpawnPatternType = behavior.PatternType,
                PierceRemaining = child.PierceCount,
                RepeatHitCooldownSeconds = child.RepeatHitCooldown,
                Lifetime = child.Lifetime,
                // Child templates use their own authored arm time; parent arm time is not inherited.
                ArmSeconds = Mathf.Max(0f, child.ArmSeconds),
                Radius = prefab.Radius,
                RotationRadians = prefab.RotationRadians,
                HalfExtents = new Unity.Mathematics.float2(prefab.HalfExtents.x, prefab.HalfExtents.y),
                ShapeType = prefab.ShapeType,
                HitPayload = new ProjectileHitPayload(
                    new CombatHitPayload
                    {
                        DamageAmount = Mathf.Max(0f, child.Damage),
                        CritChance = child.CritChance,
                        CritMultiplier = child.CritMultiplier,
                        DirectDamageEnabled = child.DirectDamageEnabled,
                        SourceNodeId = default,
                        StackEffect = stackEffect
                    },
                    onHitSpawn),
                Tracking = new ProjectileTrackingComponent
                {
                    TrackingEnabled = child.Tracking.Enabled,
                    TrackingTurnSpeedRadians = child.Tracking.TurnSpeedDegrees * Mathf.Deg2Rad,
                    TrackingQueryCooldownRemaining = child.Tracking.InitialQueryDelaySeconds,
                    TrackingQueryIntervalSeconds = child.Tracking.QueryIntervalSeconds,
                    TrackedTargetId = 0,
                    TrackedTargetIndex = -1,
                    TrackedTargetPosition = default,
                    TrackingRandomState = 0
                },
                Render = render,
                Authoring = authoring,
                TimedSpawn = timedSpawn
            };
        }

        public static AoeSpawnCommand BuildAoeTemplate(
            RuntimeAoeDefinition child,
            int echoCount,
            CombatRoot root,
            StackEffectSnapshot stackEffect,
            OnHitSpawnRef onHitSpawn = default,
            TimedSpawnComponent timedSpawn = default,
            float? scatterRadiusOverride = null)
        {
            AoeSpawnGeometry geometry = child.CreateSpawnGeometry();
            bool hasTimedSpawner = IsTimedSpawnEnabled(timedSpawn);
            CombatRenderComponent render;
            CombatRenderAuthoring authoring;
            if (root != null)
            {
                render = root.AoeTemplateRenderComponent(child.RenderId, geometry);
                authoring = root.AoeTemplateAuthoring(child.RenderId, geometry);
            }
            else
            {
                render = AoeRenderComponentFor(child.RenderId, geometry);
                authoring = AoeAuthoringFor(geometry);
            }

            return new AoeSpawnCommand
            {
                TypeId = child.TypeId,
                RenderTypeId = child.RenderId,
                Lifetime = child.LifetimeSeconds,
                // Child templates use their own authored arm time; parent arm time is not inherited.
                ArmSeconds = Mathf.Max(0f, child.ArmSeconds),
                RepeatHitCooldownSeconds = child.TickIntervalSeconds,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = Mathf.Max(0f, child.Damage),
                    CritChance = child.CritChance,
                    CritMultiplier = child.CritMultiplier,
                    DirectDamageEnabled = child.DirectDamageEnabled,
                    SourceNodeId = default,
                    StackEffect = stackEffect
                },
                AreaSize = geometry.AreaSize,
                Radius = geometry.Radius,
                RotationRadians = geometry.RotationRadians,
                HalfExtents = new Unity.Mathematics.float2(geometry.HalfExtents.x, geometry.HalfExtents.y),
                ShapeType = geometry.ShapeType,
                EchoCount = Mathf.Max(1, echoCount),
                ScatterRadius = Mathf.Max(0f, scatterRadiusOverride ?? child.ScatterRadius),
                Render = render,
                Authoring = authoring,
                OnHitSpawn = onHitSpawn,
                HasTimedSpawner = hasTimedSpawner ? 1 : 0,
                TimedSpawn = timedSpawn
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

        private static CombatRenderComponent ProjectileRenderComponentFor(int renderId)
        {
            return new CombatRenderComponent
            {
                RenderTypeId = renderId > 0 ? renderId : 1,
                AlignToVelocity = 1,
                RenderZ = 0f
            };
        }

        private static CombatRenderAuthoring ProjectileAuthoringFor(BasicAttackPrefab prefab)
        {
            float visualScale = prefab.VisualScale > 0f ? prefab.VisualScale : 1f;
            float radians = prefab.VisualRotationDegrees * Mathf.Deg2Rad;
            return new CombatRenderAuthoring
            {
                VisualScale = new Unity.Mathematics.float2(visualScale, visualScale),
                VisualRotationSin = Mathf.Sin(radians),
                VisualRotationCos = Mathf.Cos(radians)
            };
        }

        private static CombatRenderComponent AoeRenderComponentFor(int renderId, AoeSpawnGeometry geometry)
        {
            if (geometry.VisualScale.x <= 0f && geometry.VisualScale.y <= 0f)
            {
                return default;
            }

            return new CombatRenderComponent
            {
                RenderTypeId = renderId > 0 ? renderId : 1,
                AlignToVelocity = 0,
                RenderZ = CombatRoot.AoeRenderZ
            };
        }

        private static CombatRenderAuthoring AoeAuthoringFor(AoeSpawnGeometry geometry)
        {
            if (geometry.VisualScale.x <= 0f && geometry.VisualScale.y <= 0f)
            {
                return default;
            }

            return new CombatRenderAuthoring
            {
                VisualScale = new Unity.Mathematics.float2(geometry.VisualScale.x, geometry.VisualScale.y),
                VisualRotationSin = geometry.VisualRotationSin,
                VisualRotationCos = geometry.VisualRotationCos
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
            if (aoe.SpawnTemplateKey.Equals(default(Unity.Entities.Hash128)))
                return default;

            float stacksPerHit = Mathf.Max(1, stacking.StacksPerHit);
            return new StackEffectSnapshot
            {
                DebuffKey = stacking.DebuffKey,
                Threshold = threshold,
                StacksPerHit = Mathf.Max(1, stacking.StacksPerHit),
                Lifetime = Mathf.Max(0f, stacking.DebuffLifetimeSeconds),
                Contribution = new StackContribution
                {
                    Damage = Mathf.Max(0f, aoe.Damage) * stacksPerHit / threshold,
                    ProjectileCount = 0,
                    AreaSize = Mathf.Max(0.01f, aoe.AreaSize) * stacksPerHit / threshold
                },
                Faction = CombatFaction.None,
                DetonationKind = AoeVariant.AoeDetonationKindFor(aoe.LifetimeSeconds),
                DetonationKey = aoe.SpawnTemplateKey
            };
        }

        private static StackEffectSnapshot BuildProjectileStackEffectSnapshot(
            RuntimeStackingDetonation stacking,
            CombatRoot root,
            RuntimeProjectileDefinition projectile,
            int threshold)
        {
            if (projectile.SpawnTemplateKey.Equals(default(Unity.Entities.Hash128)))
                return default;

            float stacksPerHit = Mathf.Max(1, stacking.StacksPerHit);
            return new StackEffectSnapshot
            {
                DebuffKey = stacking.DebuffKey,
                Threshold = threshold,
                StacksPerHit = Mathf.Max(1, stacking.StacksPerHit),
                Lifetime = Mathf.Max(0f, stacking.DebuffLifetimeSeconds),
                Contribution = new StackContribution
                {
                    Damage = Mathf.Max(0f, projectile.Damage) * stacksPerHit / threshold,
                    ProjectileCount = Mathf.Max(1, Mathf.RoundToInt(projectile.Count * stacksPerHit)),
                    AreaSize = 0f
                },
                Faction = CombatFaction.None,
                DetonationKind = StackDetonationKind.Projectile,
                DetonationKey = projectile.SpawnTemplateKey
            };
        }

        private static bool IsTimedSpawnEnabled(TimedSpawnComponent timedSpawn) =>
            timedSpawn.JitterSeed > 0
            && timedSpawn.IntervalSeconds > 0f
            && !timedSpawn.TemplateKey.Equals(default(Unity.Entities.Hash128));
    }
}

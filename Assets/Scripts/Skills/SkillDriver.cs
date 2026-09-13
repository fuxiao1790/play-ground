using System;
using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.Common.Stats;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Audio;
using PlayGround.System.Combat.Authoring;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Vfx;
using Unity.Profiling;
using Unity.Entities;
using UnityEngine;

namespace PlayGround.Skills
{
    public sealed class SkillDriver : MonoBehaviour
    {
        [SerializeField] private SkillLoadout loadout;
        [SerializeField] private CombatRoot combatRoot;
        [SerializeField] private CombatVfxRoot vfxRoot;
        [SerializeField] private AudioRoot audioRoot;
        [SerializeField] private UnitStatSheet statSheet;
        [SerializeField] private CombatFaction faction = CombatFaction.Player;
        [SerializeField] private string fallbackCombatRootTag = GameplayTags.PlayerProjectileRoot;

        private RuntimeSkillDefinition[] compiledSlots;
        private SkillSlotState[] slotStates;
        private int[] rootNodeIndices;
        private SkillLoadout runtimeLoadout;
        private SkillValidationWarning[] validationWarnings = Array.Empty<SkillValidationWarning>();
        private static readonly ProfilerMarker TickMarker = new("SkillDriver.Tick");
        private static readonly ProfilerMarker CooldownsMarker = new("SkillDriver.Tick.Cooldowns");
        private static readonly ProfilerMarker SpawnReadySlotsMarker = new("SkillDriver.Tick.SpawnReadySlots");
        private static readonly ProfilerMarker SpawnMarker = new("SkillDriver.Tick.Spawn");
        private static int nextStackingDebuffKey;
        private int activeSlotCount;
        private ulong revision;
        private bool hasPendingEdit;
        private SkillLoadoutEditCommand pendingEdit;
        private bool preserveCooldownState;
        private int cooldownResetNodeIndex = -1;
        private int configuredInitialRuntimeNodeCount;
        private ICombatTarget casterOwner;
        private int[] firedCastTokens;
        private int nextCastToken;
        private bool audioRootSetupErrorLogged;
        private List<(IntervalChildKind Kind, Unity.Entities.Hash128 Key)> registeredTemplateKeys = new();

        public int SlotCount => activeSlotCount;
        public ulong Revision => revision;
        public IReadOnlyList<SkillLoadoutNode> RuntimeNodes => runtimeLoadout?.Nodes;
        public IReadOnlyList<SkillValidationWarning> ValidationWarnings => validationWarnings;
        public SkillSlotState GetSlotState(int index) => slotStates?[index];
        public event Action<ulong> LoadoutChanged;
        public event Action<SkillLoadoutEditResult> EditResolved;

        private void Awake()
        {
            if (combatRoot == null)
                combatRoot = FindRootByTag<CombatRoot>(CombatRootTag);
        }

        private void OnEnable()
        {
            audioRoot ??= AudioRoot.Instance;
            if (audioRoot == null && Application.isPlaying && !audioRootSetupErrorLogged)
            {
                audioRootSetupErrorLogged = true;
                Debug.LogError(
                    $"{nameof(SkillDriver)} on '{name}' is not configured: no {nameof(AudioRoot)} is assigned or active.");
            }
        }

        private void Start()
        {
            runtimeLoadout = loadout == null ? SkillLoadout.CreateEmptyRuntime() : loadout.CreateRuntimeClone();
            runtimeLoadout.EnsureRuntimeNodeCount(configuredInitialRuntimeNodeCount);
            CompileAndRegister();
        }

        public void Tick(bool fireHeld, Vector2 aimDir, Vector2 aimWorldPos)
        {
            using (TickMarker.Auto())
            {
                ProcessPendingEdit();
                if (compiledSlots == null) return;

                float deltaTime = Time.deltaTime;
                if (deltaTime <= 0f) return;

                using (CooldownsMarker.Auto())
                {
                    for (int i = 0; i < activeSlotCount; i++)
                        slotStates[i].Tick(deltaTime);
                }

                if (!fireHeld) return;

                using (SpawnReadySlotsMarker.Auto())
                {
                    for (int i = 0; i < activeSlotCount; i++)
                    {
                        if (!slotStates[i].IsReady) continue;

                        if (compiledSlots[i] is RuntimeProjectileDefinition { SpawnBlocked: true })
                        {
                            slotStates[i].RefundFire();
                            continue;
                        }

                        using (SpawnMarker.Auto())
                        {
                            int castToken = NextCastToken();
                            SkillSpawnTranslator.Spawn(
                                compiledSlots[i],
                                transform.position,
                                aimDir,
                                aimWorldPos,
                                combatRoot,
                                faction,
                                casterOwner?.CombatTargetProxy ?? Entity.Null,
                                castToken);
                            firedCastTokens[i] = castToken;
                        }

                        slotStates[i].ResetOnFire();
                    }
                }
            }
        }

        public void BindCombatRoot(CombatRoot root)
        {
            if (combatRoot == root) return;
            ReleaseTemplateKeys(registeredTemplateKeys);
            combatRoot = root;
            RegisterProjectileTypes();
            RegisterAoeTypes();
            RegisterTargetedTypes();
            RegisterSpawnTemplates();
        }

        public void BindVfxRoot(CombatVfxRoot root)
        {
            if (vfxRoot == root) return;
            vfxRoot = root;
            RegisterProjectileTypes();
            RegisterAoeTypes();
            RegisterTargetedTypes();
        }

        public void BindCaster(ICombatTarget owner)
        {
            casterOwner = owner;
        }

        public void ReceiveSpawnRejected(int castToken)
        {
            if (castToken <= 0 || slotStates == null || firedCastTokens == null)
            {
                return;
            }

            for (int i = 0; i < activeSlotCount; i++)
            {
                if (firedCastTokens[i] != castToken)
                {
                    continue;
                }

                slotStates[i].RefundFire();
                firedCastTokens[i] = 0;
                return;
            }
        }

        // --- private ---

        private string CombatRootTag =>
            string.IsNullOrEmpty(fallbackCombatRootTag)
                ? GameplayTags.PlayerProjectileRoot
                : fallbackCombatRootTag;

        private int NextCastToken()
        {
            nextCastToken++;
            if (nextCastToken <= 0)
            {
                nextCastToken = 1;
            }

            return nextCastToken;
        }

        private void CompileAndRegister()
        {
            if (runtimeLoadout == null) return;

            SkillSlotState[] previousStates = preserveCooldownState ? slotStates : null;
            int[] previousNodeIndices = preserveCooldownState ? this.rootNodeIndices : null;
            int resetNodeIndex = cooldownResetNodeIndex;
            preserveCooldownState = false;
            cooldownResetNodeIndex = -1;

            SkillStatSnapshot snapshot = SkillStatAggregator.Aggregate(runtimeLoadout, statSheet);
            CompiledLoadout compiled = SkillLoadoutCompiler.Compile(runtimeLoadout, snapshot);
            compiledSlots = compiled.Roots;
            slotStates = new SkillSlotState[compiled.Count];
            firedCastTokens = new int[compiled.Count];
            this.rootNodeIndices = compiled.RootNodeIndices;
            activeSlotCount = compiled.Count;

            for (int i = 0; i < compiled.Count; i++)
            {
                RuntimeSkillDefinition def = compiled.Roots[i];
                int nodeIndex = compiled.RootNodeIndices[i];
                SkillSlotState state = nodeIndex != resetNodeIndex
                    ? FindPreservedState(previousStates, previousNodeIndices, nodeIndex)
                    : null;
                state ??= new SkillSlotState();
                state.SetRecoveryTime(def.RecoveryTime);
                slotStates[i] = state;
            }

            RegisterProjectileTypes();
            RegisterAoeTypes();
            RegisterTargetedTypes();
            RegisterSounds();
            RegisterSpawnTemplates(compiled.Warnings);
            validationWarnings = compiled.Warnings.ToArray();
        }

        private void RegisterSounds()
        {
            if (compiledSlots == null)
                return;

            for (int i = 0; i < activeSlotCount; i++)
                RegisterSoundsRecursive(compiledSlots[i], 1);
        }

        private void RegisterSoundsRecursive(RuntimeSkillDefinition definition, int depth)
        {
            if (definition == null || depth > CombatRoot.MaxSpawnChainDepth)
                return;

            if (definition is RuntimeStackingDetonation stackingDetonation)
            {
                RegisterSoundsRecursive(stackingDetonation.Detonation, depth);
                return;
            }

            definition.SoundIds = new SkillSoundIds
            {
                SpawnId = audioRoot != null ? audioRoot.Register(definition.SpawnSound) : 0
            };

            if (definition is RuntimeAoeDefinition aoe)
            {
                if (combatRoot != null && aoe.TypeId >= 0)
                    combatRoot.SetAoeSoundIds(aoe.TypeId, aoe.SoundIds, aoe.SpawnSoundRadius);

                RegisterSoundsRecursive(aoe.ChildSpawnSetup?.ChildDefinition, depth + 1);
                RegisterSoundsRecursive(aoe.AoeIntervalSpawnSetup?.ChildDefinition, depth + 1);
                RegisterSoundsRecursive(aoe.TargetedIntervalSpawnSetup?.ChildDefinition, depth + 1);
                RegisterSoundsRecursive(aoe.OnHitAoeSpawnDefinition, depth + 1);
                RegisterSoundsRecursive(aoe.OnHitTargetedSpawnDefinition, depth + 1);
                RegisterSoundsRecursive(aoe.OnHitProjectileSpawnDefinition, depth + 1);
                RegisterSoundsRecursive(aoe.StackingDetonation, depth + 1);
                return;
            }

            if (definition is RuntimeTargetedDefinition targeted)
            {
                if (combatRoot != null && targeted.TypeId >= 0)
                    combatRoot.SetTargetedSoundIds(
                        targeted.TypeId,
                        targeted.SoundIds,
                        targeted.SpawnSoundRadius);

                RegisterSoundsRecursive(targeted.OnHitAoeSpawnDefinition, depth + 1);
                RegisterSoundsRecursive(targeted.OnHitProjectileSpawnDefinition, depth + 1);
                RegisterSoundsRecursive(targeted.StackingDetonation, depth + 1);
                return;
            }

            if (definition is RuntimeProjectileDefinition projectile)
            {
                RegisterSoundsRecursive(projectile.ChildSpawnSetup?.ChildDefinition, depth + 1);
                RegisterSoundsRecursive(projectile.AoeIntervalSpawnSetup?.ChildDefinition, depth + 1);
                RegisterSoundsRecursive(projectile.TargetedIntervalSpawnSetup?.ChildDefinition, depth + 1);
                RegisterSoundsRecursive(projectile.ImpactAoeDefinition, depth + 1);
                RegisterSoundsRecursive(projectile.ImpactProjectileDefinition, depth + 1);
                RegisterSoundsRecursive(projectile.ImpactTargetedDefinition, depth + 1);
                RegisterSoundsRecursive(projectile.StackingDetonation, depth + 1);
            }
        }

        private static SkillSlotState FindPreservedState(
            SkillSlotState[] states,
            int[] nodeIndices,
            int nodeIndex)
        {
            if (states == null || nodeIndices == null) return null;
            for (int i = 0; i < nodeIndices.Length; i++)
                if (nodeIndices[i] == nodeIndex) return states[i];
            return null;
        }

        // UI configuration requests the initial empty-node count before Start.
        // The driver does not serialize or own a UI capacity policy.
        public void ConfigureInitialRuntimeNodeCount(int nodeCount)
        {
            configuredInitialRuntimeNodeCount = Mathf.Max(0, nodeCount);
        }

        public bool TryRestoreRuntimeLoadout(
            IReadOnlyList<SkillLoadoutRestoreNode> restoredNodes,
            out string rejectionReason)
        {
            const int maxRestoredNodes = 256;
            const int maxRestoredSupportsPerNode = 64;
            if (runtimeLoadout == null)
            {
                rejectionReason = "No runtime loadout is available.";
                return false;
            }

            if (hasPendingEdit)
            {
                rejectionReason = "A loadout edit is pending.";
                return false;
            }

            if (restoredNodes == null || restoredNodes.Count > maxRestoredNodes)
            {
                rejectionReason = "Saved loadout has an invalid node count.";
                return false;
            }

            var replacement = new List<SkillLoadoutNode>(restoredNodes.Count);
            for (int nodeIndex = 0; nodeIndex < restoredNodes.Count; nodeIndex++)
            {
                SkillLoadoutRestoreNode restored = restoredNodes[nodeIndex];
                if (restored == null)
                {
                    rejectionReason = $"Saved loadout node {nodeIndex} is missing.";
                    return false;
                }

                if (restored.Supports.Count > maxRestoredSupportsPerNode)
                {
                    rejectionReason = $"Saved loadout node {nodeIndex} has too many support slots.";
                    return false;
                }

                if (restored.Skill == null)
                {
                    if (restored.Supports.Count > 0 || restored.TriggerToNext != null)
                    {
                        rejectionReason = $"Saved loadout node {nodeIndex} has dependencies but no skill.";
                        return false;
                    }

                    replacement.Add(new SkillLoadoutNode(null));
                    continue;
                }

                var set = ScriptableObject.CreateInstance<SkillSet>();
                set.hideFlags = HideFlags.DontSave;
                set.name = $"{restored.Skill.name} (Restored Runtime)";
                set.SetSkill(restored.Skill);
                int restoredSupportSlotCount = restored.SupportSlotCount < 0
                    ? set.MaxSupportCount
                    : restored.SupportSlotCount;
                if (!set.TrySetSupportSlotCount(restoredSupportSlotCount))
                {
                    rejectionReason = $"Saved loadout node {nodeIndex} has an invalid support cap.";
                    return false;
                }

                if (restored.Supports.Count > set.SupportSlotCount)
                {
                    rejectionReason = $"Saved loadout node {nodeIndex} exceeds its {set.SupportSlotCount}-slot support cap.";
                    return false;
                }

                for (int supportIndex = 0; supportIndex < restored.Supports.Count; supportIndex++)
                {
                    set.SetSupport(supportIndex, restored.Supports[supportIndex]);
                }

                replacement.Add(new SkillLoadoutNode(set, restored.TriggerToNext));
            }

            SkillLoadout previous = runtimeLoadout;
            SkillLoadout candidate = runtimeLoadout.CreateRuntimeClone();
            candidate.ReplaceRuntimeNodes(replacement);
            try
            {
                runtimeLoadout = candidate;
                CompileAndRegister();
            }
            catch (Exception exception)
            {
                runtimeLoadout = previous;
                CompileAndRegister();
                rejectionReason = $"Saved loadout could not compile: {exception.Message}";
                return false;
            }

            revision++;
            LoadoutChanged?.Invoke(revision);
            rejectionReason = null;
            return true;
        }

        private void ProcessPendingEdit()
        {
            if (!hasPendingEdit) return;

            SkillLoadoutEditCommand command = pendingEdit;
            hasPendingEdit = false;
            if (command.ExpectedRevision != revision)
            {
                EditResolved?.Invoke(new SkillLoadoutEditResult(false, revision, "Loadout changed. Reopen the picker."));
                return;
            }

            if (IsCooldownBlocked(command))
            {
                EditResolved?.Invoke(new SkillLoadoutEditResult(false, revision, "This direct-cast skill is on cooldown."));
                return;
            }

            SkillLoadout candidate = runtimeLoadout.CreateRuntimeClone();
            if (!TryApplyEdit(candidate, command, out string rejectionReason))
            {
                EditResolved?.Invoke(new SkillLoadoutEditResult(false, revision, rejectionReason));
                return;
            }

            runtimeLoadout = candidate;
            preserveCooldownState = true;
            cooldownResetNodeIndex = AffectsRootCooldown(command.Kind) ? command.NodeIndex : -1;
            CompileAndRegister();
            revision++;
            LoadoutChanged?.Invoke(revision);
            EditResolved?.Invoke(new SkillLoadoutEditResult(true, revision, null));
        }

        // Only replacing a skill is gated by and resets root cooldown. Removing a skill
        // cannot trigger another cast, so it remains available while its former root is
        // cooling down. Support and trigger edits also preserve cooldown progress.
        private static bool AffectsRootCooldown(SkillLoadoutEditKind kind) =>
            kind is SkillLoadoutEditKind.SetSkill;

        private bool IsCooldownBlocked(SkillLoadoutEditCommand command) =>
            AffectsRootCooldown(command.Kind) && IsRootNodeOnCooldown(command.NodeIndex);

        private bool IsRootNodeOnCooldown(int nodeIndex)
        {
            int rootIndex = FindRootSlotForNode(nodeIndex);
            return rootIndex >= 0 && slotStates[rootIndex] != null && !slotStates[rootIndex].IsReady;
        }

        private bool TryApplyEdit(SkillLoadout candidate, SkillLoadoutEditCommand command, out string rejectionReason)
        {
            if (command.NodeIndex < 0)
            {
                rejectionReason = "That skill position no longer exists.";
                return false;
            }

            if (command.Kind == SkillLoadoutEditKind.SetSkill)
                candidate.EnsureRuntimeNodeCount(command.NodeIndex + 1);

            IReadOnlyList<SkillLoadoutNode> nodes = candidate.Nodes;
            if (command.NodeIndex >= nodes.Count || nodes[command.NodeIndex] == null)
            {
                rejectionReason = "Choose a skill before editing this position.";
                return false;
            }

            SkillLoadoutNode node = nodes[command.NodeIndex];
            switch (command.Kind)
            {
                case SkillLoadoutEditKind.SetSkill:
                    if (command.Skill == null) { rejectionReason = "Choose a skill."; return false; }
                    var set = ScriptableObject.CreateInstance<SkillSet>();
                    set.hideFlags = HideFlags.DontSave;
                    set.name = $"{command.Skill.name} (Runtime)";
                    set.SetSkill(command.Skill);
                    node.SetSkillSet(set);
                    break;
                case SkillLoadoutEditKind.ClearSkill:
                    // The skill owns its supports. Keep adjacent trigger choices so they are
                    // restored if a skill is equipped in this position again.
                    node.SetSkillSet(null);
                    break;
                case SkillLoadoutEditKind.SetSupport:
                case SkillLoadoutEditKind.ClearSupport:
                    if (node.SkillSet == null || command.SupportIndex < 0)
                    { rejectionReason = "Choose a skill before editing supports."; return false; }
                    if (command.SupportIndex >= node.SkillSet.SupportSlotCount)
                    { rejectionReason = $"This skill set currently has {node.SkillSet.SupportSlotCount} support slots."; return false; }
                    node.SkillSet.SetSupport(command.SupportIndex, command.Kind == SkillLoadoutEditKind.SetSupport ? command.Support : null);
                    break;
                case SkillLoadoutEditKind.IncreaseSupportCap:
                    if (node.SkillSet == null)
                    { rejectionReason = "Choose a skill before increasing its support cap."; return false; }
                    if (!node.SkillSet.TryIncreaseSupportSlotCount())
                    { rejectionReason = $"This skill allows at most {node.SkillSet.MaxSupportCount} support slots."; return false; }
                    break;
                case SkillLoadoutEditKind.DecreaseSupportCap:
                    if (node.SkillSet == null)
                    { rejectionReason = "Choose a skill before decreasing its support cap."; return false; }
                    if (!node.SkillSet.TryDecreaseSupportSlotCount())
                    { rejectionReason = "This skill set has no support slots to remove."; return false; }
                    break;
                case SkillLoadoutEditKind.SetTrigger:
                    if (command.Trigger == null) { rejectionReason = "Choose a trigger."; return false; }
                    node.SetTriggerToNext(command.Trigger);
                    break;
                case SkillLoadoutEditKind.ClearTrigger:
                    node.SetTriggerToNext(null);
                    break;
                default:
                    rejectionReason = "Unknown loadout edit.";
                    return false;
            }

            rejectionReason = null;
            return true;
        }

        private int FindRootSlotForNode(int nodeIndex)
        {
            if (rootNodeIndices == null) return -1;
            for (int i = 0; i < activeSlotCount; i++)
                if (rootNodeIndices[i] == nodeIndex) return i;
            return -1;
        }

        private void RegisterProjectileTypes()
        {
            if (compiledSlots == null) return;
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
                if (aoeDef.TargetedIntervalSpawnSetup?.ChildDefinition != null)
                    RegisterProjectileTypesRecursive(aoeDef.TargetedIntervalSpawnSetup.ChildDefinition);
                if (aoeDef.OnHitAoeSpawnDefinition != null)
                    RegisterProjectileTypesRecursive(aoeDef.OnHitAoeSpawnDefinition);
                if (aoeDef.OnHitTargetedSpawnDefinition != null)
                    RegisterProjectileTypesRecursive(aoeDef.OnHitTargetedSpawnDefinition);
                if (aoeDef.OnHitProjectileSpawnDefinition != null)
                    RegisterProjectileTypesRecursive(aoeDef.OnHitProjectileSpawnDefinition);
                if (aoeDef.StackingDetonation != null)
                    RegisterProjectileTypesRecursive(aoeDef.StackingDetonation);
            }

            if (def is RuntimeTargetedDefinition targetedDef)
            {
                if (targetedDef.OnHitAoeSpawnDefinition != null)
                {
                    RegisterAoeTypeDefinition(targetedDef.OnHitAoeSpawnDefinition);
                    RegisterProjectileTypesRecursive(targetedDef.OnHitAoeSpawnDefinition);
                }
                if (targetedDef.OnHitProjectileSpawnDefinition != null)
                    RegisterProjectileTypesRecursive(targetedDef.OnHitProjectileSpawnDefinition);
                if (targetedDef.StackingDetonation != null)
                    RegisterProjectileTypesRecursive(targetedDef.StackingDetonation);
            }

            if (def is RuntimeProjectileDefinition projDef && projDef.Prefab != null)
            {
                RegisterProjectileVfx(projDef);
                if (combatRoot != null && projDef.TypeId < 0)
                {
                    projDef.TypeId = combatRoot.RegisterTemplate(projDef.Prefab);
                    projDef.RenderId = combatRoot.ProjectileRenderId(projDef.TypeId);
                }

            }

            if (def is RuntimeProjectileDefinition p)
            {
                if (p.ChildSpawnSetup?.ChildDefinition != null)
                    RegisterProjectileTypesRecursive(p.ChildSpawnSetup.ChildDefinition);
                if (p.AoeIntervalSpawnSetup?.ChildDefinition != null)
                    RegisterProjectileTypesRecursive(p.AoeIntervalSpawnSetup.ChildDefinition);
                if (p.TargetedIntervalSpawnSetup?.ChildDefinition != null)
                    RegisterProjectileTypesRecursive(p.TargetedIntervalSpawnSetup.ChildDefinition);
                if (p.ImpactAoeDefinition != null)
                {
                    RegisterAoeTypeDefinition(p.ImpactAoeDefinition);
                    RegisterProjectileTypesRecursive(p.ImpactAoeDefinition);
                }
                if (p.ImpactProjectileDefinition != null)
                    RegisterProjectileTypesRecursive(p.ImpactProjectileDefinition);
                if (p.ImpactTargetedDefinition != null)
                    RegisterProjectileTypesRecursive(p.ImpactTargetedDefinition);
                if (p.StackingDetonation != null)
                    RegisterProjectileTypesRecursive(p.StackingDetonation);
            }
        }

        private void RegisterAoeTypes()
        {
            if (compiledSlots == null) return;
            for (int i = 0; i < activeSlotCount; i++)
                RegisterAoeTypesRecursive(compiledSlots[i]);
        }

        private void RegisterTargetedTypes()
        {
            if (compiledSlots == null) return;
            for (int i = 0; i < activeSlotCount; i++)
                RegisterTargetedTypesRecursive(compiledSlots[i]);
        }

        private void RegisterSpawnTemplates(List<SkillValidationWarning> warnings = null)
        {
            if (combatRoot == null || compiledSlots == null) return;

            List<(IntervalChildKind Kind, Unity.Entities.Hash128 Key)> previous = registeredTemplateKeys;
            registeredTemplateKeys = new List<(IntervalChildKind Kind, Unity.Entities.Hash128 Key)>();
            for (int i = 0; i < activeSlotCount; i++)
                RegisterSpawnTemplatesRecursive(compiledSlots[i], 1, warnings, i);

            ReleaseTemplateKeys(previous);
        }

        private bool RegisterSpawnTemplatesRecursive(
            RuntimeSkillDefinition def,
            int depth,
            List<SkillValidationWarning> warnings,
            int slotIndex,
            ProjectileChildSpawnPatternType selfPattern = ProjectileChildSpawnPatternType.Forward,
            // Interval edges register their own behavior-specific templates. Root, on-hit,
            // and stacking edges need the definition's generic key instead.
            bool registerSelfTemplate = true)
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
                    ProjectileChildSpawnPatternType.Radial,
                    registerSelfTemplate: true);
            }

            if (def is RuntimeAoeDefinition aoeDef)
            {
                if (aoeDef.ChildSpawnSetup?.ChildDefinition != null)
                {
                    if (RegisterSpawnTemplatesRecursive(
                            aoeDef.ChildSpawnSetup.ChildDefinition,
                            depth + 1,
                            warnings,
                            slotIndex,
                            registerSelfTemplate: false))
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
                            slotIndex,
                            registerSelfTemplate: false))
                    {
                        RegisterAoeIntervalTemplate(aoeDef.AoeIntervalSpawnSetup);
                    }
                }

                if (aoeDef.TargetedIntervalSpawnSetup?.ChildDefinition != null)
                {
                    if (RegisterSpawnTemplatesRecursive(
                            aoeDef.TargetedIntervalSpawnSetup.ChildDefinition,
                            depth + 1,
                            warnings,
                            slotIndex,
                            registerSelfTemplate: false))
                    {
                        RegisterTargetedIntervalTemplate(aoeDef.TargetedIntervalSpawnSetup);
                    }
                }

                if (aoeDef.OnHitAoeSpawnDefinition != null)
                    RegisterSpawnTemplatesRecursive(aoeDef.OnHitAoeSpawnDefinition, depth + 1, warnings, slotIndex);
                if (aoeDef.OnHitTargetedSpawnDefinition != null)
                    RegisterSpawnTemplatesRecursive(aoeDef.OnHitTargetedSpawnDefinition, depth + 1, warnings, slotIndex);
                if (aoeDef.OnHitProjectileSpawnDefinition != null)
                    RegisterSpawnTemplatesRecursive(
                        aoeDef.OnHitProjectileSpawnDefinition,
                        depth + 1,
                        warnings,
                        slotIndex,
                        ProjectileChildSpawnPatternType.SideSpray);
                if (aoeDef.StackingDetonation != null)
                    RegisterSpawnTemplatesRecursive(aoeDef.StackingDetonation, depth + 1, warnings, slotIndex);

                StackEffectSnapshot stackEffect =
                    SkillIntervalTemplateBuilder.BuildApplicatorStackEffectSnapshot(aoeDef, combatRoot);
                AoeSpawnCommand template =
                    SkillIntervalTemplateBuilder.BuildAoeTemplate(
                        aoeDef,
                        combatRoot,
                        stackEffect,
                        BuildOnHitSpawnRef(aoeDef),
                        AoeTimedSpawnFromDefinition(aoeDef));
                if (registerSelfTemplate)
                {
                    Unity.Entities.Hash128 key = combatRoot.RegisterSpawnTemplate(in template);
                    aoeDef.SpawnTemplateKey = key;
                    registeredTemplateKeys.Add((IntervalChildKind.ImpactAoe, key));
                }
                else
                {
                    aoeDef.SpawnTemplateKey = default;
                }
                return true;
            }

            if (def is RuntimeTargetedDefinition targetedDef)
            {
                if (targetedDef.OnHitAoeSpawnDefinition != null)
                    RegisterSpawnTemplatesRecursive(targetedDef.OnHitAoeSpawnDefinition, depth + 1, warnings, slotIndex);
                if (targetedDef.OnHitProjectileSpawnDefinition != null)
                    RegisterSpawnTemplatesRecursive(targetedDef.OnHitProjectileSpawnDefinition, depth + 1, warnings, slotIndex);
                if (targetedDef.StackingDetonation != null)
                    RegisterSpawnTemplatesRecursive(targetedDef.StackingDetonation, depth + 1, warnings, slotIndex);

                TargetedSpawnCommand template =
                    SkillIntervalTemplateBuilder.BuildTargetedTemplate(
                        targetedDef,
                        combatRoot,
                        SkillIntervalTemplateBuilder.BuildApplicatorStackEffectSnapshot(targetedDef, combatRoot),
                        BuildOnHitSpawnRef(targetedDef));
                if (registerSelfTemplate)
                {
                    Unity.Entities.Hash128 key = combatRoot.RegisterSpawnTemplate(in template);
                    targetedDef.SpawnTemplateKey = key;
                    registeredTemplateKeys.Add((IntervalChildKind.Targeted, key));
                }
                else
                {
                    targetedDef.SpawnTemplateKey = default;
                }
                return true;
            }

            if (def is RuntimeProjectileDefinition projDef)
            {
                if (projDef.SpawnBlocked)
                    return false;

                if (projDef.ChildSpawnSetup?.ChildDefinition != null)
                {
                    if (RegisterSpawnTemplatesRecursive(
                            projDef.ChildSpawnSetup.ChildDefinition,
                            depth + 1,
                            warnings,
                            slotIndex,
                            registerSelfTemplate: false))
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
                            slotIndex,
                            registerSelfTemplate: false))
                    {
                        RegisterAoeIntervalTemplate(projDef.AoeIntervalSpawnSetup);
                    }
                }

                if (projDef.TargetedIntervalSpawnSetup?.ChildDefinition != null)
                {
                    if (RegisterSpawnTemplatesRecursive(
                            projDef.TargetedIntervalSpawnSetup.ChildDefinition,
                            depth + 1,
                            warnings,
                            slotIndex,
                            registerSelfTemplate: false))
                    {
                        RegisterTargetedIntervalTemplate(projDef.TargetedIntervalSpawnSetup);
                    }
                }

                if (projDef.ImpactAoeDefinition != null)
                    RegisterSpawnTemplatesRecursive(projDef.ImpactAoeDefinition, depth + 1, warnings, slotIndex);
                if (projDef.ImpactTargetedDefinition != null)
                    RegisterSpawnTemplatesRecursive(projDef.ImpactTargetedDefinition, depth + 1, warnings, slotIndex);
                if (projDef.ImpactProjectileDefinition != null)
                    RegisterSpawnTemplatesRecursive(
                        projDef.ImpactProjectileDefinition,
                        depth + 1,
                        warnings,
                        slotIndex,
                        ProjectileChildSpawnPatternType.SideSpray);
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
                if (registerSelfTemplate)
                {
                    Unity.Entities.Hash128 key = combatRoot.RegisterSpawnTemplate(in template);
                    projDef.SpawnTemplateKey = key;
                    registeredTemplateKeys.Add((IntervalChildKind.Projectile, key));
                }
                else
                {
                    projDef.SpawnTemplateKey = default;
                }
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

            Unity.Entities.Hash128 key = combatRoot.RegisterTimedSpawnTemplate(in template);
            setup.TemplateKey = key;
            registeredTemplateKeys.Add((IntervalChildKind.Projectile, key));
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
                    combatRoot,
                    stackEffect,
                    BuildOnHitSpawnRef(child),
                    AoeTimedSpawnFromDefinition(child));

            Unity.Entities.Hash128 key = combatRoot.RegisterTimedSpawnTemplate(in template);
            setup.TemplateKey = key;
            registeredTemplateKeys.Add((IntervalChildKind.ImpactAoe, key));
        }

        private void RegisterTargetedIntervalTemplate(RuntimeTargetedIntervalSpawnSetup setup)
        {
            RuntimeTargetedDefinition child = setup?.ChildDefinition;
            if (setup == null || child == null || child.TypeId < 0)
                return;

            TargetedSpawnCommand template =
                SkillIntervalTemplateBuilder.BuildTargetedTemplate(
                    child,
                    combatRoot,
                    SkillIntervalTemplateBuilder.BuildApplicatorStackEffectSnapshot(child, combatRoot),
                    BuildOnHitSpawnRef(child));
            Unity.Entities.Hash128 key = combatRoot.RegisterTimedSpawnTemplate(in template);
            setup.TemplateKey = key;
            registeredTemplateKeys.Add((IntervalChildKind.Targeted, key));
        }

        // Called only while the sim is live: on recompile and on rebind. There is no
        // teardown release. The count maps are scope-owned and freed wholesale by
        // CombatScopeOwner, and the ECS world is gone before MonoBehaviour teardown runs,
        // so a shutdown unregister is a write nobody reads through a dead EntityManager.
        // A driver destroyed at runtime would have to release at that despawn site.
        private void ReleaseTemplateKeys(List<(IntervalChildKind Kind, Unity.Entities.Hash128 Key)> keys)
        {
            if (combatRoot == null || keys == null)
            {
                return;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                (IntervalChildKind kind, Unity.Entities.Hash128 key) = keys[i];
                combatRoot.UnregisterSpawnTemplate(kind, key);
            }

            keys.Clear();
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

            if (def.ImpactTargetedDefinition != null
                && !IsDefault(def.ImpactTargetedDefinition.SpawnTemplateKey))
            {
                return new OnHitSpawnRef
                {
                    Kind = IntervalChildKind.Targeted,
                    TemplateKey = def.ImpactTargetedDefinition.SpawnTemplateKey
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

            if (def.OnHitAoeSpawnDefinition != null
                && !IsDefault(def.OnHitAoeSpawnDefinition.SpawnTemplateKey))
            {
                return new OnHitSpawnRef
                {
                    Kind = AoeVariant.AoeChildKindFor(def.OnHitAoeSpawnDefinition.LifetimeSeconds),
                    TemplateKey = def.OnHitAoeSpawnDefinition.SpawnTemplateKey
                };
            }

            if (def.OnHitTargetedSpawnDefinition != null
                && !IsDefault(def.OnHitTargetedSpawnDefinition.SpawnTemplateKey))
            {
                return new OnHitSpawnRef
                {
                    Kind = IntervalChildKind.Targeted,
                    TemplateKey = def.OnHitTargetedSpawnDefinition.SpawnTemplateKey
                };
            }

            return default;
        }

        private static OnHitSpawnRef BuildOnHitSpawnRef(RuntimeTargetedDefinition def)
        {
            if (def == null)
                return default;

            if (def.OnHitAoeSpawnDefinition != null
                && !IsDefault(def.OnHitAoeSpawnDefinition.SpawnTemplateKey))
            {
                return new OnHitSpawnRef
                {
                    Kind = AoeVariant.AoeChildKindFor(def.OnHitAoeSpawnDefinition.LifetimeSeconds),
                    TemplateKey = def.OnHitAoeSpawnDefinition.SpawnTemplateKey
                };
            }

            if (def.OnHitProjectileSpawnDefinition != null
                && !IsDefault(def.OnHitProjectileSpawnDefinition.SpawnTemplateKey))
            {
                return new OnHitSpawnRef
                {
                    Kind = IntervalChildKind.Projectile,
                    TemplateKey = def.OnHitProjectileSpawnDefinition.SpawnTemplateKey
                };
            }

            return default;
        }

        private static TimedSpawnComponent ProjectileTimedSpawnFromDefinition(RuntimeProjectileDefinition def)
        {
            TimedSpawnComponent timedSpawn = ProjectileTimedSpawnFromSetup(def.ChildSpawnSetup);
            TimedSpawnComponent aoeTimedSpawn = AoeTimedSpawnFromSetup(def.AoeIntervalSpawnSetup);
            TimedSpawnComponent targetedTimedSpawn = TargetedTimedSpawnFromSetup(def.TargetedIntervalSpawnSetup);
            if (IsTimedSpawnEnabled(targetedTimedSpawn)) return targetedTimedSpawn;
            return IsTimedSpawnEnabled(aoeTimedSpawn) ? aoeTimedSpawn : timedSpawn;
        }

        private static TimedSpawnComponent AoeTimedSpawnFromDefinition(RuntimeAoeDefinition def)
        {
            TimedSpawnComponent timedSpawn = ProjectileTimedSpawnFromSetup(def.ChildSpawnSetup);
            TimedSpawnComponent aoeTimedSpawn = AoeTimedSpawnFromSetup(def.AoeIntervalSpawnSetup);
            TimedSpawnComponent targetedTimedSpawn = TargetedTimedSpawnFromSetup(def.TargetedIntervalSpawnSetup);
            if (IsTimedSpawnEnabled(targetedTimedSpawn)) return targetedTimedSpawn;
            return IsTimedSpawnEnabled(aoeTimedSpawn) ? aoeTimedSpawn : timedSpawn;
        }

        private static TimedSpawnComponent TargetedTimedSpawnFromSetup(RuntimeTargetedIntervalSpawnSetup setup)
        {
            if (setup == null || IsDefault(setup.TemplateKey))
                return default;

            return new TimedSpawnComponent
            {
                ChildKind = IntervalChildKind.Targeted,
                JitterSeed = setup.JitterSeed,
                EnergyPerSecond = Mathf.Max(0.01f, setup.EnergyPerSecond),
                InitialEnergyPercent = Mathf.Clamp(setup.InitialEnergyPercent, 0f, 100f),
                EnergyThreshold = Mathf.Max(1e-3f, setup.EnergyThreshold),
                TemplateKey = setup.TemplateKey
            };
        }

        private static TimedSpawnComponent ProjectileTimedSpawnFromSetup(RuntimeChildSpawnSetup setup)
        {
            if (setup == null || IsDefault(setup.TemplateKey))
                return default;

            return new TimedSpawnComponent
            {
                ChildKind = IntervalChildKind.Projectile,
                JitterSeed = setup.JitterSeed,
                EnergyPerSecond = Mathf.Max(0.01f, setup.EnergyPerSecond),
                InitialEnergyPercent = Mathf.Clamp(setup.InitialEnergyPercent, 0f, 100f),
                EnergyThreshold = Mathf.Max(1e-3f, setup.EnergyThreshold),
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
                EnergyPerSecond = Mathf.Max(0.01f, setup.EnergyPerSecond),
                InitialEnergyPercent = Mathf.Clamp(setup.InitialEnergyPercent, 0f, 100f),
                EnergyThreshold = Mathf.Max(1e-3f, setup.EnergyThreshold),
                TemplateKey = setup.TemplateKey
            };
        }

        private static bool IsTimedSpawnEnabled(TimedSpawnComponent timedSpawn) =>
            timedSpawn.JitterSeed > 0
            && timedSpawn.EnergyPerSecond > 0f
            && timedSpawn.EnergyThreshold > 0f
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
                if (aoeDef.TargetedIntervalSpawnSetup?.ChildDefinition != null)
                    RegisterAoeTypesRecursive(aoeDef.TargetedIntervalSpawnSetup.ChildDefinition);
                if (aoeDef.OnHitAoeSpawnDefinition != null)
                    RegisterAoeTypesRecursive(aoeDef.OnHitAoeSpawnDefinition);
                if (aoeDef.OnHitTargetedSpawnDefinition != null)
                    RegisterAoeTypesRecursive(aoeDef.OnHitTargetedSpawnDefinition);
                if (aoeDef.OnHitProjectileSpawnDefinition != null)
                    RegisterAoeTypesRecursive(aoeDef.OnHitProjectileSpawnDefinition);
                if (aoeDef.StackingDetonation != null)
                    RegisterAoeTypesRecursive(aoeDef.StackingDetonation);
            }

            if (def is RuntimeTargetedDefinition targetedDef)
            {
                RegisterAoeTypesRecursive(targetedDef.OnHitAoeSpawnDefinition);
                RegisterAoeTypesRecursive(targetedDef.OnHitProjectileSpawnDefinition);
                RegisterAoeTypesRecursive(targetedDef.StackingDetonation);
            }

            if (def is RuntimeProjectileDefinition projDef)
            {
                if (projDef.ChildSpawnSetup?.ChildDefinition != null)
                    RegisterAoeTypesRecursive(projDef.ChildSpawnSetup.ChildDefinition);
                if (projDef.AoeIntervalSpawnSetup?.ChildDefinition != null)
                    RegisterAoeTypesRecursive(projDef.AoeIntervalSpawnSetup.ChildDefinition);
                if (projDef.TargetedIntervalSpawnSetup?.ChildDefinition != null)
                    RegisterAoeTypesRecursive(projDef.TargetedIntervalSpawnSetup.ChildDefinition);
                if (projDef.ImpactAoeDefinition != null)
                    RegisterAoeTypesRecursive(projDef.ImpactAoeDefinition);
                if (projDef.ImpactTargetedDefinition != null)
                    RegisterAoeTypesRecursive(projDef.ImpactTargetedDefinition);
                if (projDef.ImpactProjectileDefinition != null)
                    RegisterAoeTypesRecursive(projDef.ImpactProjectileDefinition);
                if (projDef.StackingDetonation != null)
                    RegisterAoeTypesRecursive(projDef.StackingDetonation);
            }
        }

        private void RegisterTargetedTypesRecursive(RuntimeSkillDefinition def)
        {
            if (def == null) return;

            if (def is RuntimeStackingDetonation stackingDetonation)
            {
                EnsureStackingDetonationDebuffKey(stackingDetonation);
                RegisterTargetedTypesRecursive(stackingDetonation.Detonation);
                return;
            }

            if (def is RuntimeTargetedDefinition targetedDef)
            {
                RegisterTargetedTypeDefinition(targetedDef);
                RegisterTargetedTypesRecursive(targetedDef.OnHitAoeSpawnDefinition);
                RegisterTargetedTypesRecursive(targetedDef.OnHitProjectileSpawnDefinition);
                RegisterTargetedTypesRecursive(targetedDef.StackingDetonation);
                return;
            }

            if (def is RuntimeAoeDefinition aoeDef)
            {
                RegisterTargetedTypesRecursive(aoeDef.ChildSpawnSetup?.ChildDefinition);
                RegisterTargetedTypesRecursive(aoeDef.AoeIntervalSpawnSetup?.ChildDefinition);
                RegisterTargetedTypesRecursive(aoeDef.TargetedIntervalSpawnSetup?.ChildDefinition);
                RegisterTargetedTypesRecursive(aoeDef.OnHitAoeSpawnDefinition);
                RegisterTargetedTypesRecursive(aoeDef.OnHitTargetedSpawnDefinition);
                RegisterTargetedTypesRecursive(aoeDef.OnHitProjectileSpawnDefinition);
                RegisterTargetedTypesRecursive(aoeDef.StackingDetonation);
                return;
            }

            if (def is RuntimeProjectileDefinition projDef)
            {
                RegisterTargetedTypesRecursive(projDef.ChildSpawnSetup?.ChildDefinition);
                RegisterTargetedTypesRecursive(projDef.AoeIntervalSpawnSetup?.ChildDefinition);
                RegisterTargetedTypesRecursive(projDef.TargetedIntervalSpawnSetup?.ChildDefinition);
                RegisterTargetedTypesRecursive(projDef.ImpactAoeDefinition);
                RegisterTargetedTypesRecursive(projDef.ImpactTargetedDefinition);
                RegisterTargetedTypesRecursive(projDef.ImpactProjectileDefinition);
                RegisterTargetedTypesRecursive(projDef.StackingDetonation);
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
            if (aoeDef == null) return;
            AoeTypeDefinition definition = aoeDef.CreateTypeDefinition();
            RegisterAoeVfx(aoeDef, definition);
            if (combatRoot != null && aoeDef.TypeId < 0)
            {
                aoeDef.TypeId = combatRoot.RegisterType(definition);
                aoeDef.RenderId = combatRoot.AoeRenderId(aoeDef.TypeId);
            }
            else if (combatRoot != null && aoeDef.TypeId >= 0)
            {
                combatRoot.SetAoeVfxIds(aoeDef.TypeId, aoeDef.VfxIds);
            }
        }

        private void RegisterTargetedTypeDefinition(RuntimeTargetedDefinition targetedDef)
        {
            if (targetedDef == null) return;

            TargetedTypeDefinition definition = targetedDef.CreateTypeDefinition();
            RegisterTargetedVfx(targetedDef, definition);
            if (combatRoot != null && targetedDef.TypeId < 0)
            {
                targetedDef.TypeId = combatRoot.RegisterTargetedType(definition);
                targetedDef.RenderId = combatRoot.TargetedRenderId(targetedDef.TypeId);
            }
            else if (combatRoot != null && targetedDef.TypeId >= 0)
            {
                combatRoot.SetTargetedVfxIds(targetedDef.TypeId, targetedDef.VfxIds);
            }
        }

        public bool TryQueueEdit(SkillLoadoutEditCommand command, out string rejectionReason)
        {
            if (runtimeLoadout == null)
            {
                rejectionReason = "No runtime loadout is available.";
                return false;
            }

            if (hasPendingEdit)
            {
                rejectionReason = "Another loadout edit is pending.";
                return false;
            }

            if (command.ExpectedRevision != revision)
            {
                rejectionReason = "Loadout changed. Reopen the picker.";
                return false;
            }

            hasPendingEdit = true;
            pendingEdit = command;
            rejectionReason = null;
            return true;
        }

        public float GetCooldownProgressForNode(int nodeIndex)
        {
            int rootIndex = FindRootSlotForNode(nodeIndex);
            return rootIndex >= 0 && slotStates?[rootIndex] != null
                ? slotStates[rootIndex].CooldownProgress
                : 0f;
        }

        public bool IsRootOnCooldown(int nodeIndex) => IsRootNodeOnCooldown(nodeIndex);

        private void RegisterAoeVfx(RuntimeAoeDefinition aoeDef, AoeTypeDefinition definition)
        {
            if (aoeDef == null || definition == null)
                return;

            AoeVfxIds vfxIds = default;
            if (vfxRoot != null)
            {
                vfxIds = new AoeVfxIds
                {
                    SpawnId = vfxRoot.Register(definition.SpawnEffect, aoeDef.SpawnEffectShape),
                    HitId = vfxRoot.Register(definition.HitEffect, aoeDef.HitEffectShape),
                    ExpireId = vfxRoot.Register(definition.ExpireEffect, aoeDef.ExpireEffectShape),
                    PulseId = vfxRoot.Register(definition.PulseEffect, aoeDef.PulseEffectShape),
                    ArmingId = vfxRoot.Register(definition.ArmingEffect, aoeDef.ArmingEffectShape)
                };
            }

            aoeDef.VfxIds = vfxIds;
            definition.SetVfxIds(vfxIds);
        }

        private void RegisterTargetedVfx(
            RuntimeTargetedDefinition targetedDef,
            TargetedTypeDefinition definition)
        {
            if (targetedDef == null || definition == null)
                return;

            TargetedVfxIds vfxIds = default;
            if (vfxRoot != null && targetedDef.Prefab != null)
            {
                vfxIds = new TargetedVfxIds
                {
                    SpawnId = vfxRoot.Register(definition.SpawnEffect, targetedDef.Prefab.SpawnEffectShape),
                    HitId = vfxRoot.Register(definition.HitEffect, targetedDef.Prefab.HitEffectShape),
                    ExpireId = vfxRoot.Register(definition.ExpireEffect, targetedDef.Prefab.ExpireEffectShape),
                    LinkId = vfxRoot.Register(definition.LinkEffect, targetedDef.Prefab.LinkEffectShape),
                    ArmingId = vfxRoot.Register(definition.ArmingEffect, targetedDef.Prefab.ArmingEffectShape)
                };
            }

            targetedDef.VfxIds = vfxIds;
            definition.SetVfxIds(vfxIds);
        }

        private void RegisterProjectileVfx(RuntimeProjectileDefinition projDef)
        {
            if (projDef?.Prefab == null)
                return;

            projDef.TrailVfxId = vfxRoot != null
                ? vfxRoot.Register(projDef.Prefab.TrailEffect, projDef.Prefab.TrailEffectShape)
                : 0;
            projDef.TrailWidth = projDef.Prefab.TrailWidth;
            projDef.TrailStepDistance = projDef.Prefab.TrailStepDistance;
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
                SoundIds = child.SoundIds,
                SpawnSoundRadius = child.SpawnSoundRadius,
                RenderTypeId = child.RenderId,
                TrailVfxId = child.TrailVfxId,
                TrailWidth = child.TrailWidth,
                TrailStepDistance = child.TrailStepDistance,
                HasTimedSpawner = hasTimedSpawner ? 1 : 0,
                ContinuousCollision = child.ContinuousCollision ? 1 : 0,
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
            CombatRoot root,
            StackEffectSnapshot stackEffect,
            OnHitSpawnRef onHitSpawn = default,
            TimedSpawnComponent timedSpawn = default)
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
                VfxIds = child.VfxIds,
                SoundIds = child.SoundIds,
                SpawnSoundRadius = child.SpawnSoundRadius,
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
                EchoCount = Mathf.Max(1, child.EchoCount),
                ScatterRadius = Mathf.Max(0f, child.ScatterRadius),
                Render = render,
                Authoring = authoring,
                OnHitSpawn = onHitSpawn,
                HasTimedSpawner = hasTimedSpawner ? 1 : 0,
                TimedSpawn = timedSpawn
            };
        }

        public static TargetedSpawnCommand BuildTargetedTemplate(
            RuntimeTargetedDefinition child,
            CombatRoot root,
            StackEffectSnapshot stackEffect,
            OnHitSpawnRef onHitSpawn = default)
        {
            CombatRenderComponent render;
            CombatRenderAuthoring authoring;
            if (root != null)
            {
                render = root.TargetedTemplateRenderComponent(child.RenderId);
                authoring = root.TargetedTemplateAuthoring(child.RenderId);
            }
            else
            {
                render = TargetedRenderComponentFor(child.RenderId);
                authoring = TargetedAuthoringFor(child.Prefab);
            }

            return new TargetedSpawnCommand
            {
                TypeId = child.TypeId,
                SoundIds = child.SoundIds,
                SpawnSoundRadius = child.SpawnSoundRadius,
                RenderTypeId = child.RenderId,
                EchoCount = Mathf.Max(1, child.EchoCount),
                // Fail-safe only; the resolve expires the instance the moment its walk ends.
                LifetimeSeconds = RuntimeTargetedDefinition.LifetimeFor(
                    child.ChainCount, child.ChainDelay),
                ArmSeconds = Mathf.Max(0f, child.ArmSeconds),
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = Mathf.Max(0f, child.Damage),
                    CritChance = child.CritChance,
                    CritMultiplier = child.CritMultiplier,
                    DirectDamageEnabled = child.DirectDamageEnabled,
                    SourceNodeId = default,
                    StackEffect = stackEffect
                },
                Resolve = new TargetedResolveConfig
                {
                    ChainDistance = Mathf.Max(0f, child.ChainDistance),
                    ChainDamageFalloff = Mathf.Max(0f, child.ChainDamageFalloff),
                    ChainDelay = Mathf.Max(0f, child.ChainDelay),
                    ChainCount = Mathf.Max(1, child.ChainCount)
                },
                VfxIds = child.VfxIds,
                VfxSize = child.VfxSize,
                Render = render,
                Authoring = authoring,
                OnHitSpawn = onHitSpawn
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
            else if (def is RuntimeTargetedDefinition targeted)
                stackEffect = BuildStackEffectSnapshot(targeted.StackingDetonation, root);

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

        private static CombatRenderComponent TargetedRenderComponentFor(int renderId)
        {
            if (renderId <= 0)
                return default;

            return new CombatRenderComponent
            {
                RenderTypeId = renderId,
                // The resolve writes kinematics.Velocity as the link direction; the sprite faces
                // it. Requirements §6.1.
                AlignToVelocity = 1,
                RenderZ = CombatRoot.AoeRenderZ
            };
        }

        // Root-less fallback (EditMode): no render registry, so no sprite native size to fold in —
        // the authored transform scale is all there is. Mirrors ProjectileAuthoringFor.
        private static CombatRenderAuthoring TargetedAuthoringFor(TargetedPrefab prefab)
        {
            if (prefab == null || prefab.Sprite == null)
                return default;

            Vector2 visualScale = prefab.VisualScale;
            float radians = prefab.VisualRotationDegrees * Mathf.Deg2Rad;
            return new CombatRenderAuthoring
            {
                VisualScale = new Unity.Mathematics.float2(
                    visualScale.x > 0f ? visualScale.x : 1f,
                    visualScale.y > 0f ? visualScale.y : 1f),
                VisualRotationSin = Mathf.Sin(radians),
                VisualRotationCos = Mathf.Cos(radians)
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
            && timedSpawn.EnergyPerSecond > 0f
            && timedSpawn.EnergyThreshold > 0f
            && !timedSpawn.TemplateKey.Equals(default(Unity.Entities.Hash128));
    }
}

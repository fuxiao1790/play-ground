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
                    rootSlotIndices.Add(i);
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

        private void RegisterProjectileTypes()
        {
            if (combatRoot == null || compiledSlots == null) return;
            for (int i = 0; i < activeSlotCount; i++)
                RegisterProjectileTypesRecursive(compiledSlots[i]);
        }

        private void RegisterProjectileTypesRecursive(RuntimeSkillDefinition def)
        {
            if (def == null) return;

            if (def is RuntimeStackingSkillDefinition stackingDef)
            {
                EnsureStackingDebuffKey(stackingDef);
                RegisterProjectileTypesRecursive(stackingDef.ApplicatorDefinition);
                RegisterProjectileTypesRecursive(stackingDef.DetonationDefinition);
                return;
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
                if (p.ImpactAoeDefinition != null)
                    RegisterAoeTypeDefinition(p.ImpactAoeDefinition);
                if (p.StackTriggerSetup?.AoeDefinition != null)
                    RegisterAoeTypeDefinition(p.StackTriggerSetup.AoeDefinition);
                if (p.ImpactProjectileDefinition != null)
                    RegisterProjectileTypesRecursive(p.ImpactProjectileDefinition);
            }
        }

        private void RegisterAoeTypes()
        {
            if (combatRoot == null || compiledSlots == null) return;
            for (int i = 0; i < activeSlotCount; i++)
                RegisterAoeTypesRecursive(compiledSlots[i]);
        }

        private void RegisterAoeTypesRecursive(RuntimeSkillDefinition def)
        {
            if (def == null) return;

            if (def is RuntimeStackingSkillDefinition stackingDef)
            {
                EnsureStackingDebuffKey(stackingDef);
                RegisterAoeTypesRecursive(stackingDef.ApplicatorDefinition);
                RegisterAoeTypesRecursive(stackingDef.DetonationDefinition);
                return;
            }

            if (def is RuntimeAoeDefinition aoeDef)
            {
                RegisterAoeTypeDefinition(aoeDef);
                if (aoeDef.StackTriggerSetup?.AoeDefinition != null)
                    RegisterAoeTypeDefinition(aoeDef.StackTriggerSetup.AoeDefinition);
            }

            if (def is RuntimeProjectileDefinition projDef)
            {
                if (projDef.ChildSpawnSetup?.ChildDefinition != null)
                    RegisterAoeTypesRecursive(projDef.ChildSpawnSetup.ChildDefinition);
                if (projDef.ImpactAoeDefinition != null)
                    RegisterAoeTypeDefinition(projDef.ImpactAoeDefinition);
                if (projDef.StackTriggerSetup?.AoeDefinition != null)
                    RegisterAoeTypeDefinition(projDef.StackTriggerSetup.AoeDefinition);
                if (projDef.ImpactProjectileDefinition != null)
                    RegisterAoeTypesRecursive(projDef.ImpactProjectileDefinition);
            }
        }

        private static void EnsureStackingDebuffKey(RuntimeStackingSkillDefinition stackingDef)
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
}

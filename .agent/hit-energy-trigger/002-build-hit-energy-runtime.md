# 002 - Build Hit Energy Authoring, Runtime Edge, And Payload

## Goal

Replace legacy stacking authoring/runtime wrapper with clean HitEnergy concepts.

## Changes

1. Rename `StackTrigger.cs`/class to `HitEnergyTrigger` while preserving
   MonoScript `.meta` GUID:
   - private serialized `energyContributionMultiplier = 1f`
   - private serialized `energyRequirementMultiplier = 1f`
   - private serialized `retentionSeconds`
   - read-only `EnergyContributionMultiplier`,
     `EnergyRequirementMultiplier`, and `RetentionSeconds` accessors
   - same source/target tag eligibility
   - fresh CreateAssetMenu label
2. Replace `RuntimeStackingDetonation` with `RuntimeHitEnergyTrigger`:
   - plain composition object, not RuntimeSkillDefinition subclass
   - `RuntimeSkillDefinition TriggeredSkill`
   - `float EnergyContributionMultiplier`
   - `float EnergyRequirementMultiplier`
   - `float RetentionSeconds`
   - `int AccumulatorId = -1`
3. Rename outgoing properties on RuntimeProjectileDefinition,
   RuntimeAoeDefinition, and RuntimeTargetedDefinition to `HitEnergyTrigger`.
4. Compiler:
   - Build RuntimeHitEnergyTrigger on source runtime definition.
   - Compile adjacent target into TriggeredSkill.
   - Apply incoming mana-cost factor and projectile launch aim directly to
     TriggeredSkill; remove fake-wrapper special cases from mana traversal.
   - Recursive mana/cost/warning walks explicitly traverse
     `HitEnergyTrigger?.TriggeredSkill`.
5. Replace `StackEffectSnapshot`/`StackContribution` with `HitEnergyPayload`:
   - `AccumulatorId`
   - `EnergyPerHit`
   - `EnergyRequired`
   - `RetentionSeconds`
   - faction plus HitEnergySpawn
   - Enabled requires assigned id, positive finite energy values, positive
     retention, and valid HitEnergySpawn
6. Replace `StackDetonationKind` and `DetonationSnapshot` with
   `HitEnergySpawnKind` and `HitEnergySpawn`.
7. SkillDriver registration/template builder:
   - Rename id counter/assignment to Accumulator vocabulary.
   - Assign one AccumulatorId per RuntimeHitEnergyTrigger.
   - Traverse TriggeredSkill for type, sound, and template registration.
   - Register TriggeredSkill template before source payload.
   - Build EnergyPerHit = source.TriggerEnergy * contribution multiplier.
   - Build EnergyRequired = TriggeredSkill.TriggerEnergy * requirement
     multiplier.
   - Remove accumulated damage/area/projectile-count data.

## Acceptance Criteria

- Final production runtime contains no StackTrigger,
  RuntimeStackingDetonation, StackEffectSnapshot, StackContribution,
  StackDetonationKind, DetonationSnapshot, DebuffKey, or DebuffName symbols.
- RuntimeHitEnergyTrigger does not inherit RuntimeSkillDefinition.
- Multiplier changes are independent.
- Same Skill asset on several edges gets distinct AccumulatorId values.
- Projectile, AOE, lingering AOE, and targeted sources use one payload builder.
- Registered TriggeredSkill template is sole output-stat source.

## Dependencies

- Task 001.

## Scope / Complexity

High. Removes fake runtime-skill wrapper and updates recursive compiler,
registration, cost, warning, and template walks.

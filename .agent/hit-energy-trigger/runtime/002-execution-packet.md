# Task Execution Packet

## Task
002-build-hit-energy-runtime.md

## Goal
Replace legacy stacking authoring/runtime wrapper and fire-time snapshots with HitEnergy authoring, edge composition, spawn, payload, registration, and transport vocabulary.

## Files Allowed To Modify
- `Assets/Scripts/Skills/Trigger/StackTrigger.cs` plus its `.meta` during GUID-preserving rename
- `Assets/Scripts/Skills/Runtime/RuntimeStackingDetonation.cs` plus its `.meta` during rename
- `Assets/Scripts/System/Status/StackEffectSnapshot.cs` plus its `.meta` during rename
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeTargetedDefinition.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillLoadoutCompiler.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- Direct production consumers required to propagate renamed `HitEnergyPayload` through projectile, AOE, targeted, combat-root, hit-payload, and status boundaries without compile breaks
- Directly affected EditMode tests
- `.agent/hit-energy-trigger/implementation-log.md`

## Files Allowed To Create
- `Assets/Scripts/Skills/Trigger/HitEnergyTrigger.cs` reusing old MonoScript meta GUID
- `Assets/Scripts/Skills/Runtime/RuntimeHitEnergyTrigger.cs` reusing old runtime meta GUID
- `Assets/Scripts/System/Status/HitEnergyPayload.cs` reusing old snapshot meta GUID

## Files Allowed To Delete
- Old three filenames above after GUID-preserving replacement.

## Files Likely Needed For Reading
- All direct symbol references returned by search for legacy task-002 symbols.
- Spawn template registry/template builder code in `SkillDriver.cs`.
- Existing EditMode compiler, damage-scale, and projectile-authoring tests.

## Behavior To Preserve
- Trigger source/target tag eligibility.
- Adjacent recursive compilation and direct-root filtering.
- Incoming trigger mana factor and projectile launch aim.
- Recursive sound/type/template/warning walks.
- Registered template behavior and all projectile/AOE/targeted payload propagation.
- Task-003 target-local accumulator processing semantics until task 003 converts it.

## Behavior To Change
- Author two independent float multipliers and retention.
- Runtime edge becomes composition and points to `TriggeredSkill`.
- Fire-time payload holds effective float energy values plus `HitEnergySpawn` only.
- Edge id becomes `AccumulatorId`; remove output-stat accumulation data.
- Registered triggered template becomes sole output-stat source.

## Relevant Global Context
- `EnergyPerHit = source.TriggerEnergy * EnergyContributionMultiplier`.
- `EnergyRequired = TriggeredSkill.TriggerEnergy * EnergyRequirementMultiplier`.
- Use finite positive floor `1e-3f`; retention `<= 0` disables payload.
- Register `TriggeredSkill` template before building source payload.
- Runtime/ECS snapshots contain no managed authoring references.
- Keep interval energy/mana separate.

## Dependencies Confirmed
- `Skill.TriggerEnergy` exists and defaults to `1f`.
- `RuntimeSkillDefinition.TriggerEnergy` exists.
- `SkillSetCompiler` copies finite positive TriggerEnergy before outgoing edge recursion.

## Step-By-Step Instructions
1. Rename authoring class/file to `HitEnergyTrigger`, preserve MonoScript meta GUID, add private serialized multipliers/retention and read-only accessors.
2. Replace wrapper subtype with composition `RuntimeHitEnergyTrigger` and outgoing properties named `HitEnergyTrigger`.
3. Update compiler and recursive mana/warning traversals to walk `TriggeredSkill` explicitly; remove fake-wrapper cases.
4. Replace stack snapshot/kind/output types with `HitEnergyPayload`, `HitEnergySpawnKind`, and `HitEnergySpawn`; keep values unmanaged and validate finite positive energies.
5. Rename SkillDriver counter/assignment and recursive registration paths; assign unique edge-local `AccumulatorId`.
6. Register target template before building source payload; calculate effective energies from source/target runtime `TriggerEnergy` and independent multipliers.
7. Remove accumulated damage/area/projectile-count totals from payload/output data.
8. Update direct transport consumers and focused tests required by these names/data shapes. Do not redesign target buffer/activation system; task 003 owns that conversion.

## Acceptance Criteria
- Task-002 authoring/runtime/payload vocabulary uses HitEnergy names with no compatibility aliases.
- `RuntimeHitEnergyTrigger` does not inherit `RuntimeSkillDefinition`.
- Contribution and requirement multipliers remain independent.
- Each compiled edge receives distinct `AccumulatorId`.
- Projectile, AOE, lingering AOE, and targeted sources share one payload builder.
- Registered target template is sole output-stat source.
- Any legacy accumulator-system vocabulary intentionally remaining for task 003 is listed explicitly, not hidden.

## Validation Required
- User explicitly deferred validation until all tasks finish.
- Perform local static inspection and `git diff --check`, but do not require an
  intermediate compile-clean state between tasks 002 and 003.
- Do not run Unity tests. Final validation will name affected tests and require
  `Logs/TestResults-EditMode-HitEnergy.xml` and any required PlayMode XML.

## Hard Boundaries
- Do not modify files outside direct task-002 symbol consumers except imports/namespaces directly required by this task.
- Do not redesign or rename target-local buffer, activation processor, or managed progress presentation; task 003 owns them. It is permitted to leave their direct references temporarily uncompilable because user authorized validation after task 003 completes.
- Directly affected PlayMode test migration may remain for task 004; user authorized final-only validation.
- Do not change architecture or add compatibility aliases.
- Do not combine this task with later tasks.
- Stop on architectural ambiguity.

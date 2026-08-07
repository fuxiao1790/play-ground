# Task Execution Packet

## Task

003-spawn-lanes-expansion-apply.md

## Goal

Implement canonical targeted event -> expansion -> command -> pooled apply pipeline for single-hit and interval variants, exact mirror of impact/lingering AOE lanes.

## Files Allowed To Modify

- `Assets/Scripts/System/Targeted/TargetedSpawnExpansionSystem.cs` (new)
- `Assets/Scripts/System/Targeted/TargetedSpawnApplySystem.cs` (new)
- Existing/new directly related EditMode targeted spawn pipeline tests.
- Imports/namespaces directly required by the two systems.

## Files Allowed To Create

- Two Targeted system files and focused EditMode tests only.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `ImpactAoeSpawnExpansionSystem`, `LingeringAoeSpawnExpansionSystem`, their apply systems, `AoeExpansionCore`, `SpawnPoolTopUp`, targeted contracts/scope/template data, and existing AOE spawn tests.

## Behavior To Preserve

- Existing AOE/projectile spawn behavior and pool separation.
- Spawn expansion drains singleton queue and scope buffer; native lane ownership/handle flow follows current AOE shape.

## Behavior To Change

- Targeted/lingering targeted events materialize tagged targeted archetypes with no collision components.
- One event count produces one command/entity per fork, same position/no scatter, distinct deterministic IDs.

## Relevant Global Context

- Expansion owns template map dereference and multiplicity. Apply reuses disabled slots before cold create.
- Each lane singleton holds native queue/command list plus Producer/Pending handles and owns disposal.
- New entities do not resolve until next sim update. Increment `CombatStatsSingleton.EntitiesSpawned`.
- Archetypes require `TargetedVfxIds`, visual-only `TargetedVfxSizeComponent`, and `VfxTimingData`, never `AoeVfxIds` or `AoeAreaComponent`.
- Commands use `LifetimeSeconds` and nested `Resolve`; ID hash reads the stamped `JitterSeed` / `DeterministicIdTickIndex` fields from the command.

## Dependencies Confirmed

- Task 002 types present: Targeted tags/contracts/template map/scope buffers and child kinds.

## Step-By-Step Instructions

1. Mirror AOE expansion core/systems structurally, with two lane singletons. Drain both direct queues and scope buffers; include commands/pending handles/disposal.
2. Expand matching template events only, stamp instance values including acquire anchor, per-fork targeted ID/index, fresh chain state, optional interval timed-spawner restamp, and VFX from `TargetedVfxIds`/`TargetedVfxUtility`.
3. Mirror AOE apply/pool topology for single versus lingering targeted archetypes. Include no collision components/tags.
4. Use specified dead-slot queries; reuse disabled same-domain slots first; apply all command data and enable normal gates; increment spawn stats.
5. Add listed focused tests: variants/tags, count/no scatter/IDs, chain state, reuse/separation, mismatched kind/missing template, native cleanup coverage.

## Acceptance Criteria

- All seven listed scenario assertions in task file pass once Unity runs.
- No native ownership leaks and exact AOE pipeline shape.

## Validation Required

- Focused EditMode tests plus compile/build where available.
- Static check that event expansion uses no scatter, targeted VFX ids/timing, no resolve-system ordering attributes, and apply has no collision components.

## Hard Boundaries

- Do not implement target resolve, lifetime/arming changes, render/VFX resolve emission, root API, authoring/compiler/trigger support.
- Do not modify existing AOE pipeline except import compile fixes directly caused by these files.

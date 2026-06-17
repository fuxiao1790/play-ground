# Task 005: Add AoE spawn event/command types + AoeSpawnExpansionSystem + AoeSpawnApplySystem (beside old)

## Execution Role
This task is intended for a lower-cost implementation agent. Do not make architecture decisions. Follow this file and the linked context files. Additive + beside-old (no behavior change at the end).

## Goal
Mirror the projectile pipeline for AoE: add `AoeSpawnEvent` / `AoeSpawnCommandData`, the shared impact-AoE build helper, and the new `AoeSpawnExpansionSystem` + `AoeSpawnApplySystem`, built beside the live `AoeSpawnSystem`. Inputs are empty until Task 006, so behavior is unchanged.

## Required Reading
- `../context/002-target-architecture.md` (§1.3, §2 AoE pipeline)
- `../context/003-data-flow.md` (§4)
- `../context/005-decision-log.md` (D-NAMING, D-AOE-EXPANSION-MINIMAL, D-TRANSPORT, D-CONVERT-RELOCATE, D-LIFETIME-PULSE)

## Design Decisions Already Made
- `AoeSpawnEvent : IBufferElementData` (intent; no multiplicity today) and `AoeSpawnCommandData` (resolved single AoE; not a buffer element). Names per D-NAMING (managed `AoeSpawnCommand` keeps its name).
- AoE expansion is a 1:1 transform (resolve world bounds, carry spawn VFX). No scatter now (D-AOE-EXPANSION-MINIMAL).
- Apply buckets by `(faction, typeId)`; sets `CombatLifetimeComponent` (enabled iff `Lifetime>0`, disabled = pulse — Task 001).
- The impact-AoE build logic from `CombatSpawnConvertJob.AppendImpactAoe` relocates to `AoeSpawnPipeline.BuildImpactAoeEvent`, producing `AoeSpawnEvent`.

## Why This Task Exists
Design §5.7: AoE must follow the identical event→expansion→command→apply pipeline, not a shortcut path.

## Current Code References
```
Assets/Scripts/System/Aoe/AoeEcsComponents.cs
- AoeSpawnRequestElement (scope buffer; no multiplicity). KEEP for now (Task 006 removes).
Assets/Scripts/System/Aoe/AoeSpawnSystem.cs
- archetype; bucket by (faction,typeId); reuse IJobChunk (AoeSpawnJob); cold-create CreateAoeEntity;
  RecordAoeReset/LifetimeFor/HitGateFor/HitSpawnFor/AreaFor/PulseVfxFor/NeedsCollision; emits spawn VFX.
- Required outcome: port to AoeSpawnApplySystem with input AoeSpawnCommandData; apply Task 001 lifetime
  (set Remaining, enable iff Lifetime>0). Leave AoeSpawnSystem live this task (Task 006 deletes).
Assets/Scripts/System/Common/CombatSpawnConvertJob.cs (AppendImpactAoe)
- Builds AoeSpawnRequestElement from CombatPendingSpawn.ImpactAoe (geometry, render, ImpactAoeIdSalt).
- Required outcome: COPY into AoeSpawnPipeline.BuildImpactAoeEvent producing AoeSpawnEvent. Do not delete
  CombatSpawnConvertJob yet.
Assets/Scripts/System/Common/CombatEcsComponents.cs (CombatScopeOwner.Acquire)
- Required outcome: ADD AddBuffer<AoeSpawnEvent>.
```

## Files To Modify
- `System/Common/CombatEcsComponents.cs` — add `AddBuffer<AoeSpawnEvent>` in `CombatScopeOwner.Acquire`. Additive.

## Files To Create
- `System/Aoe/AoeSpawnPipeline.cs`:
  - `public struct AoeSpawnEvent : IBufferElementData` — same fields as `AoeSpawnRequestElement` (Faction, AoeId, TypeId, Lifetime, RepeatHitCooldownSeconds, HitPayload, AreaSize, Radius, RotationRadians, Position, HalfExtents, BoundsMin, BoundsMax, ShapeType, Render, ProjectileBurst). `ECS Lifecycle:` comment as for the projectile event.
  - `public struct AoeSpawnCommandData` — same fields, resolved (bounds computed). Comment as for projectile command.
  - `internal static class AoeSpawnPipeline.BuildImpactAoeEvent(in CombatPendingSpawn pending)` → `AoeSpawnEvent` (port `AppendImpactAoe`: geometry, `ComputeWorldBounds`, render with `CombatRoot.AoeRenderZ`, `ImpactAoeIdSalt` via the same `HashId`). Keep Burst-safe (use `Unity.Mathematics`, not `Mathf`).
- `System/Aoe/AoeSpawnExpansionSystem.cs` — `SystemBase`, owns `internal NativeQueue<AoeSpawnEvent> EventQueue` (Persistent) + `internal NativeStream PendingCommands; internal JobHandle PendingHandle;`. Order: after both collision systems, before `AoeSpawnApplySystem`. OnUpdate: complete; drain `EventQueue` + scope `AoeSpawnEvent` buffer → `NativeArray`; clear both; resolve each into `AoeSpawnCommandData` (1:1, recompute world bounds) into `PendingCommands`; ALSO append the spawn-time VFX request (`Trigger=0`) to the scope `VfxSpawnRequestElement` buffer for each (as `AoeSpawnSystem` does today).
- `System/Aoe/AoeSpawnApplySystem.cs` — `SystemBase`, `[UpdateAfter(typeof(AoeSpawnExpansionSystem))]`. Port `AoeSpawnSystem` (archetype, `AoeSpawnKey` bucketing, `AoeSpawnJob` reuse, cold-create, reset helpers, `NeedsCollision`), input `AoeSpawnCommandData`. Set `CombatLifetimeComponent.Remaining` and enable iff `Lifetime>0` (pulse → disabled, per Task 001). Keep `AoePulseVfxComponent` setup.

## Files To Delete
None this task.

## Required Changes
1. Create the two structs + `BuildImpactAoeEvent` helper (verbatim geometry/render/id math).
2. Add the `AoeSpawnEvent` scope buffer.
3. Create `AoeSpawnExpansionSystem` (drain both → command, resolve bounds, emit spawn VFX) and `AoeSpawnApplySystem` (port of `AoeSpawnSystem`, input changed, lifetime set per Task 001).
4. Keep `AoeSpawnSystem` live (it still drains the OLD `AoeSpawnRequestElement` buffer and emits VFX). Ensure the new systems' inputs are empty (no producer writes `AoeSpawnEvent` yet) so no duplicate spawns.
5. Recompile; run.

## Behavior Preservation Requirements
- No behavior change: new systems inert (empty inputs); old `AoeSpawnSystem` still spawns all AoEs.

## Intentional Behavior Changes
None. This task should be behavior-preserving.

## Out of Scope
- Switching producers to `AoeSpawnEvent` and deleting `AoeSpawnSystem` (Task 006).

## Dependencies
- Task 001 (lifetime), Task 002 (projectile pipeline exists, shared helpers/patterns), Task 003 (expansion/apply pattern to mirror).

## Follow-Up Tasks
- Task 006 (AoE cutover + delete old), Task 007 (delete dead convert job).

## Implementation Constraints
- Same as Task 003 (no structural change in jobs; Persistent `EventQueue` disposed in `OnDestroy`; `PendingCommands` TempJob disposed by apply each frame).
- Keep AoE Profiler markers/counters (`Aoe.Spawn`, `.ReuseJob`, Cold/Reuse) on the new apply system.
- Spawn-time VFX (`Trigger=0`) must be emitted exactly once per spawned AoE (today `AoeSpawnSystem` emits it while draining); emit it in expansion when building each command so it fires for every AoE regardless of reuse/cold.

## Step-by-Step Implementation Plan
```
1. Create AoeSpawnPipeline.cs: AoeSpawnEvent (IBufferElementData), AoeSpawnCommandData,
   BuildImpactAoeEvent (ported AppendImpactAoe).
2. Add AddBuffer<AoeSpawnEvent> in CombatScopeOwner.Acquire.
3. Create AoeSpawnExpansionSystem (drain both → AoeSpawnCommandData; resolve bounds; emit Trigger=0 VFX).
4. Create AoeSpawnApplySystem (port AoeSpawnSystem; input AoeSpawnCommandData; set CombatLifetimeComponent).
5. Ordering attributes (expansion after collisions, apply after expansion).
6. Recompile; run full suite (green; new path inert).
```

## Acceptance Criteria
```
- [ ] AoeSpawnEvent (IBufferElementData) and AoeSpawnCommandData exist with the right fields.
- [ ] AoeSpawnPipeline.BuildImpactAoeEvent mirrors AppendImpactAoe (same salt/geometry/render-Z).
- [ ] AoeSpawnExpansionSystem owns EventQueue, produces AoeSpawnCommandData (bounds resolved), and
      emits spawn-time VFX (Trigger=0) per AoE.
- [ ] AoeSpawnApplySystem reuses/cold-creates from AoeSpawnCommandData and sets CombatLifetimeComponent
      (enabled iff Lifetime>0).
- [ ] Old AoeSpawnSystem still live; behavior unchanged; existing tests pass.
- [ ] Repo compiles; no leaks.
```

## Validation
- Compile + full test suite (green; new path inert).
- Optional focused test: enqueue an `AoeSpawnEvent` into `AoeSpawnExpansionSystem.EventQueue` in a bare world, `Tick`, assert one active AoE + one VFX request.

## Risk Level
Medium — AoE apply port must match original; lifetime enable/disable mapping for pulse must be correct.

## Failure Modes
- **Pulse AoE always lingering / always one-shot:** lifetime enabled-state wrong in apply. Detect via ported `PulseHitsOverlappingTargetOnce` once Task 006 connects it.
- **Missing spawn VFX:** Trigger=0 not emitted in expansion.
- **Duplicate AoEs:** a producer already feeds `AoeSpawnEvent` (it shouldn't until Task 006).

## Rollback Strategy
Delete the new pipeline/systems + the added buffer; `AoeSpawnSystem` is untouched.

## Notes for Future Tasks
- Task 006 retargets `CombatRoot.Spawn(AoeSpawnCommand)` and collision impact-AoE to `AoeSpawnEvent`, then deletes `AoeSpawnSystem` + `AoeSpawnRequestElement`.

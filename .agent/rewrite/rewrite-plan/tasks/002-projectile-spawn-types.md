# Task 002: Add projectile spawn event/command types + transport scaffolding (additive)

## Execution Role
This task is intended for a lower-cost implementation agent. Do not make architecture decisions. Follow this file and the linked context files. This task is **purely additive** — nothing in the live pipeline changes behavior.

## Goal
Introduce the new projectile pipeline data types (`ProjectileSpawnEvent`, `ProjectileSpawnCommandData`) and the shared build helpers, plus the scope managed-submission buffer, without wiring them into the active pipeline yet. The repo compiles and behaves identically after this task.

## Required Reading
- `../context/002-target-architecture.md` (§1.1, §1.2, §3)
- `../context/003-data-flow.md` (§1–§3)
- `../context/005-decision-log.md` (D-NAMING, D-EXPANSION-OWNS-MATH, D-TRANSPORT, D-CONVERT-RELOCATE)

## Design Decisions Already Made
- `ProjectileSpawnEvent` = intent + multiplicity + resolved template; implements `IBufferElementData` (so it works as both a `NativeQueue<T>` element and a scope `DynamicBuffer<T>` element). Field list is fixed in context 002 §1.1.
- `ProjectileSpawnCommandData` = exactly one entity; **no** `Count`/`SpreadDegrees`/`JitterDegrees`/`JitterSeed`/`BaseDirection`/`Speed`. Field list fixed in context 002 §1.2. NOT a buffer element.
- Names are `ProjectileSpawnEvent` / `ProjectileSpawnCommandData` (the managed `ProjectileSpawnCommand` keeps its name — D-NAMING).
- The impact-projectile/burst build logic from `CombatSpawnConvertJob` is relocated into static helpers on `ProjectileSpawnPipeline`, producing `ProjectileSpawnEvent` (used by Task 004).

## Why This Task Exists
Design R3/R4/§5.4: split the dual-use `ProjectileSpawnRequestElement` into a true event and a true command. Doing the type + helper additions first keeps the later cutover (Task 004) a focused swap rather than a big-bang rewrite.

## Current Code References
```
Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs
- ProjectileSpawnRequestElement (dual-use; lines ~110-147)
- Current: intent+command+buffer in one type. KEEP it for now (still used by the live pipeline).

Assets/Scripts/System/Common/CombatSpawnConvertJob.cs
- BuildProjectileRequest, AppendImpactProjectiles, AppendProjectileBurst, TrackingFor,
  ProjectileRender, DirectionFromTo, HashId (salts ImpactProjectileIdSalt, ProjectileBurstIdSalt)
- Current: builds ProjectileSpawnRequestElement from CombatPendingSpawn.
- Required outcome (this task): COPY this logic into ProjectileSpawnPipeline.BuildImpactProjectileEvent /
  BuildBurstEvent producing ProjectileSpawnEvent. Do NOT delete CombatSpawnConvertJob yet (Task 007).

Assets/Scripts/System/Common/CombatEcsComponents.cs (CombatScopeOwner.Acquire)
- Adds scope buffers. Required outcome: ALSO add buffer ProjectileSpawnEvent (additive).
```

## Files To Modify
- `System/Common/CombatEcsComponents.cs` — in `CombatScopeOwner.Acquire`, ADD `entityManager.AddBuffer<ProjectileSpawnEvent>(ownedScope);`. Additive (the old `ProjectileSpawnRequestElement` buffer stays for now).

## Files To Create
- `System/Projectile/ProjectileSpawnPipeline.cs` (namespace `PlayGround.System.Projectile`):
  - `public struct ProjectileSpawnEvent : IBufferElementData` — fields per context 002 §1.1. Add an `ECS Lifecycle:` comment: "transient spawn intent; enqueued by producers into the expansion queue or appended to the scope submission buffer; consumed and discarded by ProjectileSpawnExpansionSystem."
  - `public struct ProjectileSpawnCommandData` — fields per context 002 §1.2. Comment: "resolved single-entity allocation intent; produced by expansion, consumed by apply; never carries multiplicity."
  - `internal static class ProjectileSpawnPipeline` with static helpers (Burst-compatible, pure):
    - `BuildImpactProjectileEvent(in CombatPendingSpawn pending)` → `ProjectileSpawnEvent` (port `AppendImpactProjectiles` + `BuildProjectileRequest` with `ImpactProjectileIdSalt`, fire-back-toward-source direction `DirectionFromTo(pos,targetPos,invert:true)`, `seedContactGateTargetId = pending.TargetId`).
    - `BuildBurstEvent(in CombatPendingSpawn pending)` → `ProjectileSpawnEvent` (port `AppendProjectileBurst`, `ProjectileBurstIdSalt`, fire-toward-target `invert:false`, `seedContactGateTargetId = 0`).
    - Keep `DirectionFromTo`, `HashId`, `TrackingFor`, `ProjectileRender` as private statics here (copied from `CombatSpawnConvertJob`).
  - NOTE: in this task these helpers may temporarily take `CombatPendingSpawn` as input (the existing transient). Task 004 calls them from collision; Task 007 removes `CombatPendingSpawn`. If you prefer to avoid the temporary `CombatPendingSpawn` dependency, give the helpers a primitive parameter list instead (position, targetPosition, snapshot, faction, sourceId, typeId, targetId). Either is acceptable — keep it Burst-safe.

## Files To Delete
None.

## Required Changes
1. Create `ProjectileSpawnPipeline.cs` with the two structs and the helper class. Keep all field names/units identical to the matching fields on `ProjectileSpawnRequestElement` so Task 004's copy is mechanical.
2. Port the convert helpers VERBATIM (same salts, same direction invert rules, same render-Z formula `CombatRoot.ProjectileRenderZ - (id % ProjectileRenderZSlots) * ProjectileRenderZStep`).
3. Add the `ProjectileSpawnEvent` scope buffer in `CombatScopeOwner.Acquire`.
4. Recompile. No system reads the new types yet, so behavior is unchanged.

## Behavior Preservation Requirements
- Entire runtime behavior unchanged (additive task).

## Intentional Behavior Changes
None. This task should be behavior-preserving.

## Out of Scope
- Wiring producers/expansion/apply to the new types (Tasks 003/004).
- Deleting `ProjectileSpawnRequestElement`, `CombatSpawnConvertJob`, `CombatPendingSpawn` (Tasks 004/007).

## Dependencies
- Task 001 (so `ProjectileSpawnCommandData`/event reference the post-001 lifetime via `CombatLifetimeComponent` semantics — though the command stores raw `Lifetime`, not the component; mainly ordering to avoid archetype churn conflicts).

## Follow-Up Tasks
- Task 003 (expansion + apply systems consume these types), Task 004 (producers emit them).

## Implementation Constraints
- Burst: both structs are blittable (only primitives + existing blittable snapshot/component structs). The helper statics must be Burst-callable (no managed types, no `Mathf` — use `Unity.Mathematics`).
- A struct implementing `IBufferElementData` may still be a `NativeQueue<T>` element — keep `ProjectileSpawnEvent` free of managed fields.
- Naming/folders: one new file under `System/Projectile/`.

## Step-by-Step Implementation Plan
```
1. Create System/Projectile/ProjectileSpawnPipeline.cs with ProjectileSpawnEvent (IBufferElementData)
   and ProjectileSpawnCommandData (plain) per context 002 §1.1/§1.2.
2. Add static ProjectileSpawnPipeline.BuildImpactProjectileEvent / BuildBurstEvent + private
   DirectionFromTo/HashId/TrackingFor/ProjectileRender, copied from CombatSpawnConvertJob.
3. Add AddBuffer<ProjectileSpawnEvent> in CombatScopeOwner.Acquire.
4. Recompile.
```

## Acceptance Criteria
```
- [ ] ProjectileSpawnEvent exists, implements IBufferElementData, has all context-002 §1.1 fields.
- [ ] ProjectileSpawnCommandData exists with NO Count/Spread/Jitter/BaseDirection/Speed fields.
- [ ] ProjectileSpawnPipeline.BuildImpactProjectileEvent / BuildBurstEvent compile and mirror the
      old convert logic (same salts/direction/render-Z).
- [ ] Scope has a ProjectileSpawnEvent buffer.
- [ ] Repo compiles; no behavior change (old pipeline still live).
```

## Validation
- Compile only. Run the full existing test suite to confirm no regression (additive task → all green).

## Risk Level
Low — additive types + a buffer; no live code path changes.

## Failure Modes
- **Burst error on the new structs:** a non-blittable field slipped in — remove it.
- **Helper drift:** a salt or direction-invert differs from `CombatSpawnConvertJob` → impact spawns will mis-aim in Task 004. Diff the helpers against the original.

## Rollback Strategy
Delete `ProjectileSpawnPipeline.cs` and the added buffer line; no other code references them yet.

## Notes for Future Tasks
- Task 004 will replace `CombatSpawnConvertJob`'s projectile output with calls to these helpers + enqueue.

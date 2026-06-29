# 005 — AoeSpawnExpansionSystem trigger-0 spawn VFX

**File:** `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`
**Depends on:** 001
**Scope:** small/medium

## Current behavior

This system is the one producer that does **not** use a flush job: in `OnUpdate`
(main thread) it resolves `VfxSingleton`, gets the
`DynamicBuffer<VfxSpawnRequestElement>`, and appends a trigger-0
`VfxSpawnRequestElement` per spawned AOE (lines ~100–134), counting/`EnsureCapacity`
first. This is the source of the doc's stale "trigger 0 not emitted" claim — it
*is* emitted here.

## Change

Move the trigger-0 emission into the existing burst `AoeExpansionJob`, which
already iterates `Events` × `Templates.Map`:

- Add `public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;` and
  `public bool HasVfxWriter;` to `AoeExpansionJob`.
- For each spawned AOE child (the same `count = max(1, template.Count)` loop the
  main-thread block used), `if (HasVfxWriter) VfxPending.Enqueue(new VfxPendingSpawn
  { TypeId = template.TypeId, Trigger = 0, Position = e.Position, AreaSize =
  template.AreaSize });`.
- Delete the main-thread `GetSingletonEntity<VfxSingleton>()`, the
  `vfxBuffer`/`vfxCount`/`EnsureCapacity` block, and the trigger-0 append loop.
- Wire the writer + combine into `ProducerHandle`:
  ```csharp
  var vfx = World.GetExistingSystemManaged<CombatVfxDispatchSystem>();
  // job init: VfxPending = vfx != null ? vfx.AsParallelWriter() : default,
  //           HasVfxWriter = vfx != null && vfx.HasQueue,
  // after Schedule:
  if (vfx != null)
      vfx.ProducerHandle = JobHandle.CombineDependencies(vfx.ProducerHandle, Dependency);
  ```
  (`Dependency` here is the `AoeExpansionJob` handle this system already tracks.)

## Notes

- `AoeExpansionJob` already receives `Templates = templates.Map` and `Events`, so
  it has everything needed to emit trigger-0 with no extra inputs.
- This removes the only main-thread VFX buffer write and the implicit sync it
  forced; emission now rides the burst job and the standard `ProducerHandle`.

## Acceptance criteria

- No `VfxSingleton`/`VfxSpawnRequestElement`/buffer reference remains in this file.
- Expansion-spawned AOEs still emit one trigger-0 `VfxPendingSpawn` each, now from
  the job into the shared queue.
- The job handle is combined into `CombatVfxDispatchSystem.ProducerHandle`.

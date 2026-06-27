# Render Rework — Intent

## Goal

Collapse the two-system pipeline (`CombatRenderPrepareSystem` + `CombatBatchedRenderSystem`)
into a single system. Burst jobs gather and bucket matrices by sprite kind. The managed
`SystemBase` only submits pre-bucketed data to the GPU — no filtering, no copying, no
per-group ToComponentDataArray calls.

## Prerequisite

**Blocked on [decouple-render-id-from-faction](../decouple-render-id-from-faction/intent.md).**
That task replaces `(CombatRenderFaction, CombatRenderTypeId)` with a single
`CombatRenderBatchId` shared component and moves layer + bounds into a global resource
registry keyed by `batchId`. Without it, the render system still has to loop per-faction
and carry root references, which works against this design. That task lists this rework
as its downstream.

After that task, ECS chunks are already partitioned by `CombatRenderBatchId` (shared
component). The main thread knows all registered batch ids from the global registry at
frame start. This makes bucketing trivially implicit in chunk topology.

## Design

### Data flow

```
CombatBatchedRenderSystem.OnUpdate (SystemBase, PresentationSystemGroup)
  │
  ├── for each registered batchId:
  │     NativeList<Matrix4x4>.Clear()               [main thread, O(1)]
  │     schedule IJobChunk filtered to that batchId [Burst, ScheduleParallel]
  │       reads: CombatKinematicsComponent, CombatRenderComponent
  │       writes: NativeList<Matrix4x4>.AsParallelWriter()
  │
  ├── Dependency.Complete()                          [sync]
  │
  └── for each registered batchId:
        SubmitBatches(nativeList, resources, layer, bounds)  [managed GPU call]
```

### Bucketing without NativeParallelMultiHashMap

`CombatRenderBatchId` is an `ISharedComponentData`, so ECS puts all entities sharing
the same batchId into the same chunks. Scheduling one `IJobChunk` per batchId with a
query filtered to that id means each job only sees its own batchId's chunks — no
cross-bucket writes, no hash map.

Each batchId owns a `NativeList<Matrix4x4>` allocated once (persistent) and cleared
each frame. Jobs append via `AsParallelWriter()`. Main thread reads the fully-populated
list after `Complete()`.

### Matrix computation

Matrix is computed on-the-fly in the gather job using `CombatRenderMatrixUtility.ElementFor`.
`CombatRenderElement` (the intermediate per-entity component) is removed — no separate
prep write needed.

### What the managed loop does

```csharp
foreach (var (batchId, list) in buckets)
{
    var res = registry[batchId];
    SubmitBatches(list.AsArray(), res.Resources, res.Layer, res.BoundsHalfExtent);
}
```

No faction loop, no shared filter per group, no ToComponentDataArray allocation.

## What changes

| Item | Action |
|---|---|
| `CombatRenderPrepareSystem` | Deleted |
| `CombatRenderElement` | Deleted — matrix computed in gather job |
| `CombatBatchedRenderSystem` | Rewritten: schedules per-batchId Burst jobs, submits in managed pass |
| Per-batchId `NativeList<Matrix4x4>` | New: persistent, owned by the system, cleared each frame |
| Projectile/AOE split queries | Deleted — single query per batchId using `CombatRenderActiveTag` |
| `CombatRenderComponent` | Kept — still carries IsRenderable, AlignToVelocity, VisualScale, RenderZ |
| `CombatRenderFaction` / `CombatRenderTypeId` | Gone (prerequisite task) |
| `CombatRenderBatchId` | The single shared component from prerequisite task |

## Open questions

- **Buffer sizing**: NativeList starts at a reasonable initial capacity (e.g. pool size
  hint from the registry) and grows on demand. Never shrunk during a session.
- **Frame with zero entities for a batchId**: job completes immediately, empty list,
  SubmitBatches is skipped. No GPU call.
- **Domain (projectile vs AOE)**: after the decouple task the batchId is globally unique
  per visual type, so no domain split is needed in the render loop. If domain queries
  must be kept for other reasons, revisit.

## Acceptance

- One system in PresentationSystemGroup owns all render work.
- Burst jobs populate per-batchId `NativeList<Matrix4x4>` without NativeParallelMultiHashMap.
- Managed pass only calls `SubmitBatches` — no ECS queries, no shared filter, no
  `ToComponentDataArray`.
- `CombatRenderPrepareSystem` and `CombatRenderElement` do not exist.
- Visual output is identical to before the rework.

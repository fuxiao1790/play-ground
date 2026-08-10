# 005 — SpawnTemplateRefCountSystem

**Scope:** medium. **Depends on:** 001, 003, 004.

The only place counters are read, applied, or erased. New file
`Assets/Scripts/System/Spawning/SpawnTemplateRefCountSystem.cs`.

```csharp
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateAfter(typeof(CombatPoolCleanupSystem))]
public sealed partial class SpawnTemplateRefCountSystem : SystemBase
```

`SystemBase`, single-threaded, main thread. Not a job.

## Update body

This system consumes one queue. It has no knowledge of spawning or despawning beyond
the deltas it drains — both halves arrive as events from 003 and 004.

1. `CompleteDependency()` — nothing may hold the template maps or the delta queue.
2. Read `SpawnTemplateRegistryState` and the three template components directly from
   the scope singleton; throw if missing (memory `fail-loud-singletons`).
3. **Drain** — `while (state.Deltas.TryDequeue(out SpawnTemplateRefDelta d))`, resolve
   `d.Kind` to a counter map, `entry.InstanceCount += d.Delta`, write back. Create the
   entry on demand if absent so a delta for an already-erased key is well-defined
   rather than a throw. Clamp `InstanceCount` at 0 and assert in the editor if a clamp
   fires — a negative count means a release arrived without a matching acquire.
4. **Erase** — walk each counter map and remove every entry where `Reclaimable`
   (`!Pinned && OwnerCount <= 0 && InstanceCount <= 0`) from both the counter map and
   its template map.

Erase in a second pass over a `GetKeyArray(Allocator.Temp)` snapshot; do not remove
while enumerating.

## Placement rationale

`LateSimulationSystemGroup` runs last inside `SimulationSystemGroup`, so every
expansion, collision, timed-spawn, apply and lifetime job for this tick has already
run and completed, and every delta for the tick is in the queue. The next write to the
registries is managed code in the following frame's `Update()`, which runs before
`SimulationSystemGroup` (`CombatEcsWorld.cs:32`). The doc's "immutable for the entire
simulation tick" guarantee therefore still holds literally.

## Cost

Work is proportional to spawns plus despawns, not to entity count: one queue drain and
an erase pass. Skip the erase pass when nothing changed — a dirty flag inside
`SpawnTemplateRegistryState`, set by the drain and by
`CombatRoot.UnregisterSpawnTemplate`. An idle frame costs one empty-queue check.

## Acceptance criteria

- A template with no owners and no live entities is gone from both the template map
  and the counter map by the end of the frame in which the last claim dropped.
- A template with `OwnerCount > 0` is never erased regardless of `InstanceCount`.
- A template with `InstanceCount > 0` is never erased regardless of `OwnerCount`.
- A `Pinned` template is never erased.
- A spawn/despawn pair for the same entity nets zero.
- With no spawns, no despawns and no unregisters, the system does no map work.
- No sim job reads or writes a counter map.

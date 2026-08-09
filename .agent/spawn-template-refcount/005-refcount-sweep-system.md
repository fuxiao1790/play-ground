# 005 — SpawnTemplateRefCountSystem

**Scope:** medium. **Depends on:** 001, 003, 004.

The single point where counters are applied and entries are erased. New file
`Assets/Scripts/System/Spawning/SpawnTemplateRefCountSystem.cs`.

```csharp
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateAfter(typeof(CombatPoolCleanupSystem))]
public sealed partial class SpawnTemplateRefCountSystem : SystemBase
```

`SystemBase`, single-threaded, main thread. Not a job.

## Update body

1. `CompleteDependency()` — nothing may hold the template maps or the delta queue.
2. Read `SpawnTemplateRegistryState` and the three template components directly from
   the scope singleton; throw if missing (memory `fail-loud-singletons`).
3. **Drain** — `while (state.Deltas.TryDequeue(out SpawnTemplateRefDelta d))`, resolve
   `d.Kind` to a counter map, `entry.InstanceCount += d.Delta`, write back. Create the
   entry on demand if absent so a delta for an already-erased key does not throw.
   Clamp `InstanceCount` at 0 on the low side and assert in the editor if a clamp
   fires — a negative count means 003 and 004 disagree about a key set.
4. **Erase** — walk each counter map and remove every entry where `Reclaimable`
   (`!Pinned && OwnerCount <= 0 && InstanceCount <= 0`) from both the counter map and
   its template map.

Erase in a second pass over a `GetKeyArray(Allocator.Temp)` snapshot; do not remove
while enumerating.

## Placement rationale

`LateSimulationSystemGroup` runs last inside `SimulationSystemGroup`, so every
expansion, collision, timed-spawn and apply job for this tick has already run and
completed. `CombatPoolCleanupSystem` is in the same group, so ordering after it means
this frame's destroys are accounted for this frame. The next write to the registries
is managed code in the following frame's `Update()`, which runs before
`SimulationSystemGroup` (`CombatEcsWorld.cs:32`). The doc's "immutable for the entire
simulation tick" guarantee therefore still holds literally.

## Cost

Zero-cost when nothing changed: an empty queue and a `Reclaimable` scan that only runs
when the drain actually applied a delta or a managed unregister happened this frame.
Track a dirty flag set by the drain; skip the erase pass when clean. `CombatRoot`
cannot set the flag directly (different assembly boundary is not an issue, but the
component is a value type) — store the flag inside `SpawnTemplateRegistryState` and
have `CombatRoot.UnregisterSpawnTemplate` set it.

## Acceptance criteria

- A template with no owners and no instances is gone from both the template map and
  the counter map by the end of the frame in which the last claim dropped.
- A template with `OwnerCount > 0` is never erased regardless of `InstanceCount`.
- A template with `InstanceCount > 0` is never erased regardless of `OwnerCount`.
- A `Pinned` template is never erased.
- With no registrations, no despawns and an empty queue, the system does no map work.
- No sim job reads or writes a counter map.

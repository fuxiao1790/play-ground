# 004 — `ExternalSpawnGateSystem` → Burst job

## Why

`ExternalSpawnGateSystem.OnUpdate` is entirely managed and does three kinds of
real work per request (`Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs:90-247`):

1. **Mana debit** — `TrySpendMana` (`:125-144`) does `Exists` + `HasComponent` +
   `GetComponentData<Mana>` + `SetComponentData` per request.
2. **Lane routing** — `AppendInternalSpawn` (`:146-247`) does a managed
   `EntityManager.GetBuffer<T>(scope).Add(...)` per request across four event
   types.
3. **Target acquisition** — for `IntervalChildKind.Targeted` only, it calls
   `hash.BuildHandle.Complete()` and then runs a full nearest-hostile spatial
   search, **inside the request loop** (`:159-178`).

Item 3 is the reason this task ranks above the other target-bound work: it is a
sync point *inside a loop*, sitting mid-`SimulationSystemGroup` between
`TargetSpatialHashSystem` and the expansion systems. Every Targeted request in a
frame pays it again.

The system has **no managed-object anchor at all** — no `TargetCompanion`, no
`GetComponentObject`, no Unity object. `TargetedAcquisition` is already
`[BurstCompile]` (`TargetedAcquisition.cs:13`) and already takes a `Snapshot` of
native arrays (`:16-37`). Everything here can move.

## Scope

- `Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs`

## Change

### Replace `BuildHandle.Complete()` with the existing consumer-handle pattern

This is the substantive change; the rest is mechanical translation.

`ImpactAoeCollisionSystem.cs:103-104` already establishes how a system reads the
spatial hash without completing it: depend on `BuildHandle`, then register the
read back into `ConsumerHandle`. Adopt that here rather than completing:

```csharp
var hashRw = SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>();
JobHandle input = JobHandle.CombineDependencies(Dependency, hashRw.ValueRO.BuildHandle);

JobHandle gateHandle = new ExternalSpawnGateJob { ... }.Schedule(input);

hashRw.ValueRW.ConsumerHandle =
    JobHandle.CombineDependencies(hashRw.ValueRO.ConsumerHandle, gateHandle);
```

`TargetSpatialHashSystem.OnUpdate` completes `ConsumerHandle` at the top of the
next frame (`TargetSpatialHashSystem.cs:91-94`) before clearing and rebuilding,
so this is exactly the contract the existing consumers rely on. **Reused
mechanism, no new handle field** — required by `Docs/coding-standards.md`
§*System Encapsulation*.

### Translate the managed accesses

| Managed today | In the job |
|---|---|
| `EntityManager.GetBuffer<ExternalSpawnRequest>(scope)` | `BufferTypeHandle<ExternalSpawnRequest>` over the scope chunk array |
| `EntityManager.GetBuffer<ProjectileSpawnEvent>(scope).Add(…)` (and 3 more) | `BufferTypeHandle<T>` per lane, same scope chunks |
| `Exists` / `HasComponent<Mana>` / `Get`/`SetComponentData<Mana>` | `ComponentLookup<Mana>` — `HasComponent` + indexer |
| `lane.Events.Add(rejection)` | `NativeList<SpawnRejectedEvent>` field, written directly (single-threaded `IJob`) |
| `SystemAPI.TryGetSingleton(out TargetedSpawnTemplate)` | pass `templates.Map` (`NativeHashMap`) as a `[ReadOnly]` field |
| `hash.TargetEntities.AsArray()` etc. | pass the four `.AsArray()` values + `AoeOccupiedCells` as `[ReadOnly]` fields, built into a `Snapshot` inside `Execute` |

`Entity.Null` checks and the `TargetProxy` existence test survive as
`ComponentLookup.HasComponent`, which is the job-side equivalent and is cheaper
than `EntityManager.Exists`.

### The `throw` at the end must go

`ExternalSpawnGateSystem.cs:245-246` throws
`InvalidOperationException($"Unhandled interval child kind {request.Kind}.")`.
Burst does not support managed exceptions with interpolated strings. Replace
with either:

- a Burst-safe `throw new InvalidOperationException()` with no message (allowed
  in Burst only in Debug builds and stripped in Release), or
- **preferred:** count unhandled kinds into a field and let the system surface
  it, keeping the fail-loud intent without the managed string.

Do not silently drop the case. The existing `throw` encodes a real invariant —
an unrouted spawn kind is a broken world — and matches the fail-loud singleton
convention this codebase uses elsewhere ([[fail-loud-singletons]]).

### Scheduling: `Schedule` or `Run`

`Schedule` is specified above because the handle story is already solved by the
`ConsumerHandle` pattern this system's neighbours use. If the `BufferTypeHandle`
write aliasing across five different buffer types on the same scope entity turns
out to be awkward under the job safety system, **`.Run()` is an acceptable
fallback** per the plan's premise — it keeps the Burst compilation and the
removal of the per-request `TargetedAcquisition` managed call, and only gives up
the overlap. Note that `.Run()` would *not* remove the effective sync point on
the hash, so prefer `Schedule`.

## Acceptance Criteria

- `OnUpdate` contains no per-request loop; only query/handle setup, job
  construction, and handle publication.
- `hash.BuildHandle.Complete()` no longer appears anywhere in this file.
- The system registers into `TargetSpatialHashSingleton.ConsumerHandle` and does
  not introduce a new handle field on any singleton.
- `SpawnRejectedSingleton.ProducerHandle` is published with the job handle so
  `SpawnRejectionBridge` (which completes it, `SpawnRejectionBridge.cs:19`) stays
  correct.
- Mana debit semantics are byte-identical, including the two edge cases that are
  easy to lose in translation: a caster with **no `Mana` component** succeeds
  (`:127-131` returns `true`), and cost is clamped with `math.max(0f, …)`.
- Targeted acquisition still falls back to `request.Position` when the hash,
  template, or nearest-hostile lookup fails, and still sets
  `HasAcquiredTarget = 0` in that case — `TargetedSpawnApplySystem.cs:192` uses
  that flag to decide initial arming, so a wrong default is visible in-game as
  chains rendering before they acquire.
- The unhandled-kind case still fails loudly by some mechanism.

## Dependencies

None. Independent of 002 even though both touch spawn events — 002 changes how
the buffers are *drained*, this changes how they are *filled*. If both land,
re-verify that the gate's writes and the gather's `buffer.Clear()` still bracket
correctly within one frame: the gate runs `[UpdateBefore]` all four expansion
systems (`:52-55`), so gate-writes-then-gather-clears holds.

## Scope/Complexity

Medium-large. One file, but the job needs ~12 fields and the routing switch has
four branches plus the acquisition sub-path.

## Test Coverage

- `Assets/Tests/EditMode/TargetedRoutingEditModeTests.cs:78-131` — seeds
  `ExternalSpawnRequest` and asserts the resulting `TargetedSpawnEvent` buffer.
  Directly covers routing and the mana-less-caster case.
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs:176-205` — mana
  debit and rejection; asserts exact `ProjectileSpawnEvent` buffer lengths after
  the gate.

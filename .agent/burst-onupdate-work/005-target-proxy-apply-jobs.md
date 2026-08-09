# 005 — Target proxy update → Burst job; delete → batch destroy

## Why

`TargetProxyUpdateApplySystem` runs every frame and its entire `OnUpdate` is
managed (`Assets/Scripts/System/Targets/TargetProxyUpdateApplySystem.cs:22-78`):
`Dependency.Complete()`, then per event `EntityManager.Exists` +
`GetComponentData` + `SetComponentData` against `Health`/`Mana`/`TargetPosition`/
`TargetCollisionShape`.

Volume is target-count-bound — `Docs/performance.md` caps player + mobs at
roughly 50, and actor roots push one position update each per frame
(`Docs/architecture/phase-order.md` step 3). So ~50 events/frame. **The loop is
not the cost.** The payoff is the `Dependency.Complete()` at line 24: this
system runs in `SimulationSystemGroup` before `TargetSpatialHashSystem`
(`:9-10`), so that stall lands early in the simulation phase where jobs from the
previous group are still in flight.

`TargetProxyDeleteApplySystem` has the same shape and destroys entities one at a
time (`:38`).

## Scope

- `Assets/Scripts/System/Targets/TargetProxyUpdateApplySystem.cs`
- `Assets/Scripts/System/Targets/TargetProxyDeleteApplySystem.cs`

## Change

### `TargetProxyUpdateApplySystem`

The whole body moves into one `[BurstCompile] IJob`, chained on `Dependency`
instead of completing it. All four update kinds translate to `ComponentLookup`
writes:

```csharp
[BurstCompile]
private struct ApplyProxyUpdatesJob : IJob
{
    public BufferTypeHandle<TargetProxyUpdateEvent> EventHandle;
    [ReadOnly] public NativeArray<ArchetypeChunk> ScopeChunks;
    public ComponentLookup<TargetPosition> PositionLookup;
    public ComponentLookup<TargetCollisionShape> ShapeLookup;
    public ComponentLookup<Health> HealthLookup;
    public ComponentLookup<Mana> ManaLookup;

    public void Execute()
    {
        for (int c = 0; c < ScopeChunks.Length; c++)
        {
            BufferAccessor<TargetProxyUpdateEvent> accessor =
                ScopeChunks[c].GetBufferAccessor(ref EventHandle);
            for (int b = 0; b < accessor.Length; b++)
            {
                DynamicBuffer<TargetProxyUpdateEvent> buffer = accessor[b];
                for (int i = 0; i < buffer.Length; i++)
                {
                    Apply(buffer[i]);
                }

                buffer.Clear();
            }
        }
    }
    // Apply(...) is today's switch, with EntityManager.Exists replaced by
    // HealthLookup.HasComponent(evt.Proxy) / PositionLookup.HasComponent(evt.Proxy)
}
```

`Dependency = job.Schedule(Dependency);` replaces `Dependency.Complete()`.

**Existence check translation is load bearing.** Today the guard is
`EntityManager.Exists(updateEvent.Proxy)` (`:36`), which is true for *any* live
entity. In the job the equivalent is `ComponentLookup.HasComponent(proxy)`,
which is true only for entities carrying that component. For proxies these
coincide — the archetype always has all four (`CombatTargetProxy.cs:403-410`) —
but a destroyed-and-recycled `Entity` handle would behave differently.
`ComponentLookup.HasComponent` returns `false` for a stale handle (version
mismatch), which is the same outcome as `Exists`, so the substitution is safe.
Verify per-kind: `PushResourceMaxes`, `SetHealth`, `SetMana` all need
`HasComponent` on the specific lookup they write, not just one shared check.

### `TargetProxyDeleteApplySystem`

`DestroyEntity` cannot move into a job. Two changes, both main-thread:

1. Collect surviving proxies into a `NativeList<Entity>` in a `[BurstCompile]`
   job (the `Exists` filter and buffer walk).
2. One `EntityManager.DestroyEntity(NativeArray<Entity>)` call on the main
   thread, replacing the per-entity loop.

Because step 2 is structural it needs the job completed first, so this system
keeps a `.Complete()` — the win here is the single batched structural call, not
sync-point removal. Say so honestly in review; per
`Docs/reference/simulation/ecs-notes.md` §*Sync Points*, one structural call is
one sync point either way, but the per-entity form pays chunk compaction
repeatedly.

Given delete volume is per-actor-teardown rather than per-frame, **if the job
adds more complexity than the batch call saves, land only the batch
`DestroyEntity` and skip the job.** That is a legitimate outcome of this task,
not a failure to complete it.

## Deliberately Excluded: `TargetProxyCreateApplySystem`

This system has the same managed shape (`:22-64` — `CreateEntity` + 6×
`SetComponentData` + `AddComponentObject` per event), and it is **not** being
converted. Reasoning:

- **Frequency.** Creates fire on actor registration, not per frame. A 50-mob
  scene creates 50 proxies once, then approximately zero per frame thereafter.
  There is no steady-state cost to remove.
- **Managed anchor.** `CombatTargetProxy.TryTakePendingCreate` (`:275-289`)
  reads a managed `Dictionary<int, ICombatTarget>`, `AddComponentObject`
  attaches a managed `TargetCompanion`, and `target.CombatTargetProxy = proxy`
  writes back to a managed object. The main thread has to walk the events
  regardless; a job could only take over the 6 unmanaged component writes,
  leaving a second pass over the same events.
- **Shape cost.** Splitting one loop into "managed pass + job + managed pass"
  makes this system harder to read to save work that does not occur in the
  steady state. That fails the plan's own shape test.

**If the user wants it included anyway**, the change is: resolve tokens and
batch `CreateEntity(archetype, count, Allocator.Temp)` on the main thread, write
the six components in a `[BurstCompile] IJobParallelFor` over the created array,
then a second main-thread loop for `AddComponentObject` + proxy write-back.
Estimated Medium, no correctness risk, no measurable frame-time gain.

## Acceptance Criteria

- `TargetProxyUpdateApplySystem.OnUpdate` has no `Dependency.Complete()` and no
  per-event loop; `Dependency` is assigned the scheduled handle.
- All four `TargetProxyUpdateKind` branches preserve today's exact semantics,
  in particular:
  - `PushResourceMaxes` re-clamps `Current` into the new `Max`
    (`math.clamp(health.Current, 0f, health.Max)`, `:52`) — dropping this
    silently leaves over-max resources after a Max decrease.
  - `SetHealth`/`SetMana` clamp the incoming value against the *current* `Max`.
  - `RegenPerSecond` is written by `PushResourceMaxes` only.
- The scope buffer is cleared exactly once per frame, now inside the job.
- `TargetProxyDeleteApplySystem` issues at most one `DestroyEntity` call per
  frame.
- Neither system's `[UpdateInGroup]`/`[UpdateBefore]`/`[UpdateAfter]` attributes
  change — `Docs/contracts/target-proxy.md` §*Ordering* pins update-before-hash
  and delete-after-`CombatApplyBridge`, and both are load bearing for
  current-frame proxy validity.

## Dependencies

None. Independent.

## Scope/Complexity

Medium for update (one job, four branches, careful lookup translation). Small
for delete (batch call, optional job).

## Test Coverage

`Docs/contracts/target-proxy.md` describes the create/update/delete lifecycle;
proxy behavior is exercised indirectly by the collision and targeted test
suites via `CombatTargetProxy.Push`. There is no dedicated proxy-apply test
file — a focused test asserting that `PushResourceMaxes` re-clamps `Current`
after a `Max` decrease would cover the highest-risk acceptance criterion above
and does not exist today.

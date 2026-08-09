# 004 — Pool cleanup decrements

**Scope:** small-medium. **Depends on:** 001.

`CombatPoolCleanupSystem.PoolTrimJob` (`CombatPoolCleanupSystem.cs:185`) destroys
disabled pool slots. Those slots still carry template keys in their components, so
destroying them without a decrement strands the count and the entry is never
reclaimed.

## Change

Add to `PoolTrimJob`:

```csharp
[ReadOnly] public ComponentTypeHandle<ProjectileHitComponent> ProjectileHitHandle;
[ReadOnly] public ComponentTypeHandle<AoeHitSpawnComponent> AoeHitHandle;
[ReadOnly] public ComponentTypeHandle<TimedSpawnComponent> TimedSpawnHandle;
[ReadOnly] public ComponentTypeHandle<CombatHitPayload> HitPayloadHandle;
public NativeQueue<SpawnTemplateRefDelta>.ParallelWriter Deltas;
```

The job runs over a `WithAny<ProjectileTag, AoeTag>` query plus a separate targeted
query, so a chunk holds only one domain's components. Gate every read on
`chunk.Has(ref handle)` and only touch the arrays that exist.

In the destroy loop, for each disabled entity being destroyed, emit `-1` for the same
key set that 003 emits `+1` for:

- `ProjectileHitComponent.OnHitSpawn` → `(Kind, TemplateKey)`
- `AoeHitSpawnComponent.OnHitSpawn` → `(Kind, TemplateKey)`
- `TimedSpawnComponent` → `(ChildKind, TemplateKey)`
- `CombatHitPayload.StackEffect.DetonationKey` → projectile registry

`SpawnTemplateRefEmit.Enqueue` already skips default keys, so a slot that never
carried a template costs one comparison.

Read the `SpawnTemplateRegistryState` singleton in `OnUpdate` and pass
`state.Deltas.AsParallelWriter()` into both `ScheduleTrim` calls. Read it directly and
throw if missing (memory `fail-loud-singletons`) — note the test-world consequence in
[007](./007-docs-and-tests.md), since `CombatPoolCleanupSystemTests` exists in both
EditMode and PlayMode and may build worlds without a combat scope.

## Not in scope

`CombatRoot.OnDestroy` destroys all scoped entities via `DestroyScopedEntities`
(`CombatRoot.cs:126-127`) without decrementing. That is correct: the scope entity and
every native container, counters included, are disposed immediately afterwards by
`CombatScopeOwner.Release`. No accounting is needed on a teardown path.

`CombatDeathUtility.Kill` is deliberately untouched — see "Why live + pooled" in
[index.md](./index.md).

## Acceptance criteria

- Every key that 003 counts `+1` at materialization is counted `-1` exactly once when
  the slot is destroyed.
- Chunks lacking a given component are skipped without a safety-system error.
- The trim job stays `ScheduleParallel` and Burst-compiled.
- `LastDeletedCount` and the existing `CombatStatsSingleton` accounting are unchanged.

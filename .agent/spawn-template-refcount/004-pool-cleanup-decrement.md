# 004 — Despawn events

**Scope:** medium. **Depends on:** 001, 003.

Every despawn emits one release event, the mirror of the acquire in
[003](./003-apply-instance-counting.md). Death sites never touch a reference count; they
enqueue a `SpawnTemplateRefDelta` through `SpawnTemplateRefEmit.ReleaseX`.

## Despawn is exactly one thing

`Active` goes false in exactly one place — `CombatDeathUtility.Kill`. The only other
writer of `Active` is spawn-apply (sets it true) and `SpawnPoolTopUp` (creates slots
disabled, before they have ever been counted). So the death funnel is complete and the
release sites are enumerable.

`CombatDeathUtility.Kill` itself stays untouched: it only receives `EnabledRefRW` handles
and cannot see the entity's key components. The release is emitted next to each `Kill`
call, where the components are already in scope.

## Sites

| Site | Domain | Notes |
|---|---|---|
| `ProjectileHitEmission.Deactivate` | projectile | Shared funnel for **both** collision lanes; 8 call sites, one function. |
| `CombatLifetimeSystem.ProjectileLifetimeJob` | projectile | Expiry. |
| `AoeCollisionCore.Deactivate` | AOE | Shared funnel for impact and lingering collision. |
| `CombatLifetimeSystem.AoeLifetimeJob` | AOE | Lingering expiry only — impact AOEs carry no `CombatLifetimeComponent`. |
| `TargetedResolveSystem` | targeted | Normal despawn: the chain ends when its walk ends. |
| `CombatLifetimeSystem.TargetedLifetimeJob` | targeted | Fail-safe expiry. |

Each owning system reads `SpawnTemplateRegistryState` directly and passes
`state.Deltas.AsParallelWriter()` into its job.

## The enableable-component trap

`TimedSpawnComponent` is `IEnableableComponent` and is **disabled** on non-timed
projectiles and non-timed lingering AOEs. Adding it to an `IJobEntity` `Execute`
signature the normal way puts it in the query's *All* list, which matches only entities
where it is **enabled** — that would silently drop every non-timed projectile from
collision and from lifetime expiry.

Every job that reads it for release therefore carries
`[WithPresent(typeof(TimedSpawnComponent))]`:

- `ProjectileDiscreteCollisionSystem.ProjectileCollisionJob`
- `ProjectileContinuousCollisionSystem.ProjectileContinuousCollisionJob`
- `LingeringAoeCollisionSystem.LingeringAoeCollisionJob`
- `CombatLifetimeSystem.ProjectileLifetimeJob`
- `CombatLifetimeSystem.AoeLifetimeJob`

This is the single highest-risk detail in the change: getting it wrong does not fail to
compile, it silently stops non-timed projectiles from colliding.

**The attribute is not enough when the job schedules with an explicit query.**
`[WithPresent]` shapes the *generated* query only. A job scheduled as
`job.ScheduleParallel(myQuery, dep)` validates `Execute()` against `myQuery`, so every
component the signature reads must also be declared on that builder, or scheduling
throws at runtime:

> InvalidOperationException: When scheduling an instance of `…LingeringAoeCollisionJob`
> with a custom query, the query must (at the very minimum) contain all the components
> required for `…Execute()` to run.

`LingeringAoeCollisionSystem` is the only job here that both schedules with a custom
query and gained a component, so its builder carries `.WithPresent<TimedSpawnComponent>()`
alongside the attribute. `ImpactAoeCollisionSystem` and `TargetedResolveSystem` also use
custom queries but their `Execute` signatures are unchanged; the projectile collision and
lifetime jobs use generated queries, where the attribute alone is sufficient.

Impact AOEs have no `TimedSpawnComponent` at all, so `ImpactAoeCollisionSystem` passes
`default` into `AoeCollisionCore.RunCollision`; the default-key guard releases nothing.

## No double release

Every kill path returns immediately after deactivating, and every collision and lifetime
query requires `Active` enabled, so once a system disables it no later system in the
frame matches that entity. `CombatLifetimeSystem` is ordered before the collision
systems, so expiry and collision cannot both fire on one entity in one frame.

## Not in scope

`CombatPoolCleanupSystem` needs **no change**. It destroys already-disabled slots, which
released their keys when they died. Its earlier version under the live-plus-pooled model
had to read four component types with `chunk.Has` guards; that is all reverted.

`CombatRoot.OnDestroy` destroys scoped entities without releasing. Correct: the scope
entity and every native container, counters included, are disposed immediately after by
`CombatScopeOwner.Release`. No accounting is needed on a teardown path.

## Acceptance criteria

- No death site reads, writes, or names a refcount map.
- Every key acquired at spawn is released exactly once when that entity dies.
- Non-timed projectiles still collide and still expire — the `WithPresent` check.
- `CombatDeathUtility` and `CombatPoolCleanupSystem` are unchanged from HEAD.
- Collision jobs stay `ScheduleParallel` and Burst-compiled.

# 003 — `SpawnPoolTopUp` per-entity enable → query granularity

## Why

`SpawnPoolTopUp.EnsureDisabledSlots` is called by all five spawn-apply lanes
every frame there is a pool deficit. Its per-entity managed loop is the single
worst cost-per-line item in the plan:

`Assets/Scripts/System/Spawning/SpawnPoolTopUp.cs:31-38`

```csharp
NativeArray<Entity> created =
    entityManager.CreateEntity(archetype, deficit, Allocator.Temp);
for (int i = 0; i < created.Length; i++)
{
    entityManager.SetComponentEnabled<Active>(created[i], false);
}
created.Dispose();
```

`Docs/reference/simulation/ecs-notes.md` §*Optimization Strategies* gives the
measured spread for touching 1M entities: **0.03 ms** at enableable/chunk
granularity versus **35 ms** for `EntityManager` + `NativeArray` per entity.
This loop is on the wrong side of that table, on the cold-create path, in a
system whose design target is 50k projectiles.

This task is the reason the plan is a refactor rather than a wrap-in-a-job
exercise: **there is no job to move this into.** `SetComponentEnabled` is an
`EntityManager` call and stays on the main thread either way. The fix is the
granularity, not the compilation.

## Scope

- `Assets/Scripts/System/Spawning/SpawnPoolTopUp.cs`

## Change

`EntityManager.SetComponentEnabled` has an `EntityQuery` overload that operates
at chunk granularity in one call. Freshly created entities of a single archetype
land in contiguous chunks, so this is the ideal case for it.

```csharp
public static int EnsureDisabledSlots(
    EntityManager entityManager,
    EntityArchetype archetype,
    EntityQuery disabledSlotQuery,
    int demand,
    ProfilerMarker createSlotsMarker)
{
    int have = disabledSlotQuery.CalculateEntityCount();
    int deficit = demand - have;
    if (deficit <= 0)
    {
        return 0;
    }

    using (createSlotsMarker.Auto())
    {
        entityManager.CreateEntity(archetype, deficit, Allocator.Temp).Dispose();
        entityManager.SetComponentEnabled<Active>(newSlotQuery, false);
    }

    return deficit;
}
```

### The problem this creates, and how to solve it

`SetComponentEnabled<Active>(query, false)` needs a query that matches **only the
entities just created**, not the whole pool — disabling `Active` across the live
pool would despawn every active projectile in the game. There is no
"entities I just made" query.

Two viable approaches; **approach B is recommended**:

**A. Query the archetype with `Active` enabled, before anything else runs.**
Fragile: it depends on no live entity of that archetype being enabled at this
moment, which is false — the pool is full of active entities. Rejected.

**B. Create the entities already disabled.** Add a dedicated *cold-slot*
archetype per pool that is identical to the live archetype, and disable `Active`
on it once via a query scoped to that creation batch — or, cleaner, invert the
default so `Active` starts disabled.

The cleanest form of B, and the one this task specifies: keep using the returned
`NativeArray<Entity>`, but pass it to the **array overload** of
`SetComponentEnabled` if one exists in the installed Entities version, and
otherwise use a temporary `EntityQuery` built with
`EntityQueryOptions.IgnoreComponentEnabledState` plus a marker component added at
creation:

```csharp
// archetype gains typeof(ColdSlotTag) — enableable, enabled at creation
entityManager.CreateEntity(archetype, deficit, Allocator.Temp).Dispose();
entityManager.SetComponentEnabled<Active>(coldSlotQuery, false);   // matches ColdSlotTag
entityManager.SetComponentEnabled<ColdSlotTag>(coldSlotQuery, false); // consume the marker
```

where `coldSlotQuery` is `WithAll<ColdSlotTag>()` with
`IgnoreComponentEnabledState` off, so it matches only entities whose
`ColdSlotTag` is still enabled — i.e. exactly this frame's fresh batch.

### Verify the overload before building the marker

**Check first whether the installed Unity Entities version exposes
`EntityManager.SetComponentEnabled<T>(NativeArray<Entity>, bool)`.** If it does,
the whole marker-component apparatus is unnecessary and the change collapses to
a one-line substitution against the array `CreateEntity` already returns. Only
build the `ColdSlotTag` path if that overload is absent. This check is the first
step of the task, not an afterthought — it decides whether this is a 1-line or a
40-line change across five archetypes.

### Archetype impact if the marker path is needed

`ColdSlotTag` would have to be added to all five spawn archetypes:
`ProjectileDiscreteSpawnApplySystem._archetype`,
`ProjectileContinuousSpawnApplySystem`, both AOE archetypes in
`AoeSpawnApplySystem`, and `TargetedSpawnApplySystem._archetype`. Per
`Docs/coding-standards.md` §*ECS Lifecycle Comments*, the new tag needs an
`ECS Lifecycle:` comment describing that it is enabled at cold creation and
consumed in the same call.

Adding a component to five archetypes is a real cost
(`Docs/reference/simulation/ecs-notes.md` notes enableable components trade
memory and chunk fragmentation for speed). If the array overload exists, none of
this applies — which is why the version check gates the task.

## Acceptance Criteria

- The `for` loop calling `SetComponentEnabled<Active>` once per entity is gone.
- Newly cold-created slots still have `Active` disabled when
  `EnsureDisabledSlots` returns — the apply job that runs immediately after
  scans for `!activeMask[i]` and would skip them otherwise, silently dropping
  spawns.
- No live, active pool entity is ever disabled by the new call. This is the
  correctness risk of the whole task; a stress scene with a full pool plus a
  cold-create burst is the check.
- Return value semantics unchanged (`deficit`), since callers use it as the
  cold-create count for `CombatStatsSingleton.EntitiesSpawnedViaEcb`.
- If `ColdSlotTag` is introduced, it carries an `ECS Lifecycle:` comment and is
  present on all five spawn archetypes.

## Dependencies

None, but it overlaps conceptually with 002 (both on the spawn path). Different
files; either order.

## Scope/Complexity

**Unknown until the version check.** Trivial (1 line) if the `NativeArray`
overload exists; Medium (new enableable tag on five archetypes, plus lifecycle
docs) if it does not. Do the check before estimating.

## Test Coverage

- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs` — cold-create path.
- `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs` and
  `Assets/Tests/EditMode/CombatPoolCleanupSystemTests.cs` — pool warm/cold
  interaction; these are the tests most likely to catch a wrongly-scoped query.
- `Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs` — all lanes.

A new test asserting "cold-creating into a pool with N active entities leaves
those N still active" would directly cover this task's correctness risk and is
worth adding.

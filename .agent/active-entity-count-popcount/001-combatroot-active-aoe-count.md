# 001 — Delete `CombatRoot.ActiveAoeCount()` and the dead AOE counter path

Depends on: nothing. Independent of 002.
Scope: small/medium. Three source files + one doc + one test.

**Decision (user-confirmed):** do not optimize this loop — delete it. `CombatStatsGatherSystem`
already owns "active AOE count" and publishes it to `CombatStatsDisplaySingleton`,
which is the singleton game-object code is required to read
([coding-standards.md:259-266](../../Docs/coding-standards.md)). Keeping a second,
on-demand producer in `CombatRoot` would leave two answers to one question. This
task removes the duplicate path instead of making it faster.

## Why the whole path is dead, not just the loop

Reachability, verified across the repo:

```
AoePlayModeTests.cs:158  →  CombatRoot.Counters  →  ActiveAoeCount()  +  spawnedAoes
                                    ↑
                         only consumer anywhere
```

- `ActiveAoeCount()` ([CombatRoot.cs:1034-1054](../../Assets/Scripts/System/Core/CombatRoot.cs#L1034-L1054))
  is called only from `Counters`.
- `Counters` ([CombatRoot.cs:98](../../Assets/Scripts/System/Core/CombatRoot.cs#L98))
  is read only from [AoePlayModeTests.cs:158](../../Assets/Tests/PlayMode/AoePlayModeTests.cs#L158).
- `spawnedAoes` ([CombatRoot.cs:77](../../Assets/Scripts/System/Core/CombatRoot.cs#L77))
  is written at [:426](../../Assets/Scripts/System/Core/CombatRoot.cs#L426),
  [:462](../../Assets/Scripts/System/Core/CombatRoot.cs#L462),
  [:570](../../Assets/Scripts/System/Core/CombatRoot.cs#L570) and read only by
  `Counters` — write-only once `Counters` goes.
- `AoeRuntimeCounters` ([AoeRuntimeEvents.cs:65-90](../../Assets/Scripts/System/Aoes/AoeRuntimeEvents.cs#L65-L90))
  is constructed only at `CombatRoot.cs:98` — fully dead once `Counters` goes.

And the one consumer does not work. `Counters` is
`new(ActiveAoeCount(), spawnedAoes, 0, 0, 0, 0)`, so `DespawnedAoes`, `HitEvents`,
`ActiveVisuals`, `RenderBatches` are all hardcoded `0`, yet the test asserts
`DespawnedOrReusedAoes == 1` and `HitEvents == 1`. Three of its four assertions
check hardcoded zeros or are vacuous (`RenderBatches >= 0`), and it never reads
`ActiveAoes` — the value this task was originally going to optimize.

## Deletions

**1. `Assets/Scripts/System/Core/CombatRoot.cs`**

| Line | Delete |
|---|---|
| [77](../../Assets/Scripts/System/Core/CombatRoot.cs#L77) | `private int spawnedAoes;` |
| [98](../../Assets/Scripts/System/Core/CombatRoot.cs#L98) | `public AoeRuntimeCounters Counters => new(ActiveAoeCount(), spawnedAoes, 0, 0, 0, 0);` |
| [426](../../Assets/Scripts/System/Core/CombatRoot.cs#L426) | `spawnedAoes += aoeCount;` |
| [462](../../Assets/Scripts/System/Core/CombatRoot.cs#L462) | `spawnedAoes += aoeCount;` |
| [570](../../Assets/Scripts/System/Core/CombatRoot.cs#L570) | `spawnedAoes++;` |
| [1034-1054](../../Assets/Scripts/System/Core/CombatRoot.cs#L1034-L1054) | the whole `ActiveAoeCount()` method |

Each of the three `spawnedAoes` sites sits immediately before a `return aoeId;` —
delete the counter line only, keep the return.

**2. `Assets/Scripts/System/Aoes/AoeRuntimeEvents.cs`** — delete the
`AoeRuntimeCounters` struct ([:65-90](../../Assets/Scripts/System/Aoes/AoeRuntimeEvents.cs#L65-L90)).
Keep `AoeSpawnRequest` in the same file; it is live.

**3. `Docs/performance.md`** — [line 107-108](../../Docs/performance.md) currently
claims:

> `CombatRoot` exposes active count, spawn/despawn totals, hit event count,
> simulation milliseconds, and render milliseconds

That becomes false. Re-point it at the real owner, e.g.:

> `CombatStatsSingleton` accumulates active counts, spawn/despawn totals, and hit
> event counts; `CombatStatsGatherSystem` publishes them to
> `CombatStatsDisplaySingleton` for the debug overlay to read

The `Required Counters` list at [performance.md:61-80](../../Docs/performance.md)
stays valid — "active AOEs" is still exposed, via the overlay reading the display
mirror ([PerformanceUi.cs:116](../../Assets/Scripts/Debugging/PerformanceUi.cs#L116)).

## Do not delete

- **`allAoeQuery`.** `OnDestroy` needs it via `DestroyScopedEntities(allAoeQuery)`
  ([CombatRoot.cs:127](../../Assets/Scripts/System/Core/CombatRoot.cs#L127)), and it
  must stay unfiltered so disabled pool slots are destroyed too. Only
  `ActiveAoeCount()`'s use of it goes away.
- **No new query.** The earlier version of this task added an `Active`-filtered
  query. That is now explicitly *not* wanted — nothing would read it.
- `AoeSpawnRequest`, `allProjectileQuery`, the `ImpactAoeSpawnEvent` /
  `LingeringAoeSpawnEvent` buffers (still used by `AppendAoeEvent` at
  [:1171](../../Assets/Scripts/System/Core/CombatRoot.cs#L1171),
  [:1204](../../Assets/Scripts/System/Core/CombatRoot.cs#L1204),
  [:1221](../../Assets/Scripts/System/Core/CombatRoot.cs#L1221)).

## Test replacement

Delete `AoeCountersTrackSpawnDespawnHitAndRenderBatchFields`
([AoePlayModeTests.cs:148-164](../../Assets/Tests/PlayMode/AoePlayModeTests.cs#L148-L164)).

Coverage actually lost: one assertion, "a `CombatRoot` AOE spawn is counted
somewhere". Everything else it claimed to test was a hardcoded zero, and real AOE
hit dispatch is already covered by `AoeDamageDispatchCallsTargetDamageCallback`
([:134-146](../../Assets/Tests/PlayMode/AoePlayModeTests.cs#L134-L146)).

Replace it with a test that observes the surviving source of truth — spawn totals
in the display mirror — rather than a per-root field:

```csharp
[UnityTest]
public IEnumerator AoeSpawnIsRecordedInCombatStatsDisplayMirror()
{
    CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out int typeId);
    AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
    root.TargetRegistry.Register(target);

    root.Spawn(Command(typeId, templateObject, Vector2.zero, 2f), CombatFaction.Player);
    yield return null;

    EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
    using EntityQuery query = entityManager.CreateEntityQuery(
        ComponentType.ReadOnly<CombatStatsDisplaySingleton>());
    CombatStatsDisplaySingleton stats = query.GetSingleton<CombatStatsDisplaySingleton>();

    Assert.That(stats.EntitiesSpawnedViaEcb + stats.EntitiesSpawnedViaReuse,
        Is.GreaterThanOrEqualTo(1), "The AOE spawn must reach the stats mirror.");
    Cleanup(rootObject, templateObject, target.gameObject);
}
```

Requires adding `using PlayGround.System.Combat.Stats;` to the test file.

**Two traps the implementer must respect here:**

1. **Do not assert on `stats.ActiveAoes`.** `ActiveAoes` is gathered from a
   render-gated query and an impact AOE deactivates within its own frame —
   `AoeSimulationTests.cs:348` asserts `IsComponentEnabled<Active>(impact)` is
   `False` right after arming completes. After one `yield return null` the expected
   value is `0`, not `1`. An `ActiveAoes == 1` assert would be timing-fragile.
2. **Do not assert on `stats.EntitiesDespawned`.** It is derived by conservation
   one frame behind ([CombatPoolCleanupSystem.cs:104-120](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L104-L120)),
   so it is still `0` after a single frame. This is the same mistake the deleted
   test made.

Both spawn accumulators are per-frame values reset by `CombatStatsResetSystem` and
republished to the mirror each frame, so they are read in the same frame the spawn
happened — hence `>=` rather than `==`, since the shared default world may carry
other combat activity. If the run shows this assert is still unstable, drop the
replacement test rather than weakening it into a vacuous check
([testing.md:20](../../Docs/testing.md): tests must prove behavior, not that
something ran) and record the gap in `Docs/todo.md`.

## Acceptance criteria

- `ActiveAoeCount`, `Counters`, `spawnedAoes`, and `AoeRuntimeCounters` do not
  appear anywhere in `Assets/` — verify with a repo-wide search, not just the files
  listed above.
- No `ToEntityArray` / `IsComponentEnabled` loop remains in `CombatRoot`.
- `CombatRoot` still compiles with `allAoeQuery` intact; `OnDestroy` still destroys
  disabled AOE pool slots.
- No new `EntityQuery` created, so no new dispose obligation
  ([coding-standards.md:360-381](../../Docs/coding-standards.md)).
- `Docs/performance.md:107-108` no longer attributes those counters to `CombatRoot`.
- `AoePlayModeTests` compiles; the stale test is gone; the replacement passes or is
  omitted with the gap recorded.
- Every other test in `Assets/Tests/PlayMode/AoePlayModeTests.cs` still passes —
  the fixture helpers (`CreateAoeFixture`, `Command`, `CreateTarget`, `Cleanup`) are
  shared and must not change.

## Net effect

Removes a per-call `NativeArray<Entity>` allocation, an O(pool size) random-access
`IsComponentEnabled` walk, a write-only counter field, a dead public struct, a
dead public property, and a failing test — and leaves exactly one producer of
active AOE counts. Closes the `Docs/todo.md` line 16 loop at this site by deleting
it rather than by making it faster.

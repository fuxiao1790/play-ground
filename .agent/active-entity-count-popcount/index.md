# Active-entity counting without per-entity loops

Closes `Docs/todo.md` line 16: *"use popcount or equivalent to count # of active
entities in a chunk instead of looping if not already."*

## Summary

The stats path already popcounts — nothing to do there. Two remaining sites walk
entities one at a time to derive an active count. This plan removes both loops by
changing **query shape** so the Entities package produces the count from its
enabled-bit masks instead.

| # | Site | Current loop | After |
|---|------|--------------|-------|
| 1 | `CombatRoot.ActiveAoeCount()` — [CombatRoot.cs:1034-1054](../../Assets/Scripts/System/Core/CombatRoot.cs#L1034-L1054) | `ToEntityArray(Temp)` alloc + N × `IsComponentEnabled<Active>` random-access lookups | **deleted** — `CombatStatsGatherSystem` already owns this count; the whole `Counters` path is dead |
| 2 | `CombatPoolCleanupSystem.PoolTrimJob` — [CombatPoolCleanupSystem.cs:198-224](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L198-L224) | `for (i < chunk.Count) if (activeMask[i]) activeCount++`, then a second all-entity pass to destroy | `math.countbits` on the job's `chunkEnabledMask`; destroy pass driven by `ChunkEntityEnumerator` (tzcnt) |

No new types, no new data paths, one fewer producer of "active AOE count".

## Already correct — do not touch

`CombatStatsGatherSystem` ([CombatStatsGatherSystem.cs:86-88](../../Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs#L86-L88)) is
the project's authoritative active-count producer and already gets popcount for
free, because `Active` is `IEnableableComponent`
([CombatLifecycleComponents.cs:27](../../Assets/Scripts/System/Lifetime/CombatLifecycleComponents.cs#L27)):

- `EntityQuery.CalculateEntityCount()` takes its enableable branch —
  `ChunkIterationUtility.cs:986-989` → `totalEntityCount += EnabledBitUtility.countbits(chunkEnabledMask)`
- `EnabledBitUtility.countbits` = `math.countbits(u.ULong0) + math.countbits(u.ULong1)`
  (`ChunkIterationUtility.cs:1452-1455`), inside a `[BurstCompile]` method.

So **`CalculateEntityCount()` on any query containing an enableable component is
already the popcount the todo asks for.** Both fixes below are therefore phrased
as "get onto that path", not "hand-roll a popcount".

## Rationale for the chosen mechanism

The obvious-looking fix for site 2 — popcount the `Active` mask directly — is not
reachable. `ArchetypeChunk.GetEnabledMask(ref handle)` returns an `EnabledMask`
whose only public surface is `this[int]` and `GetBit(int)`
(`EnabledMask.cs:111-169`); the backing `v128` and the chunk disabled-count
pointer are package-internal (`ArchetypeChunkData.GetChunkDisabledCountForType`).
Reaching them needs `unsafe` access to package internals, which
[coding-standards.md:137](../../Docs/coding-standards.md) forbids in shared
runtime systems.

The mask that *is* handed to us safely is `IJobChunk`'s `chunkEnabledMask`, which
describes "entities in this chunk matching the query". So the fix is to make the
query's matching set be the set we want to count. `EntityQueryBuilder.WithDisabled<Active>()`
(`EntityQueryBuilder.cs:1069`, documented as *"matches chunks that must have T,
but only matches entities where T is disabled"*) makes `chunkEnabledMask` the
disabled-`Active` mask, and then:

```
disabledCount = popcount(chunkEnabledMask)
activeCount   = chunk.Count - disabledCount
```

This is the same trick the package itself uses, applied through public API.

## Constraints & invariants

| Invariant | Source | Effect on plan |
|---|---|---|
| No `unsafe` in gameplay/shared runtime systems | [coding-standards.md:137](../../Docs/coding-standards.md) | rules out reaching `EnabledMask`'s raw `v128`; forces the query-shape approach |
| `EntityQuery` created by `EntityManager.CreateEntityQuery` must be disposed by its owner | [coding-standards.md:360-381](../../Docs/coding-standards.md) | satisfied trivially — 001 creates no query (it deletes code) and 002 only reshapes queries the system already builds and owns |
| Combat paths allocation-light; `ToEntityArray` churn is a bug unless measured harmless | [coding-standards.md:335-358](../../Docs/coding-standards.md) | the `ToEntityArray(Temp)` in site 1 is the primary defect, not the loop |
| Game-object code reads `CombatStatsDisplaySingleton`, **never** `CombatStatsSingleton` | [coding-standards.md:259-266](../../Docs/coding-standards.md) | constrains the refactor option for site 1 (see comparison) |
| Despawn stays disable-in-place; disabled pools reclaimed by a bounded end-of-simulation cleanup only when headroom exists | [performance.md:43-45](../../Docs/performance.md) | trim gate semantics must not change |
| Retained pools drain gradually, never below the configured active-ratio floor | [performance.md:110-118](../../Docs/performance.md) | `ChunkActiveThresholdPercent` comparison must stay bit-identical |
| `useEnabledMask == false` means *all* chunk entities match and mask contents are **undefined** | `IJobChunk.cs:61-69`; `UnsafeChunkCacheIterator.cs:122-123` sets `useEnableBits = enabledCount != chunkEntityCount` | **load-bearing**: a fully-drained pool chunk reports `false`; popcounting the stale mask there would be wrong. Must branch on it. |
| Chunks whose mask is all-zero are skipped before the job body | `UnsafeChunkCacheIterator.cs:116-120` | chunks with zero disabled entities stop being visited — behaviour-neutral (they previously early-returned) and strictly less work |
| `ArchetypeChunk.Count` is the unfiltered entity count | `ArchetypeChunkArray.cs:29` | `chunk.Count - disabledCount` is valid |
| `Active` is the only enableable component in the affected queries | `ProjectileTag`, `AoeTag`, `TargetedTag`, `AoeIdentityComponent`, `ProjectileIdentityComponent` are all plain `IComponentData` (verified) | dropping `EntityQueryOptions.IgnoreComponentEnabledState` cannot change matching for any other type |
| `ECS Lifecycle:` comments must be updated with the lifecycle they describe | [coding-standards.md:319-333](../../Docs/coding-standards.md) | checked: no component declaration changes, `Active`'s comment stays accurate. No edit needed. |

## Mechanisms reused vs introduced

Reused, all existing:
- `EntityQuery.CalculateEntityCount()` enableable branch — the same call
  `CombatStatsGatherSystem` already relies on.
- `EntityQueryBuilder.WithDisabled<T>()` — replaces the
  `WithAll<Active>() + IgnoreComponentEnabledState` idiom, which exists in the
  codebase only because the job hand-filtered the mask itself.
- `ChunkEntityEnumerator` — public, Burst-compatible, `tzcnt`-driven
  (`ChunkEntityEnumerator.cs:31-76`), and already handles `useEnabledMask == false`
  by filling both masks with ones.
- `math.countbits(ulong)` + `v128.ULong0/1` — public, no `unsafe`, lowers to
  `popcnt` under Burst.

Introduced: nothing. No new component, singleton, query object beyond the one
`CombatRoot` query that replaces an allocation, or accounting field.

## Design validation

- **Trim decision unchanged.** `activeCount >= (int)(count * ActiveThresholdPercent / 100f)`
  keeps identical inputs. Worked against the two existing trim tests:
  - `Cleanup_TrimsTheTargetedPool` ([TargetedLifetimeAndCleanupEditModeTests.cs:79-100](../../Assets/Tests/EditMode/TargetedLifetimeAndCleanupEditModeTests.cs#L79-L100)):
    2 entities, both disabled, threshold 100. New path: all entities match →
    `useEnabledMask == false` → `disabledCount = chunk.Count = 2` →
    `activeCount = 0`, `0 >= 2` false → both destroyed. Same as today. **This test
    is exactly the `useEnabledMask == false` case, which is why the branch is
    mandatory rather than defensive.**
  - `ActiveProjectileContinuesToSimulateAfterDisabledPoolTrim` ([CombatPoolCleanupSystemTests.cs:189-203](../../Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs#L189-L203)):
    1 active + 10 disabled, threshold 40. `useEnabledMask = (10 != 11) = true` →
    `disabledCount = 10`, `activeCount = 1`, `1 >= 4` false → 10 destroyed. Same.
- **Calm-down gate untouched.** The gate reads `CombatStatsSingleton` and returns
  before any query work ([CombatPoolCleanupSystem.cs:113-134](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L113-L134)).
  Neither task touches it, so the one-frame-stale conservation math and both EMAs
  are unaffected.
- **`LastDeletedCount` stays exact.** With `WithDisabled<Active>()` the pool query
  counts disabled entities only; the trimmer destroys nothing else, so
  `before - after` is still a 1:1 count of deletions. The comment at
  [CombatPoolCleanupSystem.cs:141-142](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L141-L142)
  must be reworded from "total pool count" to "disabled pool count".
- **Cost trade, stated honestly.** Today `_poolQuery.CalculateEntityCount()` hits
  the no-filter fast path (sum of `Archetype->EntityCount`, `ChunkIterationUtility.cs:953-959`).
  Under `WithDisabled<Active>()` it walks chunks and popcounts instead. That is 4
  such calls per trim pass, against a pass that already schedules two parallel
  jobs over every pool chunk — negligible, and only on calm-down frames.
- **Net win beyond the loop.** `_poolQuery.IsEmpty` at
  [CombatPoolCleanupSystem.cs:136](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L136)
  becomes a precise gate: "no disabled pool entities" instead of "no pool
  entities at all". A calm scene whose pools are fully active now early-outs
  instead of scheduling two jobs that find nothing to do.
- **Site 1 loses no live behaviour.** The deleted value had no reader: the only
  consumer never asserted `ActiveAoes`, and the remaining fields it did assert were
  hardcoded zeros. `allAoeQuery` survives unfiltered so `OnDestroy` still destroys
  disabled AOE pool slots. The `ImpactAoeSpawnEvent` /
  `LingeringAoeSpawnEvent` buffers stay in use by `AppendAoeEvent`; only the count
  that read their `.Length` goes away.

## Minimal/additive vs refactor comparison

Site 2 has no meaningful additive alternative — the loop is the implementation, and
replacing it is the change. The comparison is real for **site 1**.

**Minimal/additive** — add an `Active`-filtered AOE query beside `allAoeQuery`, keep
`ActiveAoeCount()` and `AoeRuntimeCounters.ActiveAoes`.
- resulting data flow: unchanged shape — GameObject asks ECS for a count on demand.
- new concepts/types: none; one extra `EntityQuery` field.
- copies/translations added: none. Removes one `NativeArray<Entity>` copy.
- long-term cost: the project keeps **two** producers of "active AOE count" —
  `CombatStatsGatherSystem` (per frame, render-gated, published to
  `CombatStatsDisplaySingleton`) and `CombatRoot.ActiveAoeCount()` (on demand,
  identity-gated, plus pending spawn-event buffers). Neither is wrong; they answer
  slightly different questions with the same name.

**Refactor** — delete `ActiveAoeCount()`; have `CombatRoot.Counters` read
`CombatStatsDisplaySingleton.ActiveAoes`, or delete `Counters` outright.
- resulting data flow: one producer, one source of truth for active AOE count.
- concepts changed/removed: `ActiveAoeCount()` gone; `AoeRuntimeCounters.ActiveAoes`
  either re-sourced or removed with the property.
- copies/translations removed: the whole second query path.
- long-term benefit: matches the documented rule that game-object code reads the
  display mirror ([coding-standards.md:259-266](../../Docs/coding-standards.md)).
- cost: **changes what the number means** — the mirror's `ActiveAoes` is
  render-gated (`CombatRenderComponent`), excludes AOEs still sitting in the
  spawn-event buffers, and is one presentation pass old.

**Decision: refactor — user-confirmed.** Delete the duplicate path rather than
optimize it. What tipped it beyond the general rule: the entire chain is dead
except for one broken test.

```
AoePlayModeTests.cs:158  →  CombatRoot.Counters  →  ActiveAoeCount()  +  spawnedAoes
                                    ↑                                        ↑
                         only consumer anywhere            write-only once Counters goes
```

`Counters` is `new(ActiveAoeCount(), spawnedAoes, 0, 0, 0, 0)`, so `DespawnedAoes`,
`HitEvents`, `ActiveVisuals` and `RenderBatches` are hardcoded zeros — while the
test asserts `DespawnedOrReusedAoes == 1` and `HitEvents == 1` and never reads
`ActiveAoes` at all. Three of its four assertions check hardcoded zeros or are
vacuous. So the additive option would have optimized a value nothing reads, and
kept `AoeRuntimeCounters` alive as a second answer to a question
`CombatStatsGatherSystem` already answers. Task 001 is now a deletion; see
[001](./001-combatroot-active-aoe-count.md) for the full inventory, including the
`Docs/performance.md:107-108` claim that goes stale with it.

**Default decision rule applied:** two data paths described "active AOE count";
the plan collapses to the one that already exists.

## Open questions

1. ~~Optimize `ActiveAoeCount()` or delete it?~~ **Resolved: delete.** See the
   decision above.
2. Is `PerformanceUi` / `PerformanceText` expected to grow a combined
   "total active" row? There is currently no total field anywhere; the only sum is
   the cleanup gate's local `activeLoad`
   ([CombatPoolCleanupSystem.cs:115-117](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L115-L117)).
   Out of scope here; noted because "total active entity count" has no single owner.

## Tasks

| Task | File | Depends on | Scope |
|---|---|---|---|
| 001 | [001-combatroot-active-aoe-count.md](./001-combatroot-active-aoe-count.md) | none | Small/medium — delete dead path across 3 source files + 1 doc + 1 test |
| 002 | [002-pool-trim-job-popcount.md](./002-pool-trim-job-popcount.md) | none | Small/medium — one system, query shape + job body |

Independent; either order. 002 is the only one that touches a per-frame path.

## Verification

Agents do not run tests. After implementing, the user runs:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe' -automated -runTests -batchmode -nographics -projectPath . -testPlatform EditMode -testResults "e:\UnityHub\projects\play-ground\TestResults\active-entity-count-popcount-editmode-results.xml" -logFile Logs\EditModeTests.log
```

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe' -automated -runTests -batchmode -nographics -projectPath . -testPlatform PlayMode -testResults "e:\UnityHub\projects\play-ground\TestResults\active-entity-count-popcount-playmode-results.xml" -logFile Logs\PlayModeTests.log
```

Review the XML before claiming a pass.

Must stay green (task 002): `Cleanup_TrimsTheTargetedPool`, every test in
`Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs`,
`Assets/Tests/EditMode/CombatPoolCleanupSystemTests.cs`,
`Assets/Tests/PlayMode/TargetedSkillPlayModeTests.cs`.

Must stay green (task 001): every remaining test in
`Assets/Tests/PlayMode/AoePlayModeTests.cs` — the shared fixture helpers are
untouched.

Expected diff in results: `AoeCountersTrackSpawnDespawnHitAndRenderBatchFields`
disappears (deleted as stale — it asserted against hardcoded zeros), replaced by
`AoeSpawnIsRecordedInCombatStatsDisplayMirror`. **Capture a baseline run before
implementing 001** so the pre-existing failure is on record rather than looking
like a regression introduced here.

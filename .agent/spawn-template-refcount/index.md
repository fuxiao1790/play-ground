# Spawn Template Registry Reference Counting

Make all three spawn-template registries reclaimable. Today entries are added and
never removed, so every loadout recompile and every mob bind leaks entries into
`NativeHashMap`s that live for the whole session.

## Summary

Each registry entry gains two counters:

- **`OwnerCount`** — managed registrations. `RegisterSpawnTemplate` increments,
  `UnregisterSpawnTemplate(kind, key)` decrements.
- **`InstanceCount`** — ECS entities whose components currently carry the key.
  Spawn-apply increments, pool destroy decrements.

An entry is erased only when `OwnerCount == 0 && InstanceCount == 0`. `Unregister`
is therefore exactly the "mark for deletion" the design calls for — it drops the
managed claim; the entry survives until no in-flight entity can still dereference
it, then a late-simulation sweep erases it.

Identity stays what it is today: `(IntervalChildKind kind, Hash128 key)`, where
`key` is the xxHash3 content hash of the normalized command
(`SpawnTemplateHash.Of`, `SpawnTemplateComponents.cs:62`) and `kind` selects the
registry. `OnHitSpawnRef` already carries that exact pair.

## Constraints & Invariants

| Invariant | Source | Effect on this change |
|---|---|---|
| Registry is written only by managed pre-tick code and is immutable for the whole simulation tick; every sim job may take the map `[ReadOnly]` and read it concurrently | `Docs/reference/simulation/spawn-template-registry.md` "Registry Concurrency Contract" | Counter mutation and erase must never happen inside the tick from a job. Deltas are queued from jobs; a single-threaded system applies them and erases after all sim jobs are complete. |
| Template values must stay blittable and Burst-readable | same doc, "Registry rules" | Counters live in a **separate** map, not in the command value. `ProjectileSpawnTemplate` / `AoeSpawnTemplate` / `TargetedSpawnTemplate` stay byte-identical, so every existing `[ReadOnly] TryGetValue` job read is untouched. |
| Registry maps are owned by `CombatScopeOwner`, created on first `Acquire` and disposed on last `Release` | `CombatScopeOwner.cs:49-68`, `:121-141` | New counter maps and the delta queue are allocated and disposed in the same place, same `Allocator.Persistent`. |
| Missing key on dereference is a silent `continue`, not a throw | `ProjectileSpawnExpansionSystem.cs:190-194`, `AoeSpawnExpansionSystem.cs:39`, `TargetedSpawnExpansionSystem.cs:32` | Erasing too early produces a silent behavior loss, not a crash — so it will not be caught by tests. The conservative counting below is deliberate. |
| Combat ECS singletons the sim needs are read directly and throw if missing | memory `fail-loud-singletons` | The new refcount-state singleton is read directly, no `TryGetSingleton` guard, no `RequireForUpdate` skip. Test worlds that lack a combat scope must create one (see `007`). |
| Ids are content hashes, so identical templates from different owners **dedupe to one entry** | `SpawnTemplateHash.Of`, `CombatRoot.cs:195-209` | A bare "dead" flag is unsafe: one owner's unregister would condemn an entry other owners still hold. `OwnerCount` is the flag, counted. See "Why OwnerCount" below. |
| `MonoBehaviour.Update()` runs before `SimulationSystemGroup`; the combat world is appended to the player loop | `CombatEcsWorld.cs:32` | Managed register/unregister (Update) and the sweep (LateSimulation) are both main-thread and strictly ordered within a frame. No locking needed. |

## Why OwnerCount, given "unregister just marks it dead"

Ids are content-addressed. Every mob of the same type compiles identical templates,
and player and mobs all share one `CombatRoot` and one scope
(`GameRoot.cs:68`/`:108`, `SpawnController.cs:111`), so N `SkillDriver`s routinely
claim the same entry.

With a single dead flag: driver A unregisters, entry is marked; driver B still holds
the key in its compiled definitions but has no live entities at that instant;
`InstanceCount` hits 0; entry is erased; B's next cast silently drops. Counting the
managed claims removes the failure mode without changing the caller's mental model —
`Unregister` is still "I am done with this id."

## Why "live + pooled", not "live"

Despawn is `Active = false` via `CombatDeathUtility.Kill`, called from many parallel
collision and lifetime jobs that hold only `EnabledRefRW<Active>` and cannot see the
entity's key components. Threading a delta writer through every `Kill` call site
would be invasive and would still leave pooled-disabled slots holding stale keys.

Instead `InstanceCount` counts entities that **currently carry the key in their
components**, alive or pooled-dead. It is decremented at the two points where those
components stop carrying the key:

- spawn-apply overwrites a reused disabled slot (old keys `-1`, new keys `+1`)
- `CombatPoolCleanupSystem` destroys a disabled slot (`-1`)

This over-counts relative to "live entities", which is the safe direction — an entry
is never erased while anything could still dereference it. Reclaim lags until the
pool slot is reused or trimmed, which is acceptable because reclaim is a memory
optimization, not a correctness requirement. `CombatDeathUtility` is untouched.

## Mechanisms Reused vs. Introduced

Reused:

- `NativeQueue<T>.ParallelWriter` handed to sim jobs and drained by one system —
  the existing pattern for `CircularVfxSpawnRequest` (`CombatDeathUtility.cs:56-57`,
  `VfxEmit.Enqueue`).
- `CombatScopeOwner` allocation/dispose ownership for scope-owned native containers.
- The single materialization funnels that already exist:
  `ProjectileSpawnApplyUtility.WriteCommon` (both projectile lanes) and
  `AoeSpawnApplyUtility.WriteCommon` (both AOE lanes).
- `LateSimulationSystemGroup`, already used by `CombatPoolCleanupSystem`.

Introduced:

- `SpawnTemplateRegistryState` — one scope singleton holding three
  `NativeHashMap<Hash128, SpawnTemplateRefCount>` plus one delta queue.
  Justification: keeping counters out of the command maps is what preserves the
  Burst read path and the "value is a command-shaped template" rule.
- `SpawnTemplateRefCountSystem` — the single-threaded drain + erase point.
- `CombatRoot.UnregisterSpawnTemplate(kind, key)`.

## Minimal/additive vs. refactor comparison

**Minimal/additive** — counters folded into the map value
(`NativeHashMap<Hash128, ProjectileTemplateEntry{Command, Refs}>`):

- resulting data flow: every expansion job reads a wrapper and unpacks `.Command`
- new concepts/types introduced: 3 wrapper structs
- copies/translations added: one extra struct copy per template fetch in every
  expansion and timed-spawn job; entry becomes writable so the `[ReadOnly]`
  concurrent-read guarantee has to be re-argued
- long-term cost: the doc's "the stored value is a command-shaped template, there
  is no separate `TemplateData` struct" rule is broken; every reader touched

**Refactor / sidecar** — counters in a separate scope singleton (chosen):

- resulting data flow: command maps unchanged; counters and deltas are a managed +
  single-threaded-system concern that no Burst reader sees
- existing types changed: `CombatScopeOwner` (allocate/dispose), `CombatRoot`
  (register/unregister), the two `WriteCommon` funnels, `PoolTrimJob`
- copies/translations avoided: zero change to the hot template fetch
- long-term benefit: one owner for lifetime accounting; the concurrency contract in
  the doc stays literally true for the command maps

**Decision:** sidecar. The counters are lifetime metadata, not spawn data; putting
them in the value would push lifetime bookkeeping into every Burst hot path.

## Design validation

- *Immutable during tick* — deltas are queued, never applied, from jobs. Application
  and erase happen in `SpawnTemplateRefCountSystem` in `LateSimulationSystemGroup`
  after `CombatPoolCleanupSystem`, with the dependency completed first. No sim job
  is in flight at that point, and the next managed write is in the following frame's
  `Update()`.
- *Burst read path* — the three template components are unchanged; no job signature
  that reads templates changes.
- *No early erase* — erase requires both counters at zero. `InstanceCount` counts
  pooled slots too, so it is an upper bound on live references.
- *Queued spawn events* — a slim event referencing key K is enqueued by collision or
  `TimedSpawnSystem` during tick N and expanded during tick N. The source entity is
  still counted at enqueue time, and erase happens no earlier than LateSimulation of
  tick N, after expansion has already consumed the event.
- *Root-cast templates* — no entity stores the key of its own template, so a root
  template's `InstanceCount` is 0 and its lifetime is governed purely by
  `OwnerCount`. Correct: only managed skill definitions reference it.
- *Dedupe sharing* — handled by `OwnerCount`, see above.
- *Ad-hoc `Spawn()` registrations* — `CombatRoot.Spawn(ProjectileSpawnRequest)`
  (`:189`) and `Spawn(AoeSpawnRequest)` (`:542`) register per cast with no owner to
  release. They register **pinned** (never erased), which is exactly today's
  behavior and is bounded by distinct content, not by call count.

## Default decision rule

One source of truth per concept: the command map owns template data, the counter map
owns template lifetime, keyed identically. No second representation of a template.

## Task list

| # | Task | Depends on |
|---|---|---|
| [001](./001-registry-refcount-state.md) | `SpawnTemplateRegistryState` singleton + `CombatScopeOwner` ownership | — |
| [002](./002-combatroot-register-unregister-api.md) | `CombatRoot` owner-count register / unregister / pin API | 001 |
| [003](./003-apply-instance-counting.md) | Instance `+1`/`-1` in the three spawn-apply materialization points | 001 |
| [004](./004-pool-cleanup-decrement.md) | Instance `-1` in `CombatPoolCleanupSystem.PoolTrimJob` | 001 |
| [005](./005-refcount-sweep-system.md) | `SpawnTemplateRefCountSystem` drain + erase | 001, 003, 004 |
| [006](./006-skilldriver-owner-lifecycle.md) | `SkillDriver` tracks its key set, unregisters on recompile / destroy | 002 |
| [007](./007-docs-and-tests.md) | Doc updates + test coverage + test-world scope setup | 001-006 |

## Open questions

1. **`TargetedSpawnCommand.OnHitSpawn`** (`TargetedSpawnPipeline.cs:58`) is never
   materialized onto the targeted entity — no targeted ECS component carries an
   `OnHitSpawnRef`. Either it is a dead field or targeted on-hit spawn is
   unimplemented. `003` counts only `StackEffect.DetonationKey` for targeted until
   this is resolved; flagged, not fixed here.
2. **Pinned-entry policy** for the ad-hoc `Spawn()` paths is stated above as a
   decision. If those paths should instead become properly owned, that is a separate
   change to their callers.

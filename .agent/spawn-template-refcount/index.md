# Spawn Template Registry Reference Counting

Make all three spawn-template registries reclaimable. Today entries are added and
never removed, so every loadout recompile and every mob bind leaks entries into
`NativeHashMap`s that live for the whole session.

## Summary

Each registry entry gains two counters:

- **`OwnerCount`** — managed registrations. `RegisterSpawnTemplate` increments,
  `UnregisterSpawnTemplate(kind, key)` decrements.
- **`InstanceCount`** — ECS entities whose components currently carry the key.

An entry is erased only when `OwnerCount == 0 && InstanceCount == 0`. `Unregister`
is therefore exactly the "mark for deletion" the design calls for — it drops the
managed claim; the entry survives until no in-flight entity can still dereference
it, then a late-simulation sweep erases it.

**No system that spawns or despawns entities mutates a reference count.** Every spawn
emits one acquire event and every despawn emits one release event onto a shared
`NativeQueue<SpawnTemplateRefDelta>`. The emitting code holds only a `ParallelWriter`;
it cannot name, read, or write a counter. `SpawnTemplateRefCountSystem` drains the
queue and is the only place a count is touched.

Both halves go through `SpawnTemplateRefEmit`, which is the single definition of which
keys each domain carries. `AcquireX` and `ReleaseX` are the same walk with an opposite
sign, so the two cannot disagree, and both read the entity's **components** rather than
the spawn command — so branches that zero a component at materialization need no
mirroring anywhere.

Identity stays what it is today: `(IntervalChildKind kind, Hash128 key)`, where
`key` is the xxHash3 content hash of the normalized command
(`SpawnTemplateHash.Of`, `SpawnTemplateComponents.cs:62`) and `kind` selects the
registry. `OnHitSpawnRef` already carries that exact pair.

## Constraints & Invariants

| Invariant | Source | Effect on this change |
|---|---|---|
| Registry is written only by managed pre-tick code and is immutable for the whole simulation tick; every sim job may take the map `[ReadOnly]` and read it concurrently | `Docs/reference/simulation/spawn-template-registry.md` "Registry Concurrency Contract" | Counter mutation and erase must never happen inside the tick from a job. Deltas are queued from jobs; a single-threaded system applies them and erases after all sim jobs are complete. |
| Template values must stay blittable and Burst-readable; `sizeof(AoeSpawnCommand) < 4096` | same doc, "Registry rules"; `ProjectileAuthoringEditModeTests.cs:91` | Counters live in a **separate** map, not in the command value, so applying a `+1`/`-1` does not read-modify-write a ~4KB struct. Side effect: the three registry components stay byte-identical and every existing `[ReadOnly] TryGetValue` job read is untouched. |
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

## Why symmetric spawn/despawn events

`InstanceCount` counts **live** entities. One acquire at spawn, one release at death.

The alternative considered first was counting entities that carry a key whether alive or
pooled-dead, releasing when a reused slot is overwritten or when
`CombatPoolCleanupSystem` destroys it. That avoided touching the death sites, but it is
worse on every axis: two different code paths mean "this entity is gone", the release
site has to read back the slot's previous occupant before overwriting it, `PoolTrimJob`
has to do component archaeology through `chunk.Has` guards on four types, and reclaim
lags until a slot is reused or trimmed.

Symmetric events are simpler and reclaim as soon as the last entity dies.
`CombatDeathUtility.Kill` itself stays untouched — it only receives `EnabledRefRW`
handles and cannot see key components, so the release is emitted beside each `Kill`
call, where the components are already in scope.

What it costs, honestly: the release sites are the two hottest jobs in the game.
Projectile and lingering-AOE collision and lifetime jobs each gain a
`NativeQueue.ParallelWriter` field and read `TimedSpawnComponent`, which they did not
touch before. The chunk array is only dereferenced on the death branch, so the steady
-state cost is a wider query, not a wider inner loop.

`CombatPoolCleanupSystem` needs no change at all under this model.

## Mechanisms Reused vs. Introduced

Reused:

- `NativeQueue<T>.ParallelWriter` handed to sim jobs and drained by one system —
  the existing pattern for `CircularVfxSpawnRequest` (`CombatDeathUtility.cs:56-57`,
  `VfxEmit.Enqueue`).
- `CombatScopeOwner` allocation/dispose ownership for scope-owned native containers.
- The single materialization funnels that already exist:
  `ProjectileSpawnApplyUtility.WriteCommon` (both projectile lanes) and
  `AoeSpawnApplyUtility.WriteCommon` (both AOE lanes).
- The existing shared funnels, so the release sites stay countable:
  `ProjectileHitEmission.Deactivate` (both projectile collision lanes) and
  `AoeCollisionCore.Deactivate` (impact and lingering).
- `LateSimulationSystemGroup`, already used by `CombatPoolCleanupSystem`.

Introduced:

- `SpawnTemplateRegistryState` — one scope singleton holding three
  `NativeHashMap<Hash128, SpawnTemplateRefCount>` plus one delta queue.
  Justification: a 12-byte counter value keeps delta application off the ~4KB
  template struct. See the comparison below — this is a deliberate exception to the
  one-source-of-truth default rule, with a stated containment plan.
- `SpawnTemplateRefCountSystem` — the single-threaded drain + erase point.
- `CombatRoot.UnregisterSpawnTemplate(kind, key)`.

## Minimal/additive vs. refactor comparison

**Additive — sidecar counter maps** (chosen):

- resulting data flow: command maps untouched; counters and deltas are a managed +
  single-threaded-system concern no Burst reader sees. Two containers keyed by the
  same `Hash128`, inserted and erased in lockstep.
- new concepts/types introduced: `SpawnTemplateRegistryState`,
  `SpawnTemplateRefCount`, `SpawnTemplateRefDelta`, `SpawnTemplateRefCountSystem`
- copies/translations added: none on the read path. Delta application is a
  read-modify-write of a 12-byte value.
- long-term cost: **structural warning — two structures describing one concept that
  must stay in sync.** A key present in the command map but absent from the counter
  map, or vice versa, is a silent bug. Containment below.

**Refactor — counters folded into the map value**
(`NativeHashMap<Hash128, ProjectileTemplateEntry{Command, Refs}>`):

- resulting data flow: one container per domain. Template data and template lifetime
  have a single source of truth; insert and erase are atomic by construction.
- existing types changed: the three registry components; every reader unpacks
  `.Command` — `ProjectileSpawnExpansionSystem.cs:191`, `AoeSpawnExpansionSystem.cs:39`,
  `TargetedSpawnExpansionSystem.cs:32`, `ExternalSpawnGateSystem.cs:156`,
  `TimedSpawnSystem`, plus tests. Modest, ~8 sites.
- copies/translations removed or avoided: removes the sync obligation entirely.
- long-term benefit: no drift possible; fewer allocations (3 maps, not 6).

**Decision: choose additive**, against the default rule, for one concrete reason:
**write amplification on delta application.**

`sizeof(AoeSpawnCommand)` is asserted `< 4096`
(`ProjectileAuthoringEditModeTests.cs:91`) and `ProjectileSpawnCommand` is the same
order. `NativeHashMap` has no in-place value mutation — applying one delta is
`TryGetValue` + `map[key] = entry`, so folding the counters in makes every `+1`/`-1`
copy the whole ~4KB template twice. This project's stated target is extreme spawn
counts; at a few hundred spawns per frame with two or three keys each, that is
megabytes of memcpy per frame to move two integers. The sidecar's value is 12 bytes,
so the same work is ~350x cheaper and stays in cache.

Two arguments that look like they favour the sidecar do **not** hold, and are
deliberately not part of the rationale — do not resurrect them in review: the extra
12 bytes on a 4KB `TryGetValue` copy is not a meaningful read-path cost, and the
`[ReadOnly]` concurrency guarantee is unaffected either way, since under both designs
the only writer is `SpawnTemplateRefCountSystem` at the same point in the frame. Write
amplification is the whole case.

**Containing the structural warning.** The sync obligation is real, so it is confined
rather than left implicit:

- exactly one writer per map pair — `CombatRoot.AddOwner` inserts, and
  `SpawnTemplateRefCountSystem` is the only place that erases, always removing from
  both maps together (task 005)
- the drain creates a counter entry on demand, so a release for a key the counter map
  has never seen is well-defined rather than a throw
- task 007 test 9 asserts every `InstanceCount` returns to 0 after a full
  spawn/expire/trim cycle — the standing regression guard
- if delta volume ever turns out to be low in practice, the refactor above is the
  correct simplification and should be revisited

The acquire/release halves cannot drift: both go through `SpawnTemplateRefEmit`, where
`AcquireX` and `ReleaseX` are the same private walk with an opposite sign, reading the
same component types. Adding a key-carrying field means editing one method. The
clamp-and-assert in 005 step 3 still catches a genuine mismatch — a negative
`InstanceCount` proves a release arrived with no matching acquire.

## Resulting codebase shape

After the change: template *data* lives where it lives today, unchanged and
Burst-read the same way. Template *lifetime* is a new, clearly-owned concern with one
state singleton, one writer system, and one managed entry point (`CombatRoot`
register/unregister). Registration gains a symmetric release that `SkillDriver` is
responsible for — which is the piece missing today, not just an optimization.

Spawn and despawn systems do not participate in reference counting. The apply systems
and death sites gain a `NativeQueue.ParallelWriter` parameter and one enqueue call, and
nothing else. None of them can name, read, or write a counter. The key set for each
domain, and the reference counting itself, are each legible in one file.

No adapter or shim code, no old/new parallel path, no second representation of a
template. The one new coupling is the two-map key sync, contained as above.

## Design validation

- *Immutable during tick* — releases are queued, never applied, from jobs. Application
  and erase happen in `SpawnTemplateRefCountSystem` in `LateSimulationSystemGroup`
  after `CombatPoolCleanupSystem`, with the dependency completed first. No sim job
  is in flight at that point, and the next managed write is in the following frame's
  `Update()`.
- *No refcount mutation from spawn/despawn systems* — every spawn and death site holds
  only a `NativeQueue.ParallelWriter` and cannot name, read, or write a counter.
- *Enableable-component trap* — `TimedSpawnComponent` is disabled on non-timed sources,
  so every job reading it for release declares `[WithPresent]`. Without it the query
  would silently drop all non-timed projectiles from collision and expiry. This does
  not fail to compile; it is the highest-risk detail in the change.
- *Never-live entities* — an impact AOE with nothing to collide against is materialized
  inactive (`SpawnStateFor` returns `active: collision`) and never reaches a death site.
  Acquire is gated on `spawnState.Active` so it never claims a reference.
- *No double release* — every kill path returns immediately after deactivating, and
  every collision and lifetime query requires `Active` enabled, so no later system in
  the frame matches an entity another already killed. `CombatLifetimeSystem` is ordered
  before the collision systems.
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

**Rule:** when two representations or data paths describe the same domain concept,
refactor toward one source of truth unless there is a concrete compatibility or
migration reason not to.

**How this plan stands against it:** template *data* and template *lifetime* are two
concepts, and each has exactly one owner — the command map and the counter map. A
template is never represented twice. The rule is still strained, because the two maps
share a key space and must be inserted and erased together; that is the structural
warning named above. The exception is taken on a concrete performance ground (write
amplification), not on "avoid touching existing code", and it carries a containment
plan and a revisit condition.

Everywhere else the rule holds unconditionally: no second template struct, no
event-to-command remap, no parallel registry, no compatibility shim.

## Task list

| # | Task | Depends on |
|---|---|---|
| [001](./001-registry-refcount-state.md) | `SpawnTemplateRegistryState` singleton + `CombatScopeOwner` ownership | — |
| [002](./002-combatroot-register-unregister-api.md) | `CombatRoot` owner-count register / unregister / pin API | 001 |
| [003](./003-apply-instance-counting.md) | Spawn events at the four materialization points | 001 |
| [004](./004-pool-cleanup-decrement.md) | Despawn events at the six death sites | 001, 003 |
| [005](./005-refcount-sweep-system.md) | `SpawnTemplateRefCountSystem` — derive acquire, drain release, erase | 001, 003, 004 |
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

---
name: target-proxy-event-pipeline
description: Convert CombatTargetProxy's managed->ECS writes (Create/Delete/Push/PushResourceMaxes/SetHealth/SetMana) from direct synchronous EntityManager calls into an event+apply-system pipeline, mirroring the existing projectile/AOE spawn event pattern.
---

# Target Proxy: Event-Based Managed→ECS Pipeline

## Summary

`PlayerRoot`/`MobRoot` currently push actor data into ECS by calling `CombatTargetProxy`
static methods that mutate the proxy entity directly and synchronously
(`EntityManager.SetComponentData`/`CreateEntity`/`DestroyEntity`) from inside
`MonoBehaviour.Update()`. Every other managed→ECS boundary in the project (projectile
and AOE spawns) instead uses an event → apply split: managed code appends a slim,
blittable event struct to a buffer on the shared scope entity; a dedicated ECS system
drains it later, in a fixed `SimulationSystemGroup`/`PresentationSystemGroup` phase
(`Docs/contracts/spawn-events-and-commands.md`, ADR-003). This plan brings target-proxy
writes onto that same mechanism, with **full lifecycle parity** (Create, Delete, Push,
PushResourceMaxes, SetHealth, SetMana all become events) and **deferred entity
allocation** (the proxy `Entity` is not known synchronously after `Create()` returns,
exactly like a spawned projectile's `Entity` isn't known synchronously after
`CombatRoot.Spawn()`).

Grounding this against the actual code (not just docs) surfaced two real bugs that this
migration must fix as part of the same change, and one file the docs don't mention.
Both are load-bearing for the design and are recorded below rather than left implicit.

## Rationale For Major Architectural Decisions

- **Full lifecycle parity, not just the per-frame `Push`.** The recurring per-frame
  transform push was the original trigger for this request, but partially converting
  (only `Push`) while leaving `Create`/`Delete`/`SetHealth`/`SetMana` synchronous would
  create two different mutation models for the same entity, decided by which method you
  call — a structural warning per this project's own decision rule (two data paths for
  one concept). User explicitly chose full parity over partial.
- **Deferred entity allocation, not a pre-allocated synchronous entity.** A "populate
  fields later, allocate now" middle ground was considered and rejected by the user in
  favor of matching spawn exactly (see [Minimal/Additive vs Refactor](#minimaladditive-vs-refactor-comparison)
  below) — entity allocation itself happens inside the apply system, not inside
  `Create()`.
- **One shared token, not `ICombatTarget.TargetId`, as the create-event correlator.**
  `TargetId` looked like a natural fit (already synchronous, stable, used as a bare
  correlator by the legacy `CombatTargetSet`) but is **not globally unique**: `PlayerRoot`
  and `MobRoot` each declare their own `private static int nextTargetId` (`PlayerRoot.cs:56`,
  `MobRoot.cs:45`), so the first player and the first mob both get `TargetId == 1`. This
  collision already silently exists in `CombatTargetSet.targetsById[target.TargetId]`
  (`CombatTargetSet.cs:90`) — legacy/compat code nobody has hit hard enough to notice.
  Reusing it for the create correlator would import that bug into new load-bearing code.
  Fix: `CombatTargetProxy` mints its own `nextCreateToken`, the same way `CombatRoot`
  mints `nextProjectileId`/`nextAoeId` rather than trusting caller-supplied ids.

## Constraints & Invariants The Change Must Respect

| Invariant | Source |
|---|---|
| Buffer element / component data types must be unmanaged (blittable) — no `ICombatTarget`/managed refs inside event structs | `Docs/reference/simulation/ecs-notes.md` (Unity ECS chunk memory model); confirmed by existing `TargetCompanion` being a separate managed `IComponentData`, never a buffer element |
| Do not put managed references in event or command payloads | `Docs/contracts/spawn-events-and-commands.md` Restrictions |
| Proxy push must happen before simulation collision/tracking reads | `Docs/contracts/target-proxy.md` Ordering; `Docs/flows/runtime-frame.md` step 2 |
| Proxy deletion must occur after current-frame hit/result replay safety | `Docs/contracts/target-proxy.md` Ordering; `Docs/flows/runtime-frame.md` step 12 |
| `SetHealth`/`SetMana` must push resource maxes and clamp Current against the *just-updated* Max, in that order | `CombatTargetProxy.cs:189-192, 206-209` (current implementation) |
| Simulation jobs must not read managed `TargetCompanion` | `Docs/contracts/target-proxy.md` Restrictions |
| Actor `LateUpdate()` ordering against ECS presentation systems is explicitly unverified | `Docs/flows/runtime-frame.md:69`, `Docs/flows/target-proxy-lifecycle.md:61` (open TODOs) — treated as a constraint: don't stack new assumptions on top of it |
| `CombatApplyBridge.ReplayCombat` is what actually reads `TargetCompanion` off `result.TargetProxy` to fire `ReceiveHit`/`ReceiveCombatTick` for the frame, and runs in `PresentationSystemGroup` | `CombatApplyBridge.cs:10,91` (confirmed via grep) |
| `TargetSpatialHashSystem` is the only system querying `TargetProxyTag+TargetPosition+TargetCollisionShape+TargetFaction` together, already `UpdateBefore` the whole collision/finalize/regen chain | `Broadphase/TargetSpatialHashSystem.cs:42-46` (confirmed via grep) |

## Mechanisms Reused vs. Introduced

**Reused:**
- The event → apply split itself (ADR-003), applied to a new domain rather than inventing
  a parallel pattern.
- The shared scope entity (`CombatScopeOwner`) as the single managed→ECS mailbox — new
  buffers are added to the same entity that already carries `ProjectileSpawnEvent`,
  `ExternalSpawnRequest`, `ImpactAoeSpawnEvent`, `LingeringAoeSpawnEvent`
  (`CombatScopeOwner.cs:53-57`), not a new dedicated entity.
- `TargetCompanion` as the existing mechanism for holding a managed back-reference on an
  ECS entity, resolved from a plain (non-Burst) system on the main thread — the same
  shape the new create-apply system uses to write `target.CombatTargetProxy` back.
- `CombatTargetProxy`'s existing public method names/most signatures — `Push`,
  `PushResourceMaxes`, `Delete`, `SetHealth`, `SetMana` keep their current parameter
  lists, so `PlayerRoot`/`MobRoot` need zero call-site signature changes for those.

**Introduced (justified):**
- Three new event struct types (`TargetProxyCreateEvent`, `TargetProxyUpdateEvent`,
  `TargetProxyDeleteEvent`) — necessary because no existing event type carries proxy
  identity/transform/resource data; this is the same shape as `ProjectileSpawnEvent`
  et al., not a new pattern.
- Three new apply systems — necessary because no existing system drains proxy events;
  mirrors `ProjectileSpawnApplySystem`/`AoeSpawnApplySystem` naming and shape.
- A `nextCreateToken`/`pendingCreates`/`pendingTokenByTarget` bookkeeping trio inside
  `CombatTargetProxy` — necessary only because, unlike a spawned projectile, a target
  proxy's `Entity` must eventually be written back onto a specific managed object
  (`target.CombatTargetProxy`). Spawn has no equivalent because nothing external ever
  needs the projectile's raw `Entity` back.

## Design Validation

- **Blittability / no managed refs in events**: validated — `TargetProxyCreateEvent`
  carries only an `int Token` plus value-type ECS component data; the managed
  `ICombatTarget` reference lives in a **managed-side** `Dictionary` inside
  `CombatTargetProxy` (main-thread only, never touched by a job), resolved by token
  inside the apply system's `OnUpdate` (main thread, non-Burst) — never inside the
  buffer itself. Matches how `TargetCompanion` already keeps managed data out of
  anything Burst/job-visible.
- **Push-before-simulation-reads**: validated — `TargetProxyCreateApplySystem` and
  `TargetProxyUpdateApplySystem` both declare `[UpdateBefore(typeof(TargetSpatialHashSystem))]`,
  and that system's own `UpdateBefore` chain (traced, not assumed) already reaches every
  current consumer of proxy position/shape/health/mana before this change.
- **Delete-after-replay-safety**: validated — `TargetProxyDeleteApplySystem` runs in
  `PresentationSystemGroup` with `[UpdateAfter(typeof(CombatApplyBridge))]`, so the
  entity and its `TargetCompanion` survive until after `ReplayCombat` has read them for
  the frame. This reproduces (does not merely assume) the safety the current
  `LateUpdate()` deferral provides.
- **SetHealth/SetMana ordering**: validated — `Push`/`PushResourceMaxes`/`SetHealth`/`SetMana`
  share **one** `TargetProxyUpdateEvent` buffer/system (a tagged union, not four separate
  buffers), so FIFO append order alone reproduces the current
  "push maxes, then clamp Current" sequence without reconstructing ordering logic by
  hand.
- **`LateUpdate()` TODO not compounded**: validated by explicitly *not* touching it —
  `deleteProxyInLateUpdate`/`LateUpdate()` stays exactly as-is; only what happens inside
  it changes (enqueue instead of direct `DestroyEntity`).

## Minimal/Additive vs. Refactor Comparison

**Minimal/additive approach**: Keep `CombatTargetProxy`'s current synchronous methods
exactly as they are; add a second, parallel set of "queued" entry points
(`PushQueued`, `CreateQueued`, ...) that new call sites opt into, leaving the old direct
methods in place for existing tests/back-compat.
- resulting data flow: two parallel paths both mutating the same proxy entities —
  whichever path a given call site happens to use.
- new concepts/types introduced: event structs *and* a second API surface on
  `CombatTargetProxy`.
- copies/translations added: none directly, but any code path that could reach both
  APIs risks the two writes racing with each other on the same entity.
- long-term cost: two ways to mutate proxy state indefinitely, no single source of
  truth for "how does managed code touch a proxy," and it directly reproduces the exact
  pattern ADR-003 already rejected for spawns ("Direct entity creation from producers:
  rejected because it scatters allocation policy and structural changes").

**Refactor approach (chosen)**: `CombatTargetProxy`'s existing method names/signatures
are kept as the public surface, but every body becomes enqueue-only — there is exactly
one path to mutate proxy ECS state, through the event buffers and apply systems.
`CombatTargetRegistry`'s dead entity-hash bookkeeping (`targetsById`, `proxyKeyByTarget`,
confirmed zero consumers via repo-wide grep) is deleted rather than kept "just in case."
`SkillDriver`'s caster-snapshot bug — a real latent issue that deferred creation
promotes from "always resolved by the time anyone reads it" to "sometimes stale forever"
— is fixed as part of this change rather than patched around.
- resulting data flow: single path, matching the spawn pipeline's one-path model
  exactly.
- existing concepts/types changed or removed: `CombatTargetProxy` internals,
  `CombatTargetRegistry`'s dead fields, `SkillDriver`'s cached-`Entity` field (replaced
  with a cached `ICombatTarget` read live).
- copies/translations removed or avoided: no parallel bookkeeping, no second API
  surface, no synchronization between "old" and "new" proxy-write paths.
- long-term benefit: one mental model for every managed→ECS boundary in the project
  (spawn intent and proxy intent both look the same), consistent with ADR-003's own
  stated rationale.

**Decision: refactor.** Reason: the additive path is not a neutral middle ground — it
reproduces an anti-pattern ADR-003 already rejected, and this project's own default
decision rule ("if two representations or data paths describe the same domain concept,
refactor toward one source of truth unless there is a concrete compatibility or
migration reason not to") applies cleanly here: there is no external consumer depending
on synchronous `Create` timing that can't be updated — every current synchronous
consumer is in-repo (`PlayerRoot`, `MobRoot`, `SkillDriver`, test files), all covered by
this same plan.

## Default Decision Rule Applied

Two ID schemes existed for target correlation before this change: `ICombatTarget.TargetId`
(managed-side, not globally unique — see Rationale above) and
`CombatTargetProxy.TargetKey(Entity)` (ECS-side, entity-derived, globally unique but
requires a live entity). Rather than inventing a third, the create-event correlator
(`nextCreateToken`) is scoped *only* to the create→apply handoff window and is discarded
once `target.CombatTargetProxy` is resolved — it does not become a second general-purpose
target identity. `TargetKey(Entity)` remains the one ECS-side identity once an entity
exists; `TargetId`'s cross-faction collision is flagged as a pre-existing, separate issue
(see Open Questions) rather than silently fixed here, since fixing it is a larger,
independently-scoped change.

## Task List

1. [001-events-and-scope-wiring.md](001-events-and-scope-wiring.md) — new event structs; `CombatScopeOwner` buffer wiring.
2. [002-combat-target-proxy-rewrite.md](002-combat-target-proxy-rewrite.md) — `CombatTargetProxy` internals become enqueue-only; token/pending-dictionary bookkeeping.
3. [003-apply-systems.md](003-apply-systems.md) — `TargetProxyCreateApplySystem` / `TargetProxyUpdateApplySystem` / `TargetProxyDeleteApplySystem`, with explicit ordering.
4. [004-registry-and-combatroot-cleanup.md](004-registry-and-combatroot-cleanup.md) — delete dead `CombatTargetRegistry`/`CombatRoot` bookkeeping.
5. [005-skilldriver-lazy-caster.md](005-skilldriver-lazy-caster.md) — fix the caster-snapshot bug in `SkillDriver`.
6. [006-actor-root-call-sites.md](006-actor-root-call-sites.md) — `PlayerRoot`/`MobRoot` `BindCaster` call sites + delete-guard fix (including `MobRoot.SoftDie`).
7. [007-test-updates.md](007-test-updates.md) — buffer wiring, `Create` return-type call sites, world-tick insertion in existing PlayMode tests.
8. [008-docs-update.md](008-docs-update.md) — update `target-proxy.md`, `target-proxy-lifecycle.md`, `runtime-frame.md` to describe the event-based flow.

Dependency order: 001 → 002 → {003, 004} → 005 → 006 → 007 → 008. (004 only needs 002's
`Create` return-type change, not 003; 005 has no code dependency on 002/003/004 but is
sequenced before 006 since 006 changes the call sites 005's new signature affects.)

## Open Questions / Follow-Ups (not blocking this plan)

- `ICombatTarget.TargetId` cross-faction collision (`PlayerRoot`/`MobRoot` independent
  `nextTargetId` counters) is a pre-existing bug, surfaced but **not fixed** by this
  plan — it affects the legacy `CombatTargetSet`, which this plan does not touch. Worth
  a separate, explicitly-scoped fix (e.g. a shared `CombatTargetIdAllocator`).
- Collapsing the `LateUpdate()` proxy-delete deferral into relying solely on
  `TargetProxyDeleteApplySystem`'s `UpdateAfter(CombatApplyBridge)` ordering is possible
  but deliberately deferred until the existing `TODO: verify actor LateUpdate() ordering
  against ECS presentation systems` (`runtime-frame.md:69`) is independently resolved.
- `ExternalSpawnGateSystem.TrySpendMana`'s existing "missing caster = spend succeeds, no
  gate" fallback (`ExternalSpawnGateSystem.cs:122-127`) becomes reachable in a new
  (narrow, one-tick) window once `SkillDriver` reads a lazily-resolving caster. Recorded
  as an accepted edge case, not re-engineered here.

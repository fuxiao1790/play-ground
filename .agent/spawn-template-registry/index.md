# Spawn-Template Registry (events-as-templates, unified timed spawn)

## Summary

Fix the Burst crash and the recursive struct bloat from interval-spawn templates by storing
**spawn events themselves** in an ECS registry keyed by content hash, and collapsing the two
per-domain interval tick systems into **one thin system** that only ticks a cooldown and emits
a stored event into the existing spawn pipeline. No template→event conversion, no second spawn
path.

## Problem

Interval-spawn templates embed recursive value-type snapshots
(`IntervalProjectileChild` → `ProjectileImpactProjectileSnapshot` → … → `StackEffectSnapshot`),
so each behavior tier roughly doubles the struct: `AoeIntervalSpawnerComponent` ≈ 4.0 KB,
`AoeSpawnCommand` ≈ 4.9 KB. `AoeSpawnExpansionSystem` writes commands through a `NativeStream`
(~4 KB block limit) → `ArgumentException: Allocation size is too large` in
`AoeExpansionJob.Execute`. The same payload bloats every event/command and ECS chunk.

A secondary structural problem: two tick systems (`TimedProjectileSpawnSystem`,
`TimedAoeSpawnSystem`) each duplicate the cooldown loop, the `ChildKind` branch, and a
~30-field hand-built spawn event. That builder logic belongs in expansion, not in a tick
system.

## Architectural decisions

1. **The spawn event is the template.** The registry stores `ProjectileSpawnEvent` /
   `AoeSpawnEvent` directly (`NativeHashMap<Hash128, …SpawnEvent>`). There is **no** separate
   `…TemplateData` struct and **no** template→event conversion. Fetch the stored event, stamp a
   few per-instance fields, enqueue.

2. **Canonical path only.** Everything flows through the existing
   **spawn event → spawn expansion → spawn command → spawn apply**. The unified tick system
   writes the existing event type into the existing `EventQueue`. No new event type, queue,
   resolve step, or parallel path.

3. **One unified, thin tick system.** A single `TimedSpawnSystem` queries any active entity
   with a lifetime and a timed-spawn component — source domain (projectile vs lingering AOE) is
   irrelevant because both share `Active`, `CombatLifetimeComponent`, `CombatKinematicsComponent`.
   Its whole body: tick cooldown → when due, fetch the stored event by key, stamp
   per-instance fields, enqueue. The `ChildKind` switch (which `EventQueue`) lives in exactly
   one place.

4. **Self-describing timed-spawn component.** One `TimedSpawnComponent` replaces the per-domain
   spawner components; it carries `{ Faction, SourceId, ChildKind, Hash128 TemplateKey,
   IntervalSeconds, IntervalJitterSeconds, JitterSeed }`. Hot timer state stays in a separate
   `TimedSpawnStateComponent { CooldownRemaining, TickIndex }`.

5. **Singletons created GameObject-side.** The registry maps live on the existing shared scope
   entity, created/disposed by the ref-counted `CombatScopeOwner` (mirroring how the scope and
   its buffers are created from `CombatRoot.BindWorld`), not in a system `OnCreate`.
   Registration flows through `CombatRoot.RegisterTimedSpawnTemplate`, like `RegisterType`.

6. **Content-hash dedup; never-recycle (v1).** The key is the `Hash128` of the stored event
   with per-instance fields left default at registration, so identical behavior collapses to
   one entry. Entries are never removed in v1 (bounded by distinct compiled behaviors); a
   refcount + grace-period sweep is a documented future enhancement.

7. **Loop guard retained.** The single tick loop hard-caps iterations per update and clamps the
   per-tick advance to a positive minimum so a bad/zero interval can never hard-freeze the
   editor in Burst.

## Task list

- [001-template-data-and-registry-storage.md](001-template-data-and-registry-storage.md) —
  events-as-templates registry: singleton components, scope-entity ownership, content hashing.
- [002-combatroot-registration-api.md](002-combatroot-registration-api.md) —
  `CombatRoot.RegisterTimedSpawnTemplate(in ProjectileSpawnEvent / in AoeSpawnEvent)`.
- [003-compile-time-registration-walk.md](003-compile-time-registration-walk.md) — build child
  spawn events at compile, register, store `TemplateKey`; delete `…TemplateData` + conversion.
- [004-slim-carriers.md](004-slim-carriers.md) — unified `TimedSpawnComponent`/state/tag;
  source events/commands carry it; delete per-domain spawner components.
- [005-tick-systems-and-fanout.md](005-tick-systems-and-fanout.md) — one thin `TimedSpawnSystem`
  (fetch/stamp/enqueue + loop guard); delete the two old tick systems; expansion unchanged.
- [006-apply-systems.md](006-apply-systems.md) — apply bakes the unified component + zeroed
  state + tag on both projectile and lingering-AOE source archetypes.
- [007-validation-tests-docs.md](007-validation-tests-docs.md) — regression + dedup/never-recycle
  coverage; docs.

## Recommended order

001 → 002 → 003 → 004 → 005 → 006 → 007. 003 needs the registry + API (001–002); 005/006 need
the unified component (004); 007 last.

## Constraints / dependencies

- Burst/ECS: stored events must stay blittable; `NativeHashMap<Hash128, …SpawnEvent>` is read
  `[ReadOnly]` in the tick job and the registry map is mutated only on the main thread at
  compile time (`RegisterTimedSpawnTemplate` completes tracked jobs before mutating).
- The shared world/scope is ref-counted across faction roots; the maps are created/disposed once
  with the scope entity (`CombatScopeOwner`).
- Interval spawners attach only to root/managed-submitted sources, not to on-hit/impact-spawned
  entities.
- Per-instance fields (`Position`, `Faction`, `BaseProjectileId`/`AoeId`, `JitterSeed`,
  `DeterministicIdTickIndex`) must be default in stored events so dedup is behavior-only.

## Verification (end-to-end)

1. Unity compiles; `sizeof(AoeSpawnCommand) < 4096` (existing EditMode guard) — the
   `Allocation size is too large` crash is gone.
2. The lingering-AOE-source → projectile/AOE child loadout that previously froze the editor now
   spawns children at the configured interval over the source lifetime, stops at expiry, applies
   damage; no freeze, no Burst exception.
3. Dedup: identical child behavior → one map entry; differing behavior → distinct keys.
4. Dynamic count: changing `spawnCount` recompiles to a new key; re-selecting a prior count
   reuses its entry.
5. Regression: `BareMinimumPrototypePlayModeTests` + `AoePlayModeTests` +
   `ProjectileSpawnPipelineTests` pass (proj→proj cadence and deterministic ids unchanged).

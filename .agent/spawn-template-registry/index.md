# Spawn-Template Registry (ECS, content-hashed)

## Summary

Stop embedding heavy, recursive value-type spawn templates by value in spawn
components/commands. Store each template **once** in an ECS registry keyed by its content
hash, and have carriers reference it by a small `Hash128 TemplateKey`. This fixes a Burst
crash, removes per-spawn/per-chunk bloat, and unifies "timed" (interval) spawns with ordinary
spawns under a single template kind per domain.

## Problem

Interval-spawn-trigger templates recurse through the snapshot tree
(`IntervalProjectileChild` → `ProjectileImpactProjectileSnapshot` → `ProjectileImpactAoeSnapshot`
→ `StackEffectSnapshot`/`AoeOnHitSpawnSnapshot`), so each behavior tier roughly *doubles* the
struct. Measured: `IntervalProjectileChild` ≈ 2.9 KB, `IntervalAoeChild` ≈ 1 KB,
`AoeIntervalSpawnerComponent` ≈ 4.0 KB, `AoeSpawnCommand` ≈ 4.9 KB. `AoeSpawnExpansionSystem`
writes commands through a `NativeStream` whose per-element block is ~4 KB, so the oversized
command throws `ArgumentException: Allocation size is too large` in `AoeExpansionJob.Execute`.
The projectile pipeline (uses `NativeQueue`) doesn't crash but carries the same bloat through
every event/command and ECS chunk.

## Architectural decisions

1. **One template kind per domain, not "timed" vs "ordinary".** A timed/interval spawn is just
   a spawn template plus a timer. So there is a single `ProjectileSpawnTemplate` and a single
   `AoeSpawnTemplate`, shared by interval children and (as a follow-on) ordinary spawns.

2. **Three data tiers** — the crux of the fix:
   - **Registry (cold, shared, content-hashed):** the full spawn-event behavior **including
     fan-out** (`Count`/spread/pattern/jitter) and **damage/crit** (inside `CombatHitPayload`).
     ≈ today's `IntervalProjectileChild` / `IntervalAoeChild` minus only per-instance fields
     (position/direction/source). Referenced by `Hash128 TemplateKey`.
   - **Per-entity cold timer config** (set once at spawn, read each tick, not mutated): on the
     slim spawner component — `{ ChildKind, IntervalSeconds, IntervalJitterSeconds, TemplateKey }`.
   - **Per-entity HOT timer state** (mutated every tick): the existing
     `ProjectileChildSpawnStateComponent` / `AoeIntervalSpawnStateComponent`
     (`{ CooldownRemaining, TickIndex }`) — stays on the entity, never in the registry.

3. **Singletons are created GameObject-side, matching this project.** Entities here are created
   by ref-counted owner helpers from `CombatRoot.BindWorld()` (`CombatEcsWorld.Acquire`,
   `CombatScopeOwner.Acquire`), and `RegisterType`/`RegisterTemplate` are managed dictionaries
   on `CombatRoot`. The registry singleton therefore lives **on the existing shared scope
   entity** (created once, ref-counted, torn down by the last owner) — not in a system
   `OnCreate`. Registration flows through a new `CombatRoot.RegisterTimedSpawnTemplate`.

4. **Dedup is inherent.** The map key is the template's `Hash128` content hash, so identical
   templates collapse to one slot. The deterministic jitter seed is excluded from the hashed
   template (it stays inline on the slim component), so identical *behavior* shares one entry.

5. **Never-recycle for v1.** Insert-if-absent only; entries are never removed. Bounded by the
   number of *distinct* compiled behaviors over a session (dozens), so memory is small and the
   logic is trivial — no refcount, no sweep, no dedicated system. Refcount + grace-period
   removal is a documented future enhancement.

## Scope

Define the two unified kinds and **wire the interval spawners through them now** — this fixes
the crash and the interval-spawn bloat. Because the kind is shared, ordinary projectile/AOE
spawns (and the interval children's own spawn commands, which today re-inline the same
`ImpactAoe`/`ImpactProjectile`/`StackEffect`) can later adopt the identical template +
`TemplateKey` — same registry, no new kind. That broader migration touches the collision/impact
systems and is **out of scope** for the crash fix.

## Task list

- [001-template-data-and-registry-storage.md](001-template-data-and-registry-storage.md) —
  template data structs, singleton components, content hashing, scope-entity ownership.
- [002-combatroot-registration-api.md](002-combatroot-registration-api.md) —
  `CombatRoot.RegisterTimedSpawnTemplate` insert-if-absent with job-completion guard.
- [003-compile-time-registration-walk.md](003-compile-time-registration-walk.md) —
  `TemplateKey` on setup objects; `PlayerSkillDriver.RegisterIntervalTemplates`; move builders
  out of the translator.
- [004-slim-carriers.md](004-slim-carriers.md) — slim the spawner components/events/commands to
  carry `TemplateKey`; rename `SpawnerId` → `JitterSeed`.
- [005-tick-systems-and-fanout.md](005-tick-systems-and-fanout.md) — tick systems look up the
  template by key and emit a spawn event carrying fan-out; expansion fans out (incl. radial).
- [006-apply-systems.md](006-apply-systems.md) — apply systems bake the slim spawner component.
- [007-validation-tests-docs.md](007-validation-tests-docs.md) — regression + dedup/never-recycle
  coverage; docs.

## Recommended order

001 → 002 → 003 → 004 → (005 ∥ 006) → 007. 005 and 006 both depend on the slim carrier shapes
from 004; 003 depends on the registry/API from 001–002; 007 depends on everything.

## Constraints / dependencies

- Burst/ECS: template data must stay blittable (no managed refs); `NativeHashMap<Hash128,T>`
  is Burst-readable via singleton lookup.
- Registration writes the map from `CombatRoot` on the main thread at compile time only
  (Start / loadout change); it completes outstanding combat-world jobs before mutating, since
  the map is read by Burst tick jobs.
- The shared world is ref-counted across faction roots; the registry maps are created/disposed
  exactly once with the scope entity (`CombatScopeOwner`).
- Interval spawners attach only to root/managed-submitted projectiles/AOEs, not to
  on-hit/impact-spawned ones (those builder paths pass `default`/no key).

## Verification (end-to-end)

1. **Compile**: Unity builds clean; `sizeof(AoeSpawnCommand)` drops to ~1 KB — the original
   `Allocation size is too large` crash no longer fires.
2. **Crash repro**: the lingering-AOE-source → AOE/projectile child loadout that previously
   threw now spawns children at the configured interval over the source lifetime, stops at
   expiry, and applies damage; no Burst exception.
3. **Dedup**: two slots with identical child behavior share one map entry; differing behavior →
   distinct keys.
4. **Dynamic count**: changing `spawnCount` recompiles to a new `TemplateKey`/entry (count is in
   the template); re-selecting a prior count reuses its entry.
5. **Never-recycle**: recompiling to different behavior while old spawner entities are alive —
   old entities keep firing (their key still resolves); map grows only by distinct behaviors.
6. **Regression**: `BareMinimumPrototypePlayModeTests` + `AoePlayModeTests` +
   `ProjectileSpawnPipelineTests` pass (proj→proj cadence and deterministic ids unchanged).

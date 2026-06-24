# Interval Spawn Triggers — Rename + AOE/Projectile 2×2

## Summary

Generalize the current `ChildSpawnTrigger` (a projectile-in-flight periodically spawning
child projectiles) into two intent-named triggers, and support every combination of
duration **source** and spawned **child**:

- Rename `ChildSpawnTrigger` → **`ProjectileIntervalSpawnTrigger`** (spawns projectiles).
- Add **`AoeIntervalSpawnTrigger`** (spawns AOEs).
- Either trigger may fire from **any duration skill** as source: a projectile (lifetime) or
  a lingering AOE (lifetime). Pulse AOEs have no duration and are invalid sources.

2×2 matrix (source → child):

| Source → Child | Projectile child (`ProjectileIntervalSpawnTrigger`) | AOE child (`AoeIntervalSpawnTrigger`) |
|---|---|---|
| **Projectile source** | proj→proj — **already implemented** (rename only) | proj→aoe — **new** |
| **Lingering AOE source** | aoe→proj — **new** | aoe→aoe — **new** |

## Key architectural decisions & rationale

1. **A source carries exactly one interval spawner.** The chain parser links slot `i` →
   `i+2`, so each cause index appears in exactly one chain. A source skill set therefore has
   at most one outgoing trigger link and one child kind — never both at once. This removes
   archetype combinatorics: we never need a "both spawners" variant.

2. **Triggers are `ScriptableObject` assets referenced by asset GUID.** `TriggerLink :
   ScriptableObject`; loadout `.asset` files reference the trigger by its **asset GUID**.
   Renaming the class requires renaming the `.cs` file (Unity SO filename rule) and updating
   `m_EditorClassIdentifier` in the trigger asset YAML, but keeping the asset GUID and the
   `.cs.meta` script GUID stable means **no loadout edits** and no broken references.

3. **Mirror the existing projectile child-spawner pattern.** Per-source component holds the
   flattened child template; a per-frame Burst job decrements a cooldown and enqueues child
   spawn events into the relevant expansion queue (`ProjectileSpawnExpansionSystem.EventQueue`
   or `AoeSpawnExpansionSystem.EventQueue`). All four child-emission targets already accept
   "enqueue an event"; the new work is the tick driver + carrying the template on the source.

   **Principle (tick systems stay thin):** the only job of `TimedProjectileSpawnSystem`'s new
   branch and the new `TimedAoeSpawnSystem` is to *emit spawn events into the existing
   expansion `EventQueue`s*. They never create/pool/initialize child entities themselves — the
   existing expansion → apply pipeline owns fan-out, world-bounds resolution, VFX, pooling, and
   materialization. This keeps the children identical to any other spawned projectile/AOE and
   adds no parallel spawn path. (Parent-source entities still need their spawner *component*
   baked at their own spawn time — that is the only place new apply/archetype wiring is added,
   in tasks 004/005.)

4. **Shared, blittable child-template structs** (`IntervalProjectileChild`,
   `IntervalAoeChild`) are reused by both source tick systems so emission logic isn't
   duplicated per source type.

5. **Directionality defaults** (AOE sources have no facing/velocity):
   - Projectile child from an AOE source → even **radial 360° fan** of `Count` directions at
     the source center (`sideSpreadDegrees` unused). Projectile child from a projectile source
     keeps the existing velocity-relative `SideSpray`.
   - AOE child from any source → spawn at the source center; `Count > 1` emits N events at the
     center (ring-offset is a possible later refinement).
   - Both reuse the existing deterministic per-tick jitter/id hashing from
     `TimedProjectileSpawnSystem` for determinism.

   > CONFIRMED (user, 2026-06-24): AOE-source projectile children use the **radial 360° fan**
   > (Count directions evenly around the full circle from the AOE center; `sideSpreadDegrees`
   > unused).

## Constraints / dependencies

- Burst/ECS: spawner components and child templates must be blittable structs (no managed
  refs); reuse the deterministic hashing helpers.
- Pooling: AOE spawn-apply reuses dead entities keyed by `(faction, typeId, lingering)`;
  adding spawner components needs a key flag + dedicated archetype so plain lingering AOEs
  aren't reused as spawner AOEs (mirrors the projectile basic-vs-childspawner split).
- Interval spawners attach only to **root/managed-submitted** AOEs, not to on-hit/impact
  spawned AOEs (those builder paths pass `default`).

## Task list

- [001-rename-projectile-interval-trigger.md](001-rename-projectile-interval-trigger.md) —
  rename `ChildSpawnTrigger` → `ProjectileIntervalSpawnTrigger` (file/class/asset/refs).
- [002-aoe-trigger-and-compiler.md](002-aoe-trigger-and-compiler.md) — new
  `AoeIntervalSpawnTrigger`, runtime setup fields, compiler dispatch on parent type.
- [003-projectile-source-ecs.md](003-projectile-source-ecs.md) — shared child templates +
  projectile source emitting AOE children (proj→aoe).
- [004-aoe-source-ecs.md](004-aoe-source-ecs.md) — lingering-AOE source: components,
  `TimedAoeSpawnSystem`, spawner archetype in `AoeSpawnApplySystem` (aoe→proj, aoe→aoe).
- [005-translator-and-plumbing.md](005-translator-and-plumbing.md) — translator + request /
  event / command propagation for both source types.
- [006-validation-docs-tests.md](006-validation-docs-tests.md) — validator warning, doc
  update, test updates + new-combo coverage.

## Recommended order

001 → 002 → (003 ∥ 004) → 005 → 006. 005 depends on the component shapes from 003/004; 006
depends on everything. 003 and 004 are independent and can proceed in parallel after 002.

## Verification (end-to-end)

1. Unity compiles; renamed trigger asset still deserializes — loadouts using it
   (`ArrowSpawnBulletStackAoeLoadout`, `BenchmarkMaxLoadout`, …) load without "missing
   script".
2. Regression: `BareMinimumPrototypePlayModeTests` + a benchmark loadout using the renamed
   trigger — proj→proj child cadence and deterministic ids unchanged.
3. New combos: author one loadout each for proj→aoe, aoe→proj, aoe→aoe (lingering AOE source
   where required); in Play mode confirm spawns at the configured interval over the source's
   lifetime, stop at expiry, and apply damage. Check ECS profiler counters for pooled reuse
   and no per-frame structural-change leaks.
4. EditMode: compiler populates the correct setup field per trigger×source; non-lingering AOE
   source yields a validation warning.

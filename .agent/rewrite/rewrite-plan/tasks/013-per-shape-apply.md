# Task 013: Per-shape apply systems (start explicit)

## Goal
Replace the single bucketed `ProjectileSpawnApplySystem` (which branches on a `HasChildSpawner` shape key at apply time) with **one command container + one apply system per real shape** (design §5.3, D-SHAPE-EXPLICIT). Shape is chosen in **expansion**; apply never re-derives it.

Real shapes today:
- projectile **basic** (no child-spawner archetype),
- projectile **child-spawner** (child-spawner archetype),
- AoE (single shape).

Do NOT build apply systems for shapes that have no producer (§5.3 still forbids speculative shapes).

## Required Reading
- `../context/002-target-architecture.md` §1.2, §2 (projectile/AoE apply)
- `../context/005-decision-log.md` → D-SHAPE-EXPLICIT, D-EXPANSION-OWNS-MATH
- `../context/003-data-flow.md` §1–4

## Current Code References
- `System/Projectile/ProjectileSpawnApplySystem.cs` — drains one command array, buckets by `(faction, typeId, hasChildSpawner)`, two archetypes (`archetypeNoChildSpawner`, `archetypeWithChildSpawner`), reuse `IJobChunk` + cold-create ECB.
- `System/Projectile/ProjectileSpawnExpansionSystem.cs` — currently writes one command output for all shapes.
- `System/Aoe/AoeSpawnApplySystem.cs` — single shape already; mainly a rename + `Active` switch.

## Required Changes
1. **Expansion routes per shape:** `ProjectileSpawnExpansionSystem` writes into **two** command containers — `BasicProjectileCommandContainer` and `ChildSpawnerProjectileCommandContainer` — selecting by the resolved `HasChildSpawner`. The `ProjectileSpawnCommand` struct is shared; the *container* encodes the shape.
2. **Two apply systems:** `BasicProjectileSpawnApplySystem` and `ChildSpawnerProjectileSpawnApplySystem`. Each owns one archetype, one `WithDisabled<Active>` reuse query (+ the faction/type shared-component filter), and one ECB cold-create path with that archetype's exact component set. **No `HasChildSpawner` branch inside apply.** Lift the proven reuse/cold-create job bodies out of the old bucketed system, one per shape.
3. **AoE:** keep the single `AoeSpawnApplySystem`; it already has one shape — just consume `AoeSpawnCommand` (Task 012) and query `Active` (Task 011).
4. Keep the faction/type bucketing **within** a shape (shared-component filter for render batching) — that is not shape re-derivation, it is the existing render-key filter.
5. Wire ordering: both new apply systems sit at phase 9.9 (`../context/004`), after expansion; no apply runs before all collisions.
6. Recompile; run.

## Behavior Preservation Requirements
- Same entities produced (id, velocity, archetype, components) as the bucketed version; reuse picks the same slots per shape; cold-create count unchanged. The split is structural only.
- One ECB playback per apply system per frame (Global Invariant 3); container disposal per `../context/006`.

## Dependencies
011 (queries `Active`), 012 (`ProjectileSpawnCommand` name).

## Acceptance Criteria
- [ ] Expansion routes to per-shape containers; each container == one archetype == one apply system.
- [ ] No apply system reads `Count`/`Spread`/`Jitter` or branches on a shape key.
- [ ] Basic vs child-spawner projectiles reuse only their own shape's disabled slots.
- [ ] No speculative apply systems for shapes without producers.
- [ ] Repo compiles; suite green; spawn parity tests pass.

## Risk
Medium — correctness of per-shape reuse/cold-create parity. Mitigate with the spawn-parity and per-shape reuse tests (Task 017).

# 003 — Projectile-source ECS (shared templates + proj→aoe)

**Scope:** Medium. Extend the existing projectile child-spawner to also emit AOE children.
**Dependencies:** 002. Independent of 004 (can run in parallel).

## Changes

1. Shared child-template structs (blittable; `Assets/Scripts/System/Common/`):
   - `IntervalProjectileChild` — the fields currently inside
     [ProjectileChildSpawnerComponent](Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs)
     (type, speed, lifetime, geometry, damage, pierce, tracking, impact snapshots, stack
     effect, visual, count, pattern, spread). For projectile sources the existing component
     already serves; factor the shared fields into this struct so 004 can reuse them on AOE
     entities without duplication.
   - `IntervalAoeChild` — fields to build an `AoeSpawnEvent`: `TypeId`, `Lifetime`,
     `RepeatHitCooldownSeconds`, geometry (`Radius`/`HalfExtents`/`RotationRadians`/`ShapeType`/
     `AreaSize`), `CombatHitPayload`, `AoeProjectileBurstSnapshot`, `AoeOnHitSpawnSnapshot`,
     `CombatRenderComponent`, `Count`.
   - `IntervalChildKind { Projectile, Aoe }` enum.

2. Projectile spawner components
   [ProjectileEcsComponents.cs](Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs):
   - Add `AoeIntervalSpawnerComponent` wrapping `IntervalAoeChild` + `IntervalSeconds` +
     `IntervalJitterSeconds`.
   - Add an `IntervalChildKind ChildKind` field to `ProjectileChildSpawnStateComponent` so the
     tick job knows which template to read. (Existing proj→proj sets `ChildKind = Projectile`.)

3. Spawn-apply archetype
   [ProjectileSpawnApplySystem.cs](Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs):
   add `AoeIntervalSpawnerComponent` to the `ChildSpawnerProjectileSpawnApplySystem` archetype
   (the single child-spawner projectile archetype already exists and is tag-gated). Bake it +
   the `ChildKind` from the command in `CreateProjectileEntity` and the reuse job.

4. Tick driver
   [TimedProjectileSpawnSystem.cs](Assets/Scripts/System/Projectile/TimedProjectileSpawnSystem.cs):
   branch on `childSpawnState.ChildKind`:
   - `Projectile` → existing `EnqueueChildSpawn` path (unchanged) → `ProjectileSpawnExpansionSystem.EventQueue`.
   - `Aoe` → build an `AoeSpawnEvent` from `AoeIntervalSpawnerComponent` at the projectile
     position (reuse `AoeSpawnPipeline` event-build conventions: world bounds, render, hit
     payload) and enqueue into `AoeSpawnExpansionSystem.EventQueue`. Acquire the managed
     `AoeSpawnExpansionSystem` and forward its `ProducerHandle` exactly as the system already
     forwards to `ProjectileSpawnExpansionSystem`.
   - For AOE children with `Count > 1`, emit `Count` events at the projectile position
     (directionality decision in index.md item 5).
   - Reuse the existing deterministic `NextIntervalSeconds` / id hashing for both kinds.

## Acceptance criteria

- Unity compiles; existing proj→proj behavior unchanged (ChildKind defaults/sets Projectile).
- A proj→aoe loadout: each in-flight projectile spawns the child AOE on its interval over the
  projectile lifetime; AOEs apply damage; spawns stop when the projectile dies.
- No new per-frame structural changes; AOEs come from the pooled spawn-apply path.

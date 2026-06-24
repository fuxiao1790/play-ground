# 004 — Lingering-AOE-source ECS (aoe→proj, aoe→aoe)

**Scope:** Large. New tick system + spawner components + spawner archetype on AOEs.
**Dependencies:** 002 + the shared structs from 003. Independent of 003 otherwise.

## Changes

1. AOE spawner components
   [AoeEcsComponents.cs](Assets/Scripts/System/Aoe/AoeEcsComponents.cs):
   - `AoeIntervalSpawnerTag` (enableable or plain — plain tag + dedicated archetype is
     simplest, mirroring `ProjectileChildSpawnerTag`).
   - `AoeIntervalSpawnerComponent` holding `IntervalChildKind ChildKind`, `IntervalSeconds`,
     `IntervalJitterSeconds`, and **both** templates `IntervalProjectileChild` +
     `IntervalAoeChild` (only the active one populated — a source has one child kind).
   - `AoeIntervalSpawnStateComponent { float CooldownRemaining; int TickIndex; }`.

2. New tick system
   `Assets/Scripts/System/Aoe/TimedAoeSpawnSystem.cs`, modeled on
   [TimedProjectileSpawnSystem.cs](Assets/Scripts/System/Projectile/TimedProjectileSpawnSystem.cs):
   - `[UpdateInGroup(SimulationSystemGroup)]`, `[UpdateAfter(CombatLifetimeSystem)]`,
     `[UpdateBefore(AoeSpawnExpansionSystem)]` and `[UpdateBefore(ProjectileSpawnExpansionSystem)]`.
   - Query lingering AOEs (`AoeTag`, `Active`, `CombatLifetimeComponent`,
     `AoeIntervalSpawnerTag`). Tick the cooldown loop using `SystemAPI.Time.DeltaTime`; stop
     when lifetime is spent.
   - Per `ChildKind`: build a `ProjectileSpawnEvent` (radial 360° fan around the AOE center —
     see index.md item 5) → `ProjectileSpawnExpansionSystem.EventQueue`; **or** build an
     `AoeSpawnEvent` at the AOE center → `AoeSpawnExpansionSystem.EventQueue`. Forward each
     target system's `ProducerHandle`.
   - Reuse deterministic `NextIntervalSeconds` / id hashing (seed off `AoeIdentityComponent.AoeId`).

3. Spawn-apply archetype + reuse keying
   [AoeSpawnApplySystem.cs](Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs):
   - Add a **third archetype** = lingering archetype + `AoeIntervalSpawnerTag` +
     `AoeIntervalSpawnerComponent` + `AoeIntervalSpawnStateComponent`.
   - Extend `AoeSpawnKey` with a `hasIntervalSpawner` flag (so plain lingering AOEs aren't
     reused as spawner AOEs and vice-versa) and add a matching dead-slot query
     (`WithAll<AoeIntervalSpawnerTag>` vs `WithNone<AoeIntervalSpawnerTag>`).
   - Bake the spawner component (+ zeroed state) from the command when the flag is set, in
     both the reuse `AoeSpawnJob` and the cold-create `CreateAoeEntity` path.

## Acceptance criteria

- Unity compiles; existing AOE spawning (impact, on-hit, lingering pulses) unchanged — plain
  lingering AOEs still use the non-spawner archetype/pool.
- aoe→proj loadout: a lingering AOE emits projectiles on its interval over its lifetime
  (radial fan), projectiles apply damage; emission stops at AOE expiry.
- aoe→aoe loadout: a lingering AOE spawns child AOEs on its interval; children apply damage;
  no runaway recursion (child AOEs without their own spawner setup are plain).
- Profiler shows pooled reuse for both parent spawner AOEs and children.

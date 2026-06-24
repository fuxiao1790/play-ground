# 005 — One thin unified TimedSpawnSystem

## Scope

Replace `TimedProjectileSpawnSystem` and `TimedAoeSpawnSystem` with a single domain-agnostic
`TimedSpawnSystem` that ticks a cooldown and emits a stored event into the existing pipeline.
No template→event conversion, no fan-out logic (that stays in expansion).

## Changes

1. **New `Assets/Scripts/System/Common/TimedSpawnSystem.cs`** (`ISystem`),
   `[UpdateAfter(CombatLifetimeSystem)]`, `[UpdateBefore(ProjectileSpawnExpansionSystem)]`,
   `[UpdateBefore(AoeSpawnExpansionSystem)]`. Query `[WithAll(Active, CombatLifetimeComponent,
   TimedSpawnTag)]`, `Execute(ref TimedSpawnStateComponent state, in TimedSpawnComponent spawn,
   in CombatKinematicsComponent kin, in CombatLifetimeComponent life)`:
   ```
   if (life.Remaining <= 0f) return;
   cooldown = state.CooldownRemaining - dt; tick = state.TickIndex; guard = 0;
   while (cooldown <= 0f && guard++ < MaxTicksPerUpdate) {
       tick++;
       if (spawn.ChildKind == Aoe && HasAoeTemplates && aoeMap.TryGetValue(spawn.TemplateKey, out var e)) {
           Stamp(ref e, spawn, kin, tick); aoeQueue.Enqueue(e);
       } else if (projMap.TryGetValue(spawn.TemplateKey, out var p)) {
           Stamp(ref p, spawn, kin, tick); projQueue.Enqueue(p);
       }
       cooldown += math.max(MinIntervalSeconds, spawn.IntervalSeconds + Jitter(spawn, tick));
   }
   state.CooldownRemaining = cooldown; state.TickIndex = tick;
   ```
   `Stamp` sets only per-instance fields (`Faction`, `Position`, `BaseProjectileId`/`AoeId =
   SourceId`, `JitterSeed`, `DeterministicIdTickIndex`). Registry maps come from the singletons
   (`SystemAPI.TryGetSingleton`), passed `[ReadOnly]`; expansion `EventQueue`s as parallel
   writers, with `ProducerHandle` combined as the old systems did.

2. **Delete** `Assets/Scripts/System/Projectile/TimedProjectileSpawnSystem.cs` and
   `Assets/Scripts/System/Aoe/TimedAoeSpawnSystem.cs`.

3. **Loop guard**: keep `MaxTicksPerUpdate` cap + `math.max(MinIntervalSeconds, …)` clamp so a
   zero/negative interval can never hard-freeze the editor.

4. **Expansion unchanged**: the enqueued event is the existing type; `ProjectileSpawnExpansionSystem`
   / `AoeSpawnExpansionSystem` fan out and write commands exactly as today (fan-out params come
   from the stored event's `Count`/`SpreadDegrees`/`SpawnPatternType`).

## Acceptance criteria

- Interval children spawn at the configured interval/count with deterministic ids; proj→proj
  cadence matches the previous behavior.
- Only one tick system exists; the `ChildKind` branch appears once.
- A spawner with interval 0 does not freeze the editor (guard caps/clamps).

## Dependencies

004 (unified component). Expansion is untouched.

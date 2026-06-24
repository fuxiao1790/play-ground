# 004 — Unified TimedSpawnComponent + carriers

## Scope

Replace the per-domain spawner components with one self-describing `TimedSpawnComponent` (+ hot
state + tag), and carry it through the source's spawn event/command so apply can bake it.

## Changes

1. **Unified components** (`Assets/Scripts/System/Common/`):
   ```
   struct TimedSpawnComponent : IComponentData {
       CombatFaction Faction;
       int SourceId;
       IntervalChildKind ChildKind;
       Hash128 TemplateKey;
       float IntervalSeconds;
       float IntervalJitterSeconds;
       int JitterSeed;
   }
   struct TimedSpawnStateComponent : IComponentData { float CooldownRemaining; int TickIndex; }
   struct TimedSpawnTag : IComponentData { }
   ```

2. **Delete** the per-domain spawner components they replace:
   `ProjectileChildSpawnerComponent`, `AoeIntervalSpawnerComponent` (both `Projectile` and `Aoe`
   namespaces), `ProjectileChildSpawnStateComponent`, `AoeIntervalSpawnStateComponent`,
   `AoeIntervalSpawnerTag`. Update all references.

3. **Source carriers**: the source's spawn event/command carry one optional `TimedSpawnComponent`
   (replacing the prior `ChildSpawner`/`AoeSpawner`/`ChildSpawnState` fields):
   - `Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs` /
     `ProjectileSpawnRequest.cs` (`ProjectileSpawnEvent`, `ProjectileSpawnCommand`).
   - `Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs` (`AoeSpawnEvent`, `AoeSpawnCommand`).
   - `Assets/Scripts/System/Common/CombatRoot.cs` event builders set `TimedSpawnComponent` from
     the request (translator fills it from `setup.TemplateKey` + timer config + `SourceId`/faction).
   - Keep a `HasTimedSpawner` flag (or detect via `TemplateKey != default`) so apply selects the
     spawner archetype.

4. `SpawnerId` is already `JitterSeed`; ensure the unified component uses `JitterSeed`.

## Acceptance criteria

- `sizeof(ProjectileSpawnCommand)`/`sizeof(AoeSpawnCommand)` stay small (no embedded template);
  EditMode `sizeof(AoeSpawnCommand) < 4096` guard passes.
- No references to the deleted per-domain spawner components remain; project compiles.

## Dependencies

001 (Hash128 key type). Precedes 005, 006.

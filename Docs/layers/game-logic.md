# Game Logic

## Purpose

Own player-facing gameplay intent: skills, supports, triggers, loadouts,
cooldowns, stat resolution, mob behavior decisions, spawn rules, and combat
meaning before data is snapshotted for simulation.

## Owns

- Skill definitions, supports, trigger graphs, loadout slots, validators, and
  runtime skill compilation.
- `SkillDriver`, `SkillLoadout`, `SkillSetCompiler`, and
  `SkillSpawnTranslator`.
- Mob behavior state machines, triggers, event queues, and low-count AI choices.
- Spawn cap and spawn point rules before mobs become live actors.
- Player-facing meaning of damage, crit, stacks, detonations, and triggers.

## Does Not Own

- ECS entity allocation/reuse.
- Projectile/AOE movement, broad phase, narrow phase, lifetime, or render prep.
- Target proxy component layout.
- Presentation-time managed target companion resolution.

## Inputs

- Authored ScriptableObjects and prefab templates.
- Actor state, cooldown state, player input intent, mob events, and spawn
  configuration.
- Runtime stats from equipped sets and supports.

## Outputs

- Runtime skill definitions and plain-data spawn snapshots.
- `ProjectileSpawnRequest`, `AoeSpawnRequest`, and
  `ProjectileAoeSpawnRequest`.
- Mob movement/behavior intent and spawn requests for scene actors.

## Allowed Dependencies

- May depend on scene/authoring data.
- May produce [Skill Runtime Snapshots](../contracts/skill-runtime-snapshots.md).
- May produce [Spawn Requests](../contracts/spawn-requests.md).
- May call [Combat Root API](../contracts/combat-root-api.md) through actor or
  combat roots.

## Forbidden Dependencies

- Must not allocate projectile or AOE entities.
- Must not rely on in-flight ECS entities reading ScriptableObjects or prefabs.
- Must not embed managed object references in simulation snapshots.
- Must not widen damage result contracts with spawn-routing-only data.

## Main Systems / Modules

- `Assets/Scripts/Skills/`
- `Assets/Scripts/Mob/`
- `Assets/Scripts/Spawn/`
- `Assets/Scripts/Common/StatusEffects/`

## Related Contracts

- [Skill Runtime Snapshots](../contracts/skill-runtime-snapshots.md)
- [Spawn Requests](../contracts/spawn-requests.md)
- [Combat Root API](../contracts/combat-root-api.md)

## Related Flows

- [Skill To Combat Spawn](../flows/skill-to-combat-spawn.md)
- [Mob Spawn And Behaviour](../flows/mob-spawn-and-behaviour.md)
- [Collision To Combat Result](../flows/collision-to-combat-result.md)

## Notes / TODOs

- Detailed references:
  [skill-system.md](../reference/game-logic/skill-system.md),
  [mobs.md](../reference/game-logic/mobs.md),
  [mob-behaviour.md](../reference/game-logic/mob-behaviour.md),
  [spawn-system.md](../reference/game-logic/spawn-system.md).

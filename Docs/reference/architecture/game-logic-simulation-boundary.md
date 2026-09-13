# Game Logic And Simulation Boundary

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

This page describes where cross-cutting features belong when they touch both
game logic and ECS simulation. Skills are the main example: they are authored
and composed as game logic, then snapshotted into simulation data before
projectiles, AOEs, damage, status, and VFX run in ECS.

## Rule Of Thumb

Game logic owns player-facing intent and authored composition.

Simulation owns high-count runtime facts and scalable consequences.

The boundary between them is a plain-data snapshot:

```text
authoring/game logic
  -> runtime compile/translation
  -> spawn event snapshot
  -> ECS expansion/apply/simulation
  -> compact presentation result
  -> GameObject presentation/lifetime
```

Do not let ECS systems pull from live skill objects, ScriptableObjects,
prefabs, Transforms, or managed target callbacks. Do not make game logic allocate
projectile or AOE entities directly.

## What Belongs Where

| Concern | Game logic docs | Simulation docs |
|---|---|---|
| Player-facing concepts | Skills, supports, triggers, loadouts, mobs, spawn rules, input, cooldowns, difficulty intent. | None, except the simulation contract needed to support them. |
| Authoring data | ScriptableObjects, prefabs as authoring templates, validators, skill loadout state, mob behavior config. | Baked type ids, type registries, ECS component shapes, proxy data, native containers. |
| Composition rules | Which supports can modify which skills, trigger graph rules, stacking detonation authoring, stat aggregation. | How compiled intent is represented as events, commands, hit payloads, status data, and timed-spawn templates. |
| Runtime orchestration | `SkillDriver`, cooldown gates, compile on equipment/stat change, root input fire. | System ordering, event drains, expansion, apply, movement, collision, status processing, render preparation. |
| Translation boundary | Stateless translators that convert compiled game logic into spawn requests/events. | Event and command contracts consumed by ECS systems. |
| Collision and damage | Design meaning of damage, crit, stack, trigger, and status effects. | Target proxies, hit qualification, damage/status aggregation, consequence event emission. |
| Presentation | Actor roots, animation, hurt flashes, UI/debug text, death object lifetime. | Compact combat tick/status results and VFX spawn requests ready for presentation systems. |
| Performance limits | Design budgets and player-facing fallback rules. | Pooling, enableable components, Burst jobs, spatial hashing, batch rendering, native queue/stream ownership. |

## Skills Example

Skill docs should describe:

- skill, support, trigger, loadout, and slot concepts
- compatibility and validation rules
- stat aggregation and cooldown ownership
- compile-time conversion from authored sets to runtime definitions
- trigger graph behavior and player-facing semantics
- which skill effects are allowed to exist

Simulation docs should describe:

- the spawn event shape produced by compiled skills
- how impact, interval, stack, and on-hit follow-up effects are snapshotted
- how events become commands and commands become reusable ECS entities
- which ECS components carry damage, crit, status, collision, render, and VFX
  data
- how collision produces damage/status/spawn/VFX consequences
- frame order, pooling, despawn, and performance rules

The skill system may say "an impact trigger fires an AOE when this projectile
hits." The simulation docs say how that becomes an `AOE variant spawn event`, how it is
expanded into `AoeSpawnCommand`, and when the spawned AOE can collide.

## Bridge Documents

Some docs are intentionally boundary docs:

- [game-logic/skill-gameplay-system.md](../game-logic/skill-gameplay-system.md):
  skill authoring, composition, compilation, cooldowns, and trigger semantics.
- [simulation/skill-ecs-simulation.md](../simulation/skill-ecs-simulation.md):
  copied skill snapshots, root-cast gate, ECS skill domains, and template keys.
- [simulation/spawn-template-registry.md](../simulation/spawn-template-registry.md): plain-data
  snapshot rules for requests, events, commands, runtime components, timed
  templates, and consequence events.
- [simulation/project-aoe-system-common.md](../simulation/project-aoe-system-common.md):
  shared ECS spawn, target proxy, reuse, consequence, render, VFX, and frame
  timing rules.
- [simulation/mob-combat-state-ecs.md](../simulation/mob-combat-state-ecs.md):
  ECS-owned health/status direction and GameObject presentation sync boundary.

When adding a cross-cutting feature, update both sides if the feature changes
both design semantics and ECS runtime contracts.

## Update Checklist

When a feature touches game logic and simulation:

- Update game logic docs for authored meaning, validation, composition, and
  player-facing behavior.
- Update simulation docs for data contracts, components, systems, frame order,
  pooling, and consequences.
- Update spawn-template-registry docs when live authored data becomes copied runtime data.
- Update architecture or this boundary doc when ownership changes.
- Keep examples consistent across skill, projectile, AOE, status, and VFX docs.

## Anti-Patterns

- Explaining ECS component layout only in a game logic doc.
- Explaining player-facing skill semantics only in a simulation doc.
- Letting a simulation job read ScriptableObjects, prefabs, GameObjects,
  Transforms, or managed target companions.
- Letting game logic bypass event -> command -> apply and create combat
  entities directly.
- Mixing visual-only VFX requests with damage or status authority.
- Treating `Active`, common combat components, or `CombatScope` membership as a
  domain marker.

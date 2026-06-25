# Simulation ECS Overview

All docs in `Docs/` are design references. They describe current implementation
intent and should be checked against code before large changes.

This page is the entry point for the combat simulation ECS. It gives the short
map for each ECS aspect and links to the detailed files that hold deeper runtime
notes.

For features that touch both authored game logic and ECS runtime, use
[game-logic.md](../../layers/game-logic.md) and
[ecs-simulation.md](../../layers/ecs-simulation.md) to decide which doc owns
which aspect.

## Core Shape

The simulation layer handles high-count combat data with Unity Entities/DOTS.
Scene objects still own authored Unity behavior, object lifetime, presentation,
and low-count actor movement.

Use this split:

- GameObjects own player, mobs, spawners, authored roots, camera, walls, and
  Unity presentation.
- ECS owns projectiles, AOEs, future beams, target proxy data, collision,
  combat-state aggregation, transient VFX requests, slot reuse, and render
  preparation.
- `CombatRoot` is the managed bridge. It submits spawn intent, owns authoring
  registration, owns render/VFX resources, and binds target registries.
- ECS systems own expansion, apply, movement, collision, consequence events,
  health/status application, and presentation-ready buffers.

## ECS Aspects

| Aspect | Overview | Detail |
|---|---|---|
| Shared combat runtime | Common scope, faction, target proxy, spawn event -> command -> apply flow, reuse, despawn, rendering, VFX, and frame timing rules. | [project-aoe-system-common.md](./project-aoe-system-common.md) |
| Snapshotting | Spawn safety rules for managed requests, ECS events, commands, runtime component snapshots, timed-spawn templates, and consequence events. | [snapshotting.md](./snapshotting.md) |
| Projectiles | High-count moving attacks, tracking, timed spawns, impact spawns, collision, lifetime, render data, and projectile-specific reuse. | [projectile-system.md](./projectile-system.md) |
| AOEs | Pulse and lingering areas, repeat-hit gates, projectile bursts from AOE hits, lifetime, pulse VFX, and AOE-specific reuse. | [aoe-system.md](./aoe-system.md) |
| Combat state and status | ECS-owned target health/status direction, damage aggregation, compact presentation sync, and mob GameObject presentation boundary. | [mob-combat-state-ecs.md](./mob-combat-state-ecs.md) |
| VFX requests | Native VFX request flow from simulation jobs to presentation dispatch and Visual Effect Graph buffer contracts. | [vfx-system.md](./vfx-system.md) |
| ECS performance patterns | Structural change costs, enableable components, high-churn pooling, locality, reuse scheduling, and known design issues. | [ecs-notes.md](./ecs-notes.md) |

## Current Frame Path

1. Actor roots push target proxy position and shape before simulation.
2. Lifetime, timed-spawn, projectile, AOE, status, and collision systems run in
   ECS.
3. Collision systems emit plain data consequences, not managed callbacks.
4. Combat apply/finalize systems aggregate health, status, and result data.
5. Spawn expansion systems drain managed and ECS-produced events into commands.
6. Apply systems reuse disabled slots before cold-creating overflow entities.
7. Render preparation writes batched sprite matrices.
8. Presentation systems dispatch combat results, VFX, and render batches.

Newly applied projectiles and AOEs do not move or collide until the next
simulation update because apply runs after collision.

## Change Rules

- Keep this `index.md` as overview only.
- Put detailed runtime behavior in the specific aspect doc.
- Keep spawn intent separate from allocation intent.
- Keep hot despawn as enable/disable, not destroy/create.
- Require domain tags such as `ProjectileTag` and `AoeTag` on domain systems.
- Do not treat `Active`, common components, or scope membership as domain or
  faction.
- Do not read managed `TargetCompanion` from simulation jobs.
- Keep internal follow-up spawns in ECS event flow.

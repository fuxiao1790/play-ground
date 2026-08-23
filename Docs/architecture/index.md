# Architecture Documentation

This is the entry point for architecture docs. The structure separates ownership,
runtime sequences, boundary-crossing data, and decision history.

Authority rules:

- Layer docs own ownership and boundary rules.
- Flow docs describe cross-layer sequences and link back to owning layers.
- Contract docs define data, APIs, buffers, queues, and interfaces that cross a
  layer boundary.
- Decision records explain why major architectural choices were made.
- Overview files summarize and link. They do not redefine rules.

## Layers

- [Scene And Authoring](../layers/scene-and-authoring.md): Unity scenes,
  prefabs, MonoBehaviours, ScriptableObjects, Physics2D actors, camera, and
  authored presentation. See [UI Architecture](../ui.md) for the UI Toolkit
  structure and input-routing rules.
- [Game Logic](../layers/game-logic.md): skills, supports, loadouts, mob
  behavior, spawn rules, cooldowns, and player-facing combat intent.
- [Combat Bridge](../layers/combat-bridge.md): `CombatRoot`, target registries,
  target proxy creation, render/VFX resource registration, and managed spawn
  submission into ECS buffers.
- [ECS Simulation](../layers/ecs-simulation.md): high-count projectile, AOE,
  targeted, status, target health, collision, spawn expansion/apply, pooling, and
  render preparation data.
- [Presentation And Feedback](../layers/presentation-and-feedback.md):
  presentation bridge, batched sprite submission, VFX dispatch, actor feedback,
  and debug display.

## Major Flows

- [Runtime Frame](../flows/runtime-frame.md)
- [Skill To Combat Spawn](../flows/skill-to-combat-spawn.md)
- [Resource Spend Gate](../flows/resource-spend-gate.md)
- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)
- [Collision To Combat Result](../flows/collision-to-combat-result.md)
- [Target Proxy Lifecycle](../flows/target-proxy-lifecycle.md)
- [VFX Dispatch](../flows/vfx-dispatch.md)
- [Skill Loadout Edit](../flows/skill-loadout-edit.md)
- [Mob Spawn And Behaviour](../flows/mob-spawn-and-behaviour.md)

## Major Contracts

- [Combat Root API](../contracts/combat-root-api.md)
- [Spawn Requests](../contracts/spawn-requests.md)
- [Spawn Events And Commands](../contracts/spawn-events-and-commands.md)
- [Target Proxy](../contracts/target-proxy.md)
- [Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md)
- [Skill Runtime Snapshots](../contracts/skill-runtime-snapshots.md)
- [Skill Loadout Editing](../contracts/skill-loadout-editing.md)
- [Player Save Data](../contracts/player-save-data.md)
- [Game Settings Data](../contracts/game-settings-data.md)
- [VFX Requests](../contracts/vfx-requests.md)
- [Render Batch Data](../contracts/render-batch-data.md)

## Decisions

- [ADR-001: Hybrid GameObject And ECS Runtime](../decisions/adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-002: Plain Data Snapshot Boundary](../decisions/adr-002-plain-data-snapshot-boundary.md)
- [ADR-003: Event Command Spawn Pipeline](../decisions/adr-003-event-command-spawn-pipeline.md)
- [ADR-004: Target Proxy Collision](../decisions/adr-004-target-proxy-collision.md)
- [ADR-005: Enableable Pooling For Combat Entities](../decisions/adr-005-enableable-pooling-for-combat-entities.md)
- [ADR-006: ECS Aggregated Combat Results](../decisions/adr-006-ecs-aggregated-combat-results.md)

## Reference Docs

Detailed pre-reorganization notes are preserved under
[../reference/](../reference/). Treat the layered architecture docs as
authoritative when a reference note repeats an ownership rule.

Critical shared-VFX area-size constraint:
[Shared VFX Graph Area-Size Corruption](../reference/simulation/vfx-shared-graph-area-size-corruption.md).

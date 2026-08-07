# Data Flow Overview

This is a map of major project flows. It is not the source of truth for
ownership; use the linked layer and contract docs for rules.

## Runtime Frame

[Runtime Frame](../flows/runtime-frame.md) starts with actor roots pushing target
proxy data, runs ECS lifetime, timed spawn, movement, collision, combat result
finalization, spawn expansion/apply, render preparation, and ends with
presentation bridge work.

Key layers:
[Scene And Authoring](../layers/scene-and-authoring.md),
[ECS Simulation](../layers/ecs-simulation.md),
[Presentation And Feedback](../layers/presentation-and-feedback.md).

## Skill To Combat Spawn

[Skill To Combat Spawn](../flows/skill-to-combat-spawn.md) converts authored
skills, supports, triggers, and loadouts into runtime definitions, then into
managed spawn requests submitted through `CombatRoot`.

Key contracts:
[Skill Runtime Snapshots](../contracts/skill-runtime-snapshots.md),
[Spawn Requests](../contracts/spawn-requests.md),
[Combat Root API](../contracts/combat-root-api.md).

## Skill Loadout Edit

[Skill Loadout Edit](../flows/skill-loadout-edit.md) keeps runtime equipment in
Game Logic: UI Toolkit issues a command, `SkillDriver` validates, stages, and
atomically swaps a runtime loadout clone. Future casts use its compiled roots;
existing combat entities keep copied snapshots.

Key contract: [Skill Loadout Editing](../contracts/skill-loadout-editing.md).

## Spawn Event To Entity

[Spawn Event To Entity](../flows/spawn-event-to-entity.md) keeps gameplay intent
separate from allocation intent: requests become spawn events, expansion systems
produce one-entity commands, and apply systems reuse disabled slots before cold
creation.

Key contract:
[Spawn Events And Commands](../contracts/spawn-events-and-commands.md).

## Collision To Combat Result

[Collision To Combat Result](../flows/collision-to-combat-result.md) takes
projectile and AOE collision plus targeted chain resolve against target proxies,
emits plain data hit and consequence events, finalizes health/status in ECS, and
exposes compact presentation results.

Key contracts:
[Target Proxy](../contracts/target-proxy.md),
[Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md).

## Target Proxy Lifecycle

[Target Proxy Lifecycle](../flows/target-proxy-lifecycle.md) bridges
GameObject actors to ECS collision and combat state without letting simulation
jobs read live Unity objects.

Key contract:
[Target Proxy](../contracts/target-proxy.md).

## AOE VFX Dispatch

[VFX Dispatch](../flows/vfx-dispatch.md) carries visual-only VFX requests from
simulation jobs through scope buffers to presentation-time VFX Graph dispatch.

Key contract:
[VFX Requests](../contracts/vfx-requests.md).

## Mob Spawn And Behaviour

[Mob Spawn And Behaviour](../flows/mob-spawn-and-behaviour.md) covers spawner
coordination, mob prefab creation/reuse, low-count Physics2D movement, behavior
state, and combat target registration.

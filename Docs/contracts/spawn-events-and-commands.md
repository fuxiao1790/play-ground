# Spawn Events And Commands

## Purpose

Define the ECS boundary between gameplay spawn intent and one-entity allocation
intent.

## Produced By

Events are produced by [Combat Bridge](../layers/combat-bridge.md) and internal
[ECS Simulation](../layers/ecs-simulation.md) producers. Commands are produced
by expansion systems.

## Consumed By

Events are consumed by expansion systems. Commands are consumed by apply
systems.

## Fields / Shape

Event types:

- `ProjectileSpawnEvent`
- `ImpactAoeSpawnEvent`
- `LingeringAoeSpawnEvent`

Command types:

- `ProjectileSpawnCommand`
- `AoeSpawnCommand`

An event is a slim link into the spawn-template registry plus a per-instance
frame: spawn kind, template key (`Hash128`), position, aim / base direction,
faction, source id, jitter seed, deterministic tick index, and contact-gate seed
target. The event carries no spawn data of its own.

Commands carry the resolved spawn data. A command describes exactly one ECS
entity with position, velocity, bounds, identity, hit payload, lifetime, render
state, and optional timed-spawn state. Expansion produces commands by
dereferencing the event's template key against the registry, applying the
instance frame, and exploding template-level multiplicity (count, spread, jitter)
into one command per spawned entity.

Projectile expansion fans commands into discrete and swept command lists on
`ProjectileSpawnEventSingleton`, using authored `ProjectileSpawnCommand.SweptCollision`.
This split is at command level, not event level: interval and on-hit producers only carry a
template key; expansion is where the template resolves the lane discriminator.

See [spawn-template-registry.md](../reference/simulation/spawn-template-registry.md)
for the registry concurrency contract and the `(kind, key)` model.

AOE variant is decided at authoring from child lifetime and carried on
`IntervalChildKind` / `StackDetonationKind`: `Lifetime > 0` routes to
lingering AOE, otherwise impact AOE. Producers route by that carried kind and
do not inspect templates at runtime.

## Guarantees

Events are gameplay intent (a registry link + instance frame). Commands are
allocation intent (resolved data). Expansion owns the registry dereference and
spawn math. Apply owns reuse and cold creation. The registry is written only by
external (pre-tick) spawns and is read-only and immutable during the simulation
tick.

## Restrictions

Do not create projectile/AOE entities directly from managed gameplay,
collision, status, or timed-spawn code. Do not put managed references in event
or command payloads.

## Lifetime

Events live until drained from scope buffers or native queues. Commands live
until consumed by apply systems in the same spawn phase.

## Ordering

Producer jobs must complete before expansion drains native queues. Apply runs
after expansion and after current-frame collision.

## Sibling Event Pipeline

Target-proxy lifecycle uses a sibling scope-buffer event-to-apply pipeline:
`TargetProxyCreateApplySystem`, `TargetProxyUpdateApplySystem`, and
`TargetProxyDeleteApplySystem` consume proxy events instead of spawn expansion.
See [Target Proxy](target-proxy.md) and
[Target Proxy Lifecycle](../flows/target-proxy-lifecycle.md).

## Related Layers

- [Combat Bridge](../layers/combat-bridge.md)
- [ECS Simulation](../layers/ecs-simulation.md)

## Related Flows

- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)
- [Skill To Combat Spawn](../flows/skill-to-combat-spawn.md)
- [Collision To Combat Result](../flows/collision-to-combat-result.md)

## Notes / TODOs

Entity reuse is faction-agnostic and render-kind-agnostic within a matching
archetype. A disabled slot of any faction may be reused by a spawn of any other
faction in that archetype. Discrete and swept projectile pools are separate.
Per-entity faction is stamped from `event.Faction` at expansion time,
and per-entity `CombatRenderKindId` is overwritten from the spawn command's
`RenderTypeId`.

Detailed references:
[spawn-template-registry.md](../reference/simulation/spawn-template-registry.md) and
[project-aoe-system-common.md](../reference/simulation/project-aoe-system-common.md).

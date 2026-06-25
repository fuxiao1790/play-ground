# ADR-002: Plain Data Snapshot Boundary

## Status

Accepted

## Context

Combat effects can outlive the authored object or skill state that created them.
Simulation jobs also need Burst-compatible data without managed references.

## Decision

Copy authored/game-logic intent into plain runtime snapshots before ECS
simulation owns it.

## Consequences

In-flight entities do not read ScriptableObjects, prefabs, GameObjects,
Transforms, Colliders, or managed callbacks. Runtime changes require new
snapshots or new spawns. Snapshot contracts must stay small and explicit.

## Alternatives Considered

- Let ECS systems read ScriptableObjects or prefabs: rejected because it breaks
  Burst/job safety and lifetime boundaries.
- Store recursive child data as managed object graphs: rejected because child
  chains must remain bounded and plain-data.

## Related Docs

- [Skill Runtime Snapshots](../contracts/skill-runtime-snapshots.md)
- [Skill To Combat Spawn](../flows/skill-to-combat-spawn.md)
- [snapshotting.md](../reference/simulation/snapshotting.md)

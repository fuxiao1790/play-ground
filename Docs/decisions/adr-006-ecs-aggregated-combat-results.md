# ADR-006: ECS Aggregated Combat Results

## Status

Accepted

## Context

Plain damage and status can produce many raw hits against a small target set.
Managed per-hit replay scales with hit count and becomes the wrong boundary as
projectile/AOE counts rise.

## Decision

Aggregate direct damage and status in ECS by target proxy and expose compact
`CombatTickResult` data to the presentation bridge.

## Consequences

Managed result delivery scales closer to changed target count. Per-hit authored
side effects must stay in ECS consequence paths or use separate semantic events.
Older docs that describe per-hit `DamageReplayEvent` behavior need cleanup.

## Alternatives Considered

- Keep one managed callback per raw hit: rejected for scaling.
- Move all mob behavior to ECS: rejected as broader than needed.

## Related Docs

- [Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md)
- [Collision To Combat Result](../flows/collision-to-combat-result.md)
- [mob-combat-state-ecs.md](../reference/simulation/mob-combat-state-ecs.md)

# Task 001 Execution Packet

## Goal

Document the final player-skill UI contract before runtime or UI code changes.

## Allowed Changes

- New player-skill UI UX, contract, and flow docs.
- Documentation indexes and the skill-system reference where they point to the
  previous alternating-slot topology.
- Implementation log.

## Required Facts

- One normalized ordered node list; node has `skillSet` plus `triggerToNext`.
- Runtime clone in `SkillDriver` is the sole mutable equipment state.
- UI sends commands, never writes loadout state or calls compiler/ECS.
- V1 UI count cap is presentation-only, no pause, and changes affect future
  casts only.

## Validation

- Check all new docs are linked from their authority locations.
- Check command/event ordering, node state table, Mermaid sequence, save DTO,
  input behavior, and unbounded-core statement are present.

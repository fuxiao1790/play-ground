# Implementation Context

## Architectural Decisions
- `OnHitTrigger` replaces four subtype-specific on-hit links. It has no fields beyond `TriggerLink`.
- A trigger says when; compiled source and target types decide runtime slot; child skill set says what spawns.
- `IntervalSpawnTrigger` remains time/energy-driven but loses child-effect attributes.
- Do not wire targeted skills as on-hit sources or alter stack/expire trigger behavior.

## Global Invariants
- One outgoing trigger per node; on-hit ref priority stays projectile, AOE, targeted.
- Attach and stamp incoming mana factors only when runtime attachment succeeds.
- Triggered children compile without triggered mana aggregation; root aggregates once.
- Child count, spread, echo count, and scatter resolve in its own definition and supports.
- Compilation stays forward-only; spawn-chain depth accounting stays in `SkillDriver`.

## Ownership Boundaries
- `TriggerLink` owns link UI and mana-cost factors.
- `OnHitTrigger` owns no effect attributes.
- `IntervalSpawnTrigger` retains only `energyPerSecond` and energy conversion.
- Runtime/ECS and spawn-template path shapes remain unchanged except explicitly narrowed or deleted setup fields.

## Data Flow
- `SkillSetCompiler` compiles child, attaches it based on compiled source/target types, then stamps mana if attached.
- `SkillDriver` walks runtime on-hit fields and registers templates without trigger-subtype awareness.

## Lifecycle / Allocation Rules
- No lifecycle, allocation, ECS system, or job changes.

## ECS / Job / Threading Constraints
- Preserve existing runtime definitions and template registration flow; no ECS path redesign.

## Determinism Requirements
- Preserve `SideSpray` interval projectile behavior and existing child jitter handling.

## Producer / Consumer Separation
- No combat event, command, or result contract changes.

## Reused Mechanisms
- Generic tag validation handles on-hit source/target legality.
- Existing runtime type dispatch and child-definition fallbacks provide all needed behavior.

## Introduced Mechanisms
- `OnHitTrigger` and `AttachOnHitTarget` runtime field attachment helper.

## Validation Requirements
- Use static searches and source inspection only; project policy defers Unity test execution to user.
- User must run affected EditMode/PlayMode tests and provide `Logs/TestResults-EditMode.xml` and `Logs/TestResults-PlayMode.xml` before tests can be reported passing.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Skills/Trigger/*`, `SkillSetCompiler`, `SkillLoadoutValidator`, runtime definitions, `SkillDriver`.
- EditMode skill validation/continuous-authoring tests, AOE PlayMode test.
- `Docs/reference/game-logic/skill-system.md`, `Docs/folder-structure.md`.

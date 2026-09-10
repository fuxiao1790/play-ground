# Task Execution Packet

## Task

005-doc-updates.md

## Goal

Document unified `OnHitTrigger` and child-owned interval attributes; remove legacy on-hit names from project docs.

## Files Allowed To Modify

- `Docs/reference/game-logic/skill-system.md`
- `Docs/folder-structure.md`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- Current trigger and interval documentation around specified sections; runtime AOE declaration.

## Behavior To Preserve

- Existing stacking and legacy-slot documentation drift remains untouched.
- SideSpray, stationary-forward, echo placement, and projectile nesting caveats remain documented.

## Behavior To Change

- Four old on-hit sections become one dispatch-matrix section.
- Interval trigger fields become only `energyPerSecond`; child definitions own burst/spread/echo/scatter.

## Relevant Global Context

- Trigger says when; skill set says what; `TriggerLink` prices link. AOE on-hit field now has `RuntimeAoeDefinition` type.

## Dependencies Confirmed

- Tasks 001, 002, and 006 complete and Unity compilation succeeded.

## Step-By-Step Instructions

1. Merge on-hit subsections and pseudocode into `OnHitTrigger` dispatch description.
2. Relocate interval targeted paragraph; remove trigger-owned interval values.
3. Replace old names in examples/diagrams; update folder listing.

## Acceptance Criteria

- No legacy on-hit names in `Docs/`; ownership rule appears once; interval spawner mechanics and valid child fields remain explained.

## Validation Required

- Doc searches and source type inspection.

## Hard Boundaries

- Do not repair stale stacking or legacy-slot-model documentation.

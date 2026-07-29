# Task Execution Packet

## Task

004-mana-cost-docs-update.md

## Goal

Update the skill-system and resource-spend documentation to exactly describe the completed support interface families and trigger mana factor.

## Files Allowed To Modify

- `Docs/reference/game-logic/skill-system.md`
- `Docs/flows/resource-spend-gate.md`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- The two allowed docs, `ModifierKindInterfaces.cs`, migrated supports, and `TriggerLink.cs`.

## Behavior To Preserve

- Unrelated documentation structure and the existing stat fold explanation.

## Behavior To Change

- Replace bare generic modifier interface documentation with the exact seven shipped families and their nested kinds.
- Document explicit implementations for same-signature cross-stat contributions.
- Document the resolved trigger factor and revise the support table.

## Relevant Global Context

- Task 001 completed with seven interface families and ten nested interfaces total.
- Task 002 completed with `ResolveManaCostFactor()` returning `(1 + manaCostIncreasedPercent) * manaCostMultiplier`.

## Dependencies Confirmed

- The shipped source types and fields named above exist.

## Step-By-Step Instructions

1. Update only the requested sections of `skill-system.md`: trigger paragraph, interface block, explicit-interface paragraph, and augment-support table.
2. Update resource-spend step 1 to reference each link's resolved factor and helper.
3. Match source exactly; retain behavior-interface tail and unrelated formula text.

## Acceptance Criteria

- No bare modifier interface reference remains in `skill-system.md`.
- Interface block precisely matches source family/kind coverage.
- Table uses fully qualified interface names for every row.
- Trigger factor formula and resource-spend flow are accurate.

## Validation Required

- Search/compare docs against shipped source; inspect diff for scope.

## Hard Boundaries

- Do not modify code, tests, assets, or unrelated documentation sections.
- Do not document earlier rejected designs.

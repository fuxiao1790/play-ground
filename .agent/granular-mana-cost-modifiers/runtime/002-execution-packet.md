# Task Execution Packet

## Task

002-trigger-link-mana-cost-fold.md

## Goal

Add trigger-link increased mana cost and resolve it with the existing multiplier at every trigger-cost call site, renaming the runtime stored factor for accuracy.

## Files Allowed To Modify

- `Assets/Scripts/Skills/Trigger/TriggerLink.cs`
- `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeSkillDefinition.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- The four allowed files and trigger-cost tests in `SkillValidationEditModeTests.cs` as read-only precedent.

## Behavior To Preserve

- Existing trigger-tree composition and all costs when `manaCostIncreasedPercent` is its neutral `0f`.
- Existing serialized multiplier aliases.

## Behavior To Change

- Every edge factor becomes `(1 + manaCostIncreasedPercent) * manaCostMultiplier`.
- The internal runtime name becomes `IncomingManaCostFactor`.

## Relevant Global Context

- Trigger-edge cost is intentionally outside `StatModifierAccumulator`; do not route it through the support stat fold.
- This is a scalar per valid trigger edge, clamped as the task specifies.

## Dependencies Confirmed

- None; task 002 is independent of 001.

## Step-By-Step Instructions

1. Add neutral `manaCostIncreasedPercent` and `ResolveManaCostFactor()` next to the existing multiplier.
2. Apply the helper in `IntervalSpawnTrigger`.
3. Rename `IncomingManaCostMultiplier` to `IncomingManaCostFactor` and update every compiler read/write.
4. Store the clamped resolved link factor in the compiler.

## Acceptance Criteria

- Default increased percentage preserves existing multiplier-only behavior.
- No `IncomingManaCostMultiplier` remains in `Assets/Scripts/`.
- Trigger and interval cost use the resolved factor.

## Validation Required

- Search for stale runtime property and direct old trigger factor usages.
- Relevant Unity EditMode tests when project access permits.

## Hard Boundaries

- Do not modify support interfaces, add tests, or update docs in this task.
- Do not introduce a different trigger data model or accumulator path.

# Task Execution Packet

## Task
001-stacktrigger-owns-stack-config.md

## Goal
Move stack accrual authoring and `RuntimeStackingDetonation` construction to `StackTrigger`; remove support-only hierarchy and checks.

## Files Allowed To Modify
- `Assets/Scripts/Skills/Trigger/StackTrigger.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillLoadoutCompiler.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`
- `Assets/Scripts/Skills/SkillValidationWarning.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- `Assets/Scripts/Skills/Support/StackingSupport.cs`
- `Assets/Scripts/Skills/Support/StackingSupport.cs.meta`
- `Assets/Scripts/Skills/Support/ConversionSupport.cs`
- `Assets/Scripts/Skills/Support/ConversionSupport.cs.meta`

## Files Likely Needed For Reading
- Files above, `RuntimeStackingDetonation`, `RuntimeSkillDefinition`, related triggers, `SkillDriver` mana/key paths, and relevant tests only for interface evidence.

## Behavior To Preserve
- Registration-minted per-wrapper debuff keys.
- Outgoing trigger links attach to inner compiled detonation.
- Existing incoming-trigger mana aggregation.
- Driver wrapper traversal.

## Behavior To Change
- Accrual fields live on `StackTrigger`, defaults `3 / 4f / 1`, clamped at compile.
- `StackTrigger` targets only `Projectile | Aoe`; generic validation emits `UnsupportedTriggerTarget` for targeted targets.
- Unwired former detonation sets compile as roots.

## Relevant Global Context
- Compile target, construct wrapper, apply incoming mana factor, attach wrapper—in that order.
- No new abstractions or compatibility marker.

## Dependencies Confirmed
- Task 001 has no prerequisites.
- Required source files and deleted support hierarchy exist.

## Step-By-Step Instructions
- Follow all six edits and acceptance criteria in `001-stacktrigger-owns-stack-config.md` exactly.

## Acceptance Criteria
- No non-deleted source references to removed support types or obsolete APIs.
- Required compiler shape, tags, root behavior, validation behavior, and mana ordering remain as specified in task file.

## Validation Required
- Static/source checks only. Do not run Unity tests; name requested tests for user and require XML results under `Logs/`.

## Hard Boundaries
- Modify only listed files and direct compile fixes.
- Do not modify tests, assets, catalogs, docs, or later-task files.
- Do not change architecture or introduce abstractions.

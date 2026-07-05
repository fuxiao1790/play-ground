# Task Execution Packet

## Task
002-classify-at-authoring.md

## Goal
Classify AOE variant at managed authoring sites using child lifetime.

## Files Modified
- Assets/Scripts/Skills/PlayerSkillDriver.cs
- Assets/Scripts/Skills/SkillSpawnTranslator.cs
- Assets/Scripts/System/Common/CombatRoot.cs

## Dependencies Confirmed
- `AoeVariant.AoeChildKindFor` and `AoeVariant.AoeDetonationKindFor` exist.

## Acceptance Result
- Complete.

## Validation
- Search confirms no `IntervalChildKind.Aoe` or `StackDetonationKind.Aoe` remains in scripts/tests.

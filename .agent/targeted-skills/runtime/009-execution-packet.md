# Task Execution Packet

## Task

009-compiler-runtime-definitions.md

## Goal

Compile targeted authoring definitions into runtime definitions, register targeted types/VFX, and register targeted spawn templates with stable runtime values.

## Files Allowed To Modify

- `Assets/Scripts/Skills/Runtime/RuntimeTargetedDefinition.cs` (new)
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- Directly related EditMode compiler tests.

## Files Allowed To Create

- `Assets/Scripts/Skills/Runtime/RuntimeTargetedDefinition.cs`
- Focused targeted compiler EditMode tests only.

## Behavior To Preserve

- Existing projectile/AOE compiler, registration, template hashing, and trigger behavior.
- `SkillDefinitionTags.Any` remains `Projectile | Aoe` until task 011.
- No editor asset/YAML changes.

## Behavior To Change

- `TargetedDefinition` compiles to a single-hit `RuntimeTargetedDefinition`; lingering definitions carry positive lifetime/tick values.
- Acquire and chain radii fold through `SkillStat.AreaSize`; visual size/width do not.
- Targeted type/VFX registration and targeted command template registration run beside existing projectile/AOE registration.
- Single-hit delayed chains receive a non-zero fail-safe lifetime.

## Relevant Global Context

- Runtime code may retain managed authoring references; ECS jobs receive only baked command data.
- Targeted commands require `TargetedVfxIds`, `TargetedVfxSizeComponent`, `TargetedResolveConfig`, render data, hit payload, and variant timing.
- Registry writes happen during managed pre-tick compilation; simulation reads maps read-only.

## Dependencies Confirmed

- Task 007 targeted type registry and `CombatRoot` targeted registration/template APIs exist.
- Task 008 targeted authoring definitions, prefab, and skill classes exist.

## Step-By-Step Instructions

1. Add `RuntimeTargetedDefinition` with authored/runtime values, VFX data, type-definition creation, and declared trigger slots.
2. Add targeted `BuildRuntime` compiler branch with shared stat folding and delayed-chain lifetime fail-safe.
3. Extend `SkillDriver` bind/compile registration passes and recursive traversal for targeted definitions.
4. Build/register targeted command templates, including render data, hit payload, resolve config, VFX, and timing.
5. Add focused static/EditMode coverage only for task-009 behavior; do not implement task-010 trigger links.

## Acceptance Criteria

- Targeted and lingering definitions compile with correct values and variant lifetime.
- Area-size modifiers scale both gameplay radii but not visual size/width.
- Damage/rate supports continue to affect targeted runtime values.
- Identical targeted templates hash identically; changing count changes the key.
- Delayed single-hit chains get a fail-safe lifetime of at least `maxTargets * chainDelaySeconds`.
- Sprite-less targeted definitions retain `RenderId == 0`.
- VFX assets map to the correct slots; unassigned assets map to zero.
- Existing compiler behavior remains unchanged.

## Validation Required

- Static diff/shape checks and `git diff --check`.
- Unity EditMode command for the user; agent must not run Unity tests per project overview.

## Hard Boundaries

- Do not implement task-010 trigger links, task-011 support widening, task-012 root cast translator, validation warnings, playmode tests, or authored assets.
- Do not modify ECS runtime systems or Unity YAML.
- Stop on architectural ambiguity.

---
name: update-tests
description: Update interval-spawn tests for the renamed trigger fields and add AOE scatterRadius coverage; keep the mismatch tests
---

# 004 — Update tests

## Scope

- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`

## Keep (do NOT delete)

Because both trigger types are retained with unchanged `TargetSkillTags`, the
two projectile-trigger-onto-AOE tests remain valid and stay:
- `ValidatorWarnsWhenProjectileIntervalTargetsAoeSkill` ([:41-58](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L41-L58)).
- `CompilerIgnoresProjectileIntervalTargetAoeSkill` ([:60-88](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L60-L88)).

## Field-name updates (behavior unchanged)

- `SkillValidationEditModeTests.cs`:
  - `CompilerPopulatesAoeIntervalSetupOnProjectileSource` ([:125-162](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L125-L162))
    — `trigger.spawnCount = 2;` -> `trigger.echoCount = 2;`. The existing
    `AoeIntervalSpawnSetup.Count == 3` assertion still holds (child echo 1 + 2).
  - Any other test setting `trigger.spawnCount` on a `ProjectileIntervalSpawnTrigger`
    -> `trigger.projectileCount`; on an `AoeIntervalSpawnTrigger` ->
    `trigger.echoCount`.
- `AoePlayModeTests.cs`:
  - `CompileAndRegisterAssignsDedupedIntervalTemplateKeys` ([:290-382](../../Assets/Tests/PlayMode/AoePlayModeTests.cs#L290-L382))
    — `triggerA.spawnCount` / `triggerB.spawnCount` are on
    `ProjectileIntervalSpawnTrigger` -> `projectileCount`.
  - `LingeringAoeIntervalChildrenUseRegistryTemplatesUntilSourceExpires` ([:384+](../../Assets/Tests/PlayMode/AoePlayModeTests.cs#L384))
    — `projectileTrigger.spawnCount` -> `projectileCount`;
    `aoeTrigger.spawnCount` -> `echoCount`.

## New coverage

Add one EditMode test asserting the AOE trigger's `scatterRadius` reaches the
runtime setup, e.g. `CompilerPopulatesAoeIntervalScatterRadius`:
- Build a projectile-source -> AOE-child chain with an `AoeIntervalSpawnTrigger`
  whose `scatterRadius = 2f`.
- Assert `runtime.AoeIntervalSpawnSetup.ScatterRadius == 2f`.
- If the reviewer chose **authoritative** scatter (default), also give the child
  AOE skill a nonzero authored `ScatterRadius` and assert the setup value equals
  the trigger's (child value ignored). If **additive**, assert it equals the sum.
  Match whichever 002 implemented.

Optionally extend `CompileAndRegisterAssignsDedupedIntervalTemplateKeys`-style
dedup coverage to confirm two AOE interval triggers differing only in
`scatterRadius` mint distinct `TemplateKey`s (mirrors the existing echo-count
dedup case). Optional — the field is already part of the hashed command.

## Acceptance Criteria

- Both test assemblies compile with no `.spawnCount` / `.sideSpreadDegrees`
  references on interval triggers.
- User runs EditMode + PlayMode suites in Unity (harness cannot run Unity) and
  confirms all interval tests pass, including the retained mismatch tests and
  the new scatter test.

## Dependencies

Depends on [001](./001-trigger-fields.md), [002](./002-runtime-setup-and-compiler.md),
[003](./003-template-scatter-threading.md).

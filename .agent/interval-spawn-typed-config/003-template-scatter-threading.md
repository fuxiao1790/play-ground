---
name: template-scatter-threading
description: Thread the interval trigger's scatterRadius through RegisterAoeIntervalTemplate into BuildAoeTemplate without regressing non-interval AOEs
---

# 003 — Thread scatter into the AOE interval template

## Scope

`Assets/Scripts/Skills/PlayerSkillDriver.cs`
(`SkillIntervalTemplateBuilder.BuildAoeTemplate` + `RegisterAoeIntervalTemplate`)

## Background

`BuildAoeTemplate` currently bakes `ScatterRadius = child.ScatterRadius`
([:722](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L722)). It has exactly
two callers:
- top-level / on-hit AOE registration ([:311](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L311)) — must keep `child.ScatterRadius`.
- interval AOE registration ([:404](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L404)) — must use the trigger-derived `setup.ScatterRadius`.

## Changes

### `BuildAoeTemplate` signature ([:676-682](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L676-L682))
Add a trailing optional override that defaults to the child's value:
```csharp
public static AoeSpawnCommand BuildAoeTemplate(
    RuntimeAoeDefinition child,
    int echoCount,
    CombatRoot root,
    StackEffectSnapshot stackEffect,
    OnHitSpawnRef onHitSpawn = default,
    TimedSpawnComponent timedSpawn = default,
    float? scatterRadiusOverride = null)
```
And at [:722](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L722):
```csharp
ScatterRadius = Mathf.Max(0f, scatterRadiusOverride ?? child.ScatterRadius),
```
The top-level caller ([:311](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L311))
is left unchanged (passes no override -> keeps `child.ScatterRadius`).

### `RegisterAoeIntervalTemplate` ([:395-412](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L395-L412))
Pass the setup's scatter into the builder:
```csharp
AoeSpawnCommand template =
    SkillIntervalTemplateBuilder.BuildAoeTemplate(
        child,
        Mathf.Max(1, setup.Count),
        combatRoot,
        stackEffect,
        BuildOnHitSpawnRef(child),
        scatterRadiusOverride: setup.ScatterRadius);
```
(No `timedSpawn` is passed here today; keep that positional gap by using the
named `scatterRadiusOverride:` argument as shown.)

## Notes / invariants

- Content hashing for template dedup includes `ScatterRadius` (it is a field of
  the hashed `AoeSpawnCommand`), so two AOE interval triggers that differ only
  in `scatterRadius` correctly mint distinct `TemplateKey`s — consistent with
  the existing `spawnCount`/echo dedup behavior asserted in
  `AoePlayModeTests.CompileAndRegisterAssignsDedupedIntervalTemplateKeys`.
- No ECS/job change: `AoeSpawnCommand.ScatterRadius` is already consumed by the
  expansion job; this only changes the baked value.

## Acceptance Criteria

- An `AoeIntervalSpawnTrigger` with `scatterRadius > 0` produces interval AOE
  echoes scattered within that radius (verifiable via the expansion job's
  `command.ScatterRadius` branch); `scatterRadius = 0` overlaps at center.
- Top-level and on-hit AOEs still bake `child.ScatterRadius` (no regression).
- Project compiles.

## Dependencies

Depends on [002-runtime-setup-and-compiler.md](./002-runtime-setup-and-compiler.md)
(consumes `RuntimeAoeIntervalSpawnSetup.ScatterRadius`).

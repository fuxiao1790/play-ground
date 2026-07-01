# 002 — Authoring + Runtime + Compiler

Rename the AOE `count` concept to `echoCount` and add `scatterRadius` across the
authoring definition, the behavior context, the runtime definition, and the
compiler that folds them. Mirrors the projectile `count`/`spreadDegrees` fields.

## Changes

### `SkillDefinition.cs` — `AoeDefinitionBase`
- Rename `[Min(1)] public int count = 1;` → `[FormerlySerializedAs("count")] [Min(1)] public int echoCount = 1;`
  (preserve values already authored on `.asset` files).
- Add `[Min(0f)] public float scatterRadius = 0f;`.
- `MemberwiseClone`-based `DeepCopy()` in `AoeDefinition` / `LingeringAoeDefinition`
  needs no change (both new fields are value types, copied automatically).

### `BehaviorContexts.cs` — `AoeBehaviorContext`
- Rename setter `Count` → `EchoCount` (`set => aoe.echoCount = value;`).
- Add `public float ScatterRadius { set => aoe.scatterRadius = value; }`.

### `RuntimeAoeDefinition.cs`
- Rename `public int Count { get; set; } = 1;` → `public int EchoCount { get; set; } = 1;`.
- Add `public float ScatterRadius { get; set; }`.
- `RuntimeAoeIntervalSpawnSetup.Count` stays (it is the resolved per-tick burst
  count fed to `BuildAoeTemplate` as `echoCount`); no rename required, but note in
  a comment that it maps to echo count.

### `SkillSetCompiler.cs` — `BuildRuntime` (AOE branch, ~line 253)
- `EchoCount = Mathf.Max(1, a.echoCount),`
- `ScatterRadius = Mathf.Max(0f, a.scatterRadius),`
- Interval compile (`AoeIntervalSpawnTrigger`, ~line 345): keep
  `Count = Mathf.Max(1, childDef.EchoCount + trigger.spawnCount)` (was
  `childDef.Count`). The child's `ScatterRadius` rides on the child runtime def and
  reaches the template via `BuildAoeTemplate` in task 003.

## Acceptance criteria
- No references to AOE `.count` / `.Count` remain in authoring/runtime/compiler
  (except the interval-setup burst `Count`, documented as echo).
- Existing AOE skill assets keep their authored count via `FormerlySerializedAs`.
- Compiler folds `echoCount` (raw) and `scatterRadius` (raw) onto the runtime def.

## Dependencies
Compiles together with 001 and 003 (shared rename).

## Scope
Small, mechanical rename + two field additions across four files.

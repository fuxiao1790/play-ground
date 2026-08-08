---
name: rewire-skilldriver-compile-and-register
description: Wire SkillDriver.CompileAndRegister to call SkillLoadoutCompiler and delete the moved inline logic
---

# 002 - Rewire SkillDriver.CompileAndRegister

## Scope

Depends on [001-add-skill-loadout-compiler.md](001-add-skill-loadout-compiler.md). Changes only
[SkillDriver.cs](../../Assets/Scripts/Skills/SkillDriver.cs). No behavior change from the caller's perspective —
`Start()`, `TryRestoreRuntimeLoadout`, and `ProcessPendingEdit` all call `CompileAndRegister()` exactly as before.

## Changes

1. Replace [SkillDriver.cs:201-222](../../Assets/Scripts/Skills/SkillDriver.cs#L201-L222) (validator call + root
   detection loop) and [SkillDriver.cs:224-247](../../Assets/Scripts/Skills/SkillDriver.cs#L224-L247) (compile loop)
   with a single call:
   ```csharp
   CompiledLoadout compiled = SkillLoadoutCompiler.Compile(runtimeLoadout, snapshot);
   ```
2. Rebuild `compiledSlots`, `slotStates`, `firedCastTokens`, `this.rootNodeIndices`, `activeSlotCount` from
   `compiled.Roots` / `compiled.RootNodeIndices` / `compiled.Count`, reusing `FindPreservedState` exactly as today
   ([SkillDriver.cs:238-243](../../Assets/Scripts/Skills/SkillDriver.cs#L238-L243)) — this loop stays on
   `SkillDriver` unchanged in spirit, it just reads from `compiled.*` instead of locals built inline.
3. Use `compiled.Warnings` as the `warnings` list passed into `RegisterSpawnTemplates(warnings)`
   ([SkillDriver.cs:252](../../Assets/Scripts/Skills/SkillDriver.cs#L252)) so registration-time
   `SpawnChainDepthExceeded` warnings append to the same list the compiler seeded — matches current single-list
   behavior (see index.md open question on `Warnings` typing).
4. Set `validationWarnings = compiled.Warnings.ToArray();` at the end, replacing
   [SkillDriver.cs:253](../../Assets/Scripts/Skills/SkillDriver.cs#L253).
5. Delete from `SkillDriver.cs`, now dead: the root-detection loop, `HasTriggeredOnlyConversionSupport`
   ([SkillDriver.cs:554-570](../../Assets/Scripts/Skills/SkillDriver.cs#L554-L570)), `AppendCompilerWarnings`
   ([SkillDriver.cs:267-317](../../Assets/Scripts/Skills/SkillDriver.cs#L267-L317)). Confirm no other call site
   references them first (expected: none — both were private and only called from `CompileAndRegister`).
6. Confirm `SkillStatSnapshot snapshot = SkillStatAggregator.Aggregate(runtimeLoadout, statSheet);` stays in
   `SkillDriver` immediately before the new call — it needs the `statSheet` field, which
   `SkillLoadoutCompiler` deliberately does not take (keeps the compiler ignorant of where the snapshot came from).

## What Does Not Change

- `preserveCooldownState` / `cooldownResetNodeIndex` bookkeeping
  ([SkillDriver.cs:195-199](../../Assets/Scripts/Skills/SkillDriver.cs#L195-L199)).
- `RegisterProjectileTypes()`, `RegisterAoeTypes()`, `RegisterTargetedTypes()`, `RegisterSpawnTemplates(warnings)`
  calls and everything they call into (`SkillIntervalTemplateBuilder`, `EnsureStackingDetonationDebuffKey`, etc.) —
  out of scope per the state-management/registration split.
- `Tick`, `TryQueueEdit`, `TryApplyEdit`, `ProcessPendingEdit`, `TryRestoreRuntimeLoadout` — unaffected, they call
  `CompileAndRegister()` as an opaque step both before and after.

## Acceptance Criteria

- `SkillDriver.cs` no longer contains root-node detection, `HasTriggeredOnlyConversionSupport`, or
  `AppendCompilerWarnings` — that logic exists exactly once, in `SkillLoadoutCompiler`.
- `CompileAndRegister` reads, in order: aggregate snapshot → call `SkillLoadoutCompiler.Compile` → reconcile slot
  state → call `Register*` → publish `validationWarnings`. No compile-shaped logic (root detection, per-root
  compile, warning walk) remains inline.
- Behavior parity: for a given loadout + snapshot, `compiledSlots`, `slotStates` (cooldown-preservation semantics),
  `rootNodeIndices`, `activeSlotCount`, and `validationWarnings` are identical to pre-refactor output. This is a
  pure code-motion change — no new warnings, no reordered warnings, no changed root selection.
- Project compiles; no other file references the deleted private methods.

## Test Plan (user-run, per project-overview.md agent rules)

This repo requires agents to hand test execution to the user, with results reviewed from an exported XML file — not
run tests directly. Exact command to hand back once this task lands:

```
<UnityEditor> -runTests -testPlatform EditMode -testResults "<project-path>/TestResults/skill-compile-split-editmode-results.xml"
```

Relevant existing suites that exercise the moved logic indirectly (via `SkillSetCompiler`/`SkillLoadoutValidator`,
unchanged) and should stay green: `SkillValidationEditModeTests`, `TargetedValidationEditModeTests`,
`TargetedCompilerEditModeTests`, `ModifierFoldEditModeTests`, `ProjectileContinuousAuthoringEditModeTests`,
`CritEditModeTests`. None of these call `SkillDriver.CompileAndRegister` directly (it's private), so a pass here
confirms the untouched dependencies still behave, not the rewiring itself — flag to the user that manual/PlayMode
verification of actual skill casting + loadout editing in a scene is the real coverage for this task, since no
EditMode test currently exercises `SkillDriver.CompileAndRegister` end to end.

## Dependencies

Depends on 001.

## Scope/Complexity

Small-medium. Mechanical rewiring plus deletions; the risk is entirely in preserving the exact
`activeSlotCount`/index-mapping semantics called out in 001's acceptance criteria, not in new logic.

# 005 — Remove the wrapper

## Change kind: remove

## Structural role
Deletes the superseded bundling design so there is one stacking model, not two.
No compatibility layer left behind (focus item 2).

## Remove
- `StackingSkill` (SO), `StackingSkillDefinition`, `RuntimeStackingSkillDefinition`.
- The `RuntimeStackingSkillDefinition` branch in `SkillSetCompiler.CompileDefinition`
  /`BuildStackingRuntime` and the `RuntimeKind` helper.
- The `OnAoeHitSpawnTrigger` special-case for `RuntimeStackingSkillDefinition`
  ([SkillSetCompiler.cs:76-80](Assets/Scripts/Skills/SkillSetCompiler.cs#L76-L80)).
  Keep `OnAoeHitSpawnTrigger` itself for plain AOE→AOE on-hit spawn; only the wrapper
  branch goes.
- The wrapper-sourced `StackEffectSnapshot` builders in `SkillSpawnTranslator` that read
  `RuntimeStackingSkillDefinition` (003 replaces them with the applicator-sourced path).

## Structural notes
- Composition of multiple stacking detonations now uses ordinary links between the
  **applicator** sets (each applicator is a normal skill), not the removed wrapper.
- Migrate any existing `StackingSkill` assets / loadouts to applicator-set + `StackingSupport`
  + `StackTrigger`; note the manual asset migration in the task.

## Acceptance criteria
- No `RuntimeStackingSkillDefinition` reference remains; project compiles.
- The only stacking path is support + `StackTrigger`.

## Dependencies
004 (new path must be live and validated first).

## Scope
Small–medium (deletion + asset migration).

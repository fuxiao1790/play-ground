# 001 — Skill mana-cost field + `SkillStat.ManaCost` fold + runtime storage

## Goal
Turn the directly-authored `spawnEnergyCost` into a folded `manaCost` stat so
supports and (later) snapshot terms can modify it, and expose the folded result
on the compiled runtime definitions.

## Changes

1. **`Assets/Scripts/Skills/Modifiers/SkillStat.cs`** — add `ManaCost` to the
   enum. (The accumulator sizes its arrays from the enum length, so no other
   accumulator change is needed.)

2. **`Assets/Scripts/Skills/SkillDefinition.cs`**
   - `ProjectileDefinition` (L38): rename `spawnEnergyCost` → `manaCost`, add
     `[FormerlySerializedAs("spawnEnergyCost")]` above the `[Min(0f)]` attribute.
   - `AoeDefinitionBase` (L62): same rename + `[FormerlySerializedAs]`.
   - Keep the default value (`1f`) and `[Min(0f)]`.
   - `UnityEngine.Serialization` is already imported.

3. **`Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`** and
   **`RuntimeAoeDefinition.cs`** — add `public float ManaCost { get; set; }`.

4. **`Assets/Scripts/Skills/SkillSetCompiler.cs`** — in `BuildRuntime` (L224):
   - Projectile branch: set
     `ManaCost = Mathf.Max(0f, modifiers.Resolve(SkillStat.ManaCost, p.manaCost))`.
   - AOE branch: set
     `ManaCost = Mathf.Max(0f, modifiers.Resolve(SkillStat.ManaCost, a.manaCost))`.

## Notes
- Do **not** change `ApplyChildSpawn` / `ApplyAoeIntervalSpawn` yet — task 003
  switches those from the raw read to the folded `ManaCost`. After this task they
  still compile against the renamed field (`childSkillDefinition.manaCost`); leave
  that as a temporary raw read until 003, or land 001+003 together.
- The child-set's supports already flow through `CompileDefinition`, so once 003
  reads `childDef.ManaCost` the support-modified value is used automatically.

## Acceptance Criteria
- Project compiles; `SkillStat.ManaCost` exists.
- Existing skill assets keep their authored values (verify one asset in the
  inspector shows the old `spawnEnergyCost` number under the new `manaCost`
  label, courtesy of `[FormerlySerializedAs]`).
- Compiled `RuntimeProjectileDefinition.ManaCost` / `RuntimeAoeDefinition.ManaCost`
  equal the authored `manaCost` when no supports are present and snapshot is
  Identity.

## Dependencies
None. Blocks 002, 003.

## Scope
Small.

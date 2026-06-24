# 001 — Rename ChildSpawnTrigger → ProjectileIntervalSpawnTrigger

**Scope:** Small. Pure rename, no behavior change.
**Dependencies:** none.

## Changes

1. Rename file
   [Assets/Scripts/Skills/Trigger/ChildSpawnTrigger.cs](Assets/Scripts/Skills/Trigger/ChildSpawnTrigger.cs)
   → `ProjectileIntervalSpawnTrigger.cs`. Rename the class to `ProjectileIntervalSpawnTrigger`.
   - **Keep the existing `.cs.meta` GUID `a1000000000000000000000000000011`** (rename the file,
     keep its `.meta`). `TriggerLink : ScriptableObject`, so the file name must equal the class
     name.
   - Update `CreateAssetMenu` to menu `"PlayGround/Skills/Triggers/Projectile Interval Spawn"`,
     fileName `"NewProjectileIntervalSpawnTrigger"`.
   - Widen `SourceSkillTags` to `SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe`
     (a lingering AOE may now be the source). `TargetSkillTags` stays `Projectile`.

2. Update the trigger asset
   [Assets/ScriptableObjects/Triggers/ChildSpawnTrigger.asset](Assets/ScriptableObjects/Triggers/ChildSpawnTrigger.asset):
   - `m_EditorClassIdentifier: PlayGround.Runtime::PlayGround.Skills.ProjectileIntervalSpawnTrigger`
   - `m_Name`: `ProjectileIntervalSpawnTrigger`
   - Optionally rename the `.asset` + `.asset.meta` files; **keep the asset GUID
     `72106f578633ced47afdee80a7dc84db`** so loadouts resolve it. Loadout `.asset` files need
     no edits.

3. Update C# references to the old type name:
   - [Assets/Scripts/Skills/SkillSetCompiler.cs](Assets/Scripts/Skills/SkillSetCompiler.cs):
     `is ChildSpawnTrigger childTrigger` and the `ApplyChildSpawn(..., ChildSpawnTrigger trigger, ...)`
     signature. (Logic generalization happens in 002; here just rename the type.)
   - [Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs](Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs):
     comment `// Compiled from ChildSpawnTrigger;`.
   - The runtime type `RuntimeChildSpawnSetup` and the `ChildSpawnSetup` property stay
     (internal names; renaming is cosmetic and out of scope — only fix comments).

## Acceptance criteria

- Unity compiles with no errors.
- Loadouts referencing the trigger asset (e.g. `ArrowSpawnBulletStackAoeLoadout`,
  `BenchmarkMaxLoadout`) open with no "missing script" / no broken managed reference.
- proj→proj child spawning is byte-for-byte unchanged at runtime (deterministic ids same).

---
name: generic-interval-spawn-exploration
description: Exploration findings for merging ProjectileIntervalSpawnTrigger + AoeIntervalSpawnTrigger into one generic IntervalSpawnTrigger
---

# Exploration Findings

## Current Design
- Design doc: [Docs/reference/game-logic/skill-system.md](../../Docs/reference/game-logic/skill-system.md) — "Trigger Types" section (`ProjectileIntervalSpawnTrigger` ~L475, `AoeIntervalSpawnTrigger` ~L496), "Interval source/child support" table ~L517, compile() pseudocode ~L699-704.

## Key Files & Components

### Authoring layer
- `ProjectileIntervalSpawnTrigger` ([ProjectileIntervalSpawnTrigger.cs](../../Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs)) — fields `intervalSeconds`, `intervalJitterPercent`, `spawnCount`, `sideSpreadDegrees`; `SourceSkillTags = Projectile|Aoe`; `TargetSkillTags = Projectile`.
- `AoeIntervalSpawnTrigger` ([AoeIntervalSpawnTrigger.cs](../../Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs)) — byte-for-byte identical fields; `TargetSkillTags = Aoe`.
- Field sets are **identical**. The only distinction between the two classes is the declared `TargetSkillTags`, which is read only by `SkillLoadoutValidator` (tag-mismatch warning) and by `SkillSetCompiler`'s `is` type check (which branch to run).
- `TriggerLink` base ([TriggerLink.cs](../../Assets/Scripts/Skills/Trigger/TriggerLink.cs)) — abstract `SourceSkillTags`/`TargetSkillTags`, no fields. No reflection-based type lists exist anywhere (`grep` for `typeof(...IntervalSpawnTrigger)`/`nameof(...)` across `Assets/` returned nothing) — the two classes are only referenced by name in the 4 sites below, plus tests and 2 orphaned `.asset` files.

### Compile-time dispatch
- `SkillSetCompiler.Compile` ([SkillSetCompiler.cs:46-56](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L46-L56)) — two parallel `if (chain.link is X)` branches call `ApplyChildSpawn` / `ApplyAoeIntervalSpawn`.
- `ApplyChildSpawn` ([SkillSetCompiler.cs:298-332](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L298-L332)) — compiles the effect slot, requires `compiledChild is RuntimeProjectileDefinition`, builds `RuntimeChildSpawnSetup`, assigns to `parent.ChildSpawnSetup`.
- `ApplyAoeIntervalSpawn` ([SkillSetCompiler.cs:334-366](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L334-L366)) — same shape, requires `compiledChild is RuntimeAoeDefinition`, builds `RuntimeAoeIntervalSpawnSetup`, assigns to `parent.AoeIntervalSpawnSetup`.
- **Important**: both methods already dispatch on the *compiled effect's runtime type*, not on the trigger's declared target tag. The `TargetSkillTags` split on the trigger class is redundant with this — it only gates which of the two methods gets called, not what the methods themselves check.
- Both runtime setups are independent fields (`ChildSpawnSetup` for projectile children, `AoeIntervalSpawnSetup` for AOE children) that already coexist side-by-side on both `RuntimeProjectileDefinition` and `RuntimeAoeDefinition` ([RuntimeProjectileDefinition.cs:45-48](../../Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs#L45-L48), [RuntimeAoeDefinition.cs:28-31](../../Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs#L28-L31)) — no structural change needed there.

### Validation
- `SkillLoadoutValidator.IsIntervalSpawnTrigger` ([SkillLoadoutValidator.cs:194-195](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs#L194-L195)) — `link is ProjectileIntervalSpawnTrigger or AoeIntervalSpawnTrigger`, gates the pulse-AOE-source warning ([SkillLoadoutValidator.cs:181-192](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs#L181-L192)).
- Generic `TargetSkillTags` mismatch warning ([SkillLoadoutValidator.cs:148-152](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs#L148-L152)) is driven purely off the abstract property — no special-casing needed once the merged trigger reports `Projectile|Aoe`.

### ECS / runtime side — unaffected
- `IntervalChildKind`, `TimedSpawnComponent`, `TimedSpawnSystem`, `RegisterTimedSpawnTemplate`, and all materialization call sites (`ProjectileSpawnApplySystem`, `AoeSpawnApplySystem`) already discriminate purely on the *compiled* runtime shape (`RuntimeChildSpawnSetup` vs `RuntimeAoeIntervalSpawnSetup`, `IntervalChildKind` enum), not on which authoring trigger class produced them. Confirmed via full-repo trace (see prior conversation turn) — **no ECS-side changes required**.

## Data migration hazard (asset GUIDs)
- `Assets/ScriptableObjects/Triggers/ProjectileIntervalSpawnTrigger.asset` — `m_Script` guid `a1000000000000000000000000000011` correctly matches `ProjectileIntervalSpawnTrigger.cs.meta`.
- `Assets/ScriptableObjects/Triggers/AoeIntervalSpawnTrigger.asset` — `m_Script` guid `a1000000000000000000000000000012` does **not** match `AoeIntervalSpawnTrigger.cs.meta` (guid `963edcf1cd09aa84e92b45418222855c`); it actually matches `RuntimeAoeDefinition.cs.meta`. This asset is **already broken** in current `main` — Unity would show it as referencing the wrong script type. This is a pre-existing bug, unrelated to this task.
- Neither `.asset` is referenced by any other asset in the repo (`grep` for both GUIDs across `*.asset` found only self-references) — both are orphaned example/test fixtures, not wired into any `PlayerLoadout`. No in-repo loadout data migration is needed.
- Because the asset reference is already broken, the safe move is: repurpose `ProjectileIntervalSpawnTrigger.cs`/`.asset` in place (rename class + file, same GUIDs, so Unity re-resolves cleanly) and delete `AoeIntervalSpawnTrigger.cs`/`.asset` outright (it's non-functional today and orphaned).

## Test sites requiring changes
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs` — 9 usages across 8 tests. Two tests assert the *old* restrictive behavior that this task explicitly removes and must be deleted, not just renamed:
  - `ValidatorWarnsWhenProjectileIntervalTargetsAoeSkill` ([:41-58](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L41-L58)) — asserts `UnsupportedTriggerTarget` warning when a projectile-only trigger targets an AOE skill. No longer possible once the trigger accepts both targets.
  - `CompilerIgnoresProjectileIntervalTargetAoeSkill` ([:60-88](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L60-L88)) — asserts the compiler no-ops (null `ChildSpawnSetup`) for the same mismatched combo. Once merged, this combo populates `AoeIntervalSpawnSetup` instead — already covered by `CompilerPopulatesAoeIntervalSetupOnProjectileSource` ([:125-162](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L125-L162)) after it's renamed.
  - Remaining 6 tests just rename the type at declaration; their assertions already exercise all 4 source/target combos (`{Projectile, LingeringAoe} source x {Projectile, Aoe} target`) and remain valid as-is.
- `Assets/Tests/PlayMode/AoePlayModeTests.cs` — 4 usages across 2 tests (`CompileAndRegisterAssignsDedupedIntervalTemplateKeys`, `LingeringAoeIntervalChildrenUseRegistryTemplatesUntilSourceExpires`); pure rename, no assertion changes (both tests use `ScriptableObject.CreateInstance<T>()` directly, not the `.asset` fixtures).

## Documentation Gaps
- None — `skill-system.md` fully documents both trigger types' current behavior; the merge requires editing existing sections, not filling a gap.

## Recommended Next Step
Documentation and code are both clear enough to plan directly — proceeding to `index.md`.

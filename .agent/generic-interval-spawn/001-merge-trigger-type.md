---
name: merge-trigger-type
description: Rename ProjectileIntervalSpawnTrigger to IntervalSpawnTrigger and delete AoeIntervalSpawnTrigger
---

# 001 — Merge trigger authoring type

## Scope

Collapse the two `TriggerLink` subclasses into one.

## Changes

1. `Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs` ->
   rename file to `IntervalSpawnTrigger.cs` (rename on disk so the `.meta`
   GUID `a1000000000000000000000000000011` travels with it — do not
   delete+recreate).
   - Rename class `ProjectileIntervalSpawnTrigger` -> `IntervalSpawnTrigger`.
   - Change `[CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Projectile Interval Spawn", fileName = "NewProjectileIntervalSpawnTrigger")]`
     -> `[CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Interval Spawn", fileName = "NewIntervalSpawnTrigger")]`.
   - Change `TargetSkillTags` from `SkillDefinitionTags.Projectile` to
     `SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe`.
   - Fields (`intervalSeconds`, `intervalJitterPercent`, `spawnCount`,
     `sideSpreadDegrees`) and `SourceSkillTags` (`Projectile | Aoe`) are
     unchanged.
2. Delete `Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs` and its
   `.meta`.
3. `Assets/ScriptableObjects/Triggers/ProjectileIntervalSpawnTrigger.asset` ->
   rename file (+ `.meta`) to `IntervalSpawnTrigger.asset`. Update
   `m_Name: ProjectileIntervalSpawnTrigger` -> `m_Name: IntervalSpawnTrigger`
   and `m_EditorClassIdentifier` to reference the new class
   (`PlayGround.Skills.IntervalSpawnTrigger`). Leave the authored field values
   (`intervalSeconds: 0.15`, etc.) as-is — this is example/test fixture data,
   not meaningful content.
4. Delete `Assets/ScriptableObjects/Triggers/AoeIntervalSpawnTrigger.asset`
   and its `.meta`. This asset's `m_Script` GUID
   (`a1000000000000000000000000000012`) already points at
   `RuntimeAoeDefinition.cs`, not at `AoeIntervalSpawnTrigger.cs` — it is
   already a broken reference in Unity today and is not referenced by any
   other asset in the repo (verified via GUID grep across `Assets/**/*.asset`).
   Safe to delete outright.

## Acceptance Criteria

- `IntervalSpawnTrigger` is the only interval-spawn trigger type in the
  codebase; `ProjectileIntervalSpawnTrigger`/`AoeIntervalSpawnTrigger` no
  longer exist as symbols.
- Opening `Assets/ScriptableObjects/Triggers/IntervalSpawnTrigger.asset` in
  the Unity inspector shows it correctly typed as `IntervalSpawnTrigger` (not
  "Missing Script").
- No compile errors from the rename (dependents fixed in tasks 002-004).

## Dependencies

None — this is the first task. Tasks 002-004 depend on this rename landing
first (they reference the new type name).

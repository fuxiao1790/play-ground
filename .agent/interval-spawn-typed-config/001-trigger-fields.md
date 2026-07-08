---
name: trigger-fields
description: Replace the generic interval-trigger fields with type-specific ones on both trigger classes and their asset fixtures
---

# 001 — Type-specific trigger fields

## Scope

- `Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs`
- `Assets/ScriptableObjects/Triggers/ProjectileIntervalSpawnTrigger.asset` (+ `.meta`)
- `Assets/ScriptableObjects/Triggers/AoeIntervalSpawnTrigger.asset` (+ `.meta`)

Both trigger types are **kept** — this task only changes their fields.

## Changes

### `ProjectileIntervalSpawnTrigger.cs`
- Rename field `public int spawnCount;` -> `public int projectileCount;`
  (keep `[Min(0)]`).
- Keep `intervalSeconds`, `intervalJitterPercent`, `sideSpreadDegrees`,
  `SourceSkillTags` (`Projectile | Aoe`), `TargetSkillTags` (`Projectile`)
  unchanged.

### `AoeIntervalSpawnTrigger.cs`
- Rename field `public int spawnCount;` -> `public int echoCount;`
  (keep `[Min(0)]`).
- Replace `[Range(0f, 180f)] public float sideSpreadDegrees = 30f;` with
  `[Min(0f)] public float scatterRadius;` (default `0f` — echoes overlap at
  center unless authored otherwise, matching current `ScatterRadius = 0`
  behavior).
- Keep `intervalSeconds`, `intervalJitterPercent`, `SourceSkillTags`
  (`Projectile | Aoe`), `TargetSkillTags` (`Aoe`) unchanged.

### `ProjectileIntervalSpawnTrigger.asset`
- Rename YAML field `spawnCount:` -> `projectileCount:` (keep the value).

### `AoeIntervalSpawnTrigger.asset`
- **Fix the broken script reference**: change
  `m_Script: {fileID: 11500000, guid: a1000000000000000000000000000012, type: 3}`
  to the real trigger script GUID
  `963edcf1cd09aa84e92b45418222855c` (from
  `AoeIntervalSpawnTrigger.cs.meta`). Today this asset points at
  `RuntimeAoeDefinition.cs` and shows as a broken/mistyped script in Unity.
- Rename YAML field `spawnCount:` -> `echoCount:`.
- Replace `sideSpreadDegrees: 30` with `scatterRadius: 0` (or any sensible
  fixture value; this asset is an orphaned example, not wired to a loadout).

## Acceptance Criteria

- `ProjectileIntervalSpawnTrigger` exposes `projectileCount` (not `spawnCount`);
  `AoeIntervalSpawnTrigger` exposes `echoCount` and `scatterRadius` (no
  `spawnCount`, no `sideSpreadDegrees`).
- Opening `AoeIntervalSpawnTrigger.asset` in the inspector shows it correctly
  typed as `AoeIntervalSpawnTrigger` (not "Missing/Wrong Script").
- Compile will fail until 002 updates `SkillSetCompiler` field references —
  expected; 001-003 land together.

## Dependencies

First task. 002 depends on the renamed fields existing.

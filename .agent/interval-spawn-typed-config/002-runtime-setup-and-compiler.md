---
name: runtime-setup-and-compiler
description: Swap the dead SideSpreadDegrees on RuntimeAoeIntervalSpawnSetup for ScatterRadius and update SkillSetCompiler field references
---

# 002 — Runtime setup + compiler

## Scope

- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
  (holds `RuntimeAoeIntervalSpawnSetup`)
- `Assets/Scripts/Skills/SkillSetCompiler.cs`

## Changes

### `RuntimeAoeIntervalSpawnSetup` ([RuntimeProjectileDefinition.cs:18-28](../../Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs#L18-L28))
- Remove `public float SideSpreadDegrees { get; set; }` (write-only dead field).
- Add `public float ScatterRadius { get; set; }`.
- `RuntimeChildSpawnSetup` is unchanged (projectile count + spread already live
  in `Behavior`).

### `SkillSetCompiler.ApplyChildSpawn` ([:298-332](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L298-L332))
- Change `childDef.Count + trigger.spawnCount` -> `childDef.Count + trigger.projectileCount`.
- `trigger.sideSpreadDegrees` reference is unchanged (projectile trigger keeps
  that field).

### `SkillSetCompiler.ApplyAoeIntervalSpawn` ([:334-366](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L334-L366))
- Change `Count = Mathf.Max(1, childDef.EchoCount + trigger.spawnCount)`
  -> `Count = Mathf.Max(1, childDef.EchoCount + trigger.echoCount)`.
- Replace `SideSpreadDegrees = trigger.sideSpreadDegrees,` with
  `ScatterRadius = Mathf.Max(0f, trigger.scatterRadius),`.
  - **Scatter semantics (see index.md Open Decision): authoritative** — the
    trigger's `scatterRadius` becomes the interval burst scatter, mirroring how
    `sideSpreadDegrees` is authoritative for projectile bursts (child's own
    spread is ignored). The child skill's own `ScatterRadius` is therefore not
    added here.
  - If the reviewer prefers **additive** instead, use
    `ScatterRadius = Mathf.Max(0f, childDef.ScatterRadius + trigger.scatterRadius)`.
    This is the only line that changes for that decision.

## Acceptance Criteria

- `RuntimeAoeIntervalSpawnSetup` has `ScatterRadius`, not `SideSpreadDegrees`;
  project-wide search finds no remaining `SideSpreadDegrees` references.
- Compiler references `projectileCount` / `echoCount` / `scatterRadius`; no
  remaining `.spawnCount` reference on either trigger.
- Projectile interval compilation behavior is otherwise identical (count still
  additive, spread still authoritative).

## Dependencies

Depends on [001-trigger-fields.md](./001-trigger-fields.md). Blocks
[003-template-scatter-threading.md](./003-template-scatter-threading.md), which
consumes `setup.ScatterRadius`.

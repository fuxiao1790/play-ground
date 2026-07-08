---
name: update-docs
description: Update skill-system.md to reflect the type-specific interval trigger fields (projectileCount / echoCount+scatterRadius)
---

# 005 — Update docs

## Scope

`Docs/reference/game-logic/skill-system.md`

## Changes

1. **`ProjectileIntervalSpawnTrigger` block** (~L482-489) — rename the field in
   the C# snippet: `int spawnCount;` -> `int projectileCount;`. Keep
   `intervalSeconds`, `intervalJitterPercent`, `sideSpreadDegrees`.

2. **`AoeIntervalSpawnTrigger` block** (~L503-510) — in the C# snippet: rename
   `int spawnCount;` -> `int echoCount;` and replace
   `float sideSpreadDegrees;` -> `float scatterRadius;`.

3. **`spawnCount` additive prose** (~L530-534) — update to name the two fields:
   `projectileCount` is additive with the child projectile's `Count`;
   `echoCount` is additive with the child AOE's `EchoCount`; both floored to 1.
   State that these are the only additive multiplicity fields.

4. **Add scatter/spread prose** — document that `sideSpreadDegrees` (projectile
   trigger) and `scatterRadius` (AOE trigger) are **authoritative** for the
   interval burst geometry: they define the burst's spread / scatter directly
   and the child skill's own spread / scatter is not applied to interval-spawned
   copies. (If 002 implemented additive scatter instead, describe it as additive
   to the child's `ScatterRadius` — keep this paragraph in sync with the code.)

5. **Directionality defaults** (~L536-545) — the "AOE child from any source ...
   placed in a deterministic random disk within the child `scatterRadius`"
   wording should now read the **trigger's** `scatterRadius` (authoritative),
   not the child's. Update accordingly.

6. **"Interval source/child support" table** (~L517-523) and any example slot
   lists referencing `ProjectileIntervalSpawn` / `AoeIntervalSpawn` — no name
   change needed (both trigger types are retained). Only verify field references
   in prose match the new names.

7. **Sweep** the doc for any other `spawnCount` / `sideSpreadDegrees` mentions
   tied to interval triggers and update.

## Acceptance Criteria

- `skill-system.md` describes `projectileCount` on the projectile interval
  trigger and `echoCount` + `scatterRadius` on the AOE interval trigger, with
  correct additive (count) vs authoritative (geometry) semantics matching the
  shipped code.
- No stale `spawnCount` references remain for the interval triggers.

## Dependencies

Land after [001-004](./001-trigger-fields.md).

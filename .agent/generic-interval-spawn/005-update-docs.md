---
name: update-docs
description: Update skill-system.md to describe one IntervalSpawnTrigger instead of two trigger types
---

# 005 — Update docs

## Scope

`Docs/reference/game-logic/skill-system.md`

## Changes

1. **Trigger Types section** — replace the separate
   `ProjectileIntervalSpawnTrigger` (~L475-494) and `AoeIntervalSpawnTrigger`
   (~L496-516) subsections with one `IntervalSpawnTrigger` subsection:
   - Class body: same 4 fields (`intervalSeconds`, `intervalJitterPercent`,
     `spawnCount`, `sideSpreadDegrees`).
   - Compatible tags: source `Projectile` or `Aoe` (lingering only —
     documented separately via the pulse-AOE warning, unchanged); target
     `Projectile` or `Aoe`.
   - State plainly that the child kind (projectile vs AOE) is determined by
     what the effect slot compiles to, not by a separate trigger variant —
     the same trigger asset works for all 4 combinations.
   - Keep the existing compiled-field description (`RuntimeChildSpawnSetup`
     onto `ChildSpawnSetup` for projectile children,
     `RuntimeAoeIntervalSpawnSetup` onto `AoeIntervalSpawnSetup` for AOE
     children) — that part of the design does not change.

2. **"Interval source/child support" table** (~L517-523) — collapse the two
   rows previously implying two trigger types down to a note that one
   `IntervalSpawnTrigger` covers all combinations; keep the "Pulse AOE
   source -> warning, no-op" row as-is.

3. **`intervalJitterPercent` / `spawnCount` prose** (~L525-535) — unchanged in
   substance, just remove any wording that implies a projectile-only or
   AOE-only trigger.

4. **Directionality defaults** (~L536-545) — unchanged; still keyed off
   source kind (projectile vs AOE) and child kind (compiled effect type), not
   off trigger identity.

5. **`compile()` pseudocode** (~L699-704) — collapse:
   ```
   if chain.link is ProjectileIntervalSpawnTrigger:
       compile chain.effect recursively -> RuntimeProjectileDefinition
       bake RuntimeChildSpawnSetup onto runtime.ChildSpawnSetup
   if chain.link is AoeIntervalSpawnTrigger:
       compile chain.effect recursively -> RuntimeAoeDefinition
       bake RuntimeAoeIntervalSpawnSetup onto runtime.AoeIntervalSpawnSetup
   ```
   into:
   ```
   if chain.link is IntervalSpawnTrigger:
       compiledChild = compile chain.effect recursively
       if compiledChild is RuntimeProjectileDefinition:
           bake RuntimeChildSpawnSetup onto runtime.ChildSpawnSetup
       if compiledChild is RuntimeAoeDefinition:
           bake RuntimeAoeIntervalSpawnSetup onto runtime.AoeIntervalSpawnSetup
   ```

6. **Examples section** — the "Deep chain" example (~L950-957) and "Deep
   chain: two trigger links" example (~L1013-1036) reference
   `ProjectileIntervalSpawn` by name in slot lists. Update to
   `IntervalSpawn` (or `IntervalSpawnTrigger`, matching whatever short-form
   convention the rest of the doc uses for other trigger names in examples).

7. **Sweep for any other literal occurrences** of
   `ProjectileIntervalSpawnTrigger` / `AoeIntervalSpawnTrigger` /
   `ProjectileIntervalSpawn` / `AoeIntervalSpawn` elsewhere in the doc (e.g.
   "Current warning cases" list ~L638) and update to the merged name.

## Acceptance Criteria

- No remaining references to `ProjectileIntervalSpawnTrigger` or
  `AoeIntervalSpawnTrigger` in `skill-system.md`.
- Doc reads as one generic interval-spawn trigger supporting all 4
  source/target combinations, matching the shipped code.

## Dependencies

Should land after [001-004](./001-merge-trigger-type.md) so the doc describes
shipped behavior, not a still-in-progress rename.

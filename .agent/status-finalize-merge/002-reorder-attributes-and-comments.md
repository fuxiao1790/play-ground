# 002 — Repoint ordering attributes + fix comments (compile rider on 001)

## Goal

Once `StatusProcessSystem` is deleted (001), every `typeof(StatusProcessSystem)`
reference is a compile error. Repoint the ordering edges onto the merged system
and preserve the pipeline shape.

## Changes

Deleting `StatusProcessSystem` while keeping the merged finalize system where it
already sits (`UpdateAfter` the three collision systems, `UpdateBefore` the three
expansion systems) preserves the existing schedule position of the status work,
because finalize already ran immediately before status. So:

- **`CombatApplyFinalizeSingleSystem`**
  ([:51](../../Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs#L51)):
  remove `[UpdateBefore(typeof(StatusProcessSystem))]`. Keep the existing
  `UpdateAfter(collision)` / `UpdateBefore(expansion)` attributes — they now also
  bound the folded-in status pass.

- **`ImpactAoeSpawnExpansionSystem`**
  ([AoeSpawnExpansionSystem.cs:186](../../Assets/Scripts/System/Aoes/AoeSpawnExpansionSystem.cs#L186)):
  `[UpdateAfter(typeof(StatusProcessSystem))]` →
  `[UpdateAfter(typeof(CombatApplyFinalizeSingleSystem))]`.
  (If the system already carries an `UpdateAfter(CombatApplyFinalizeSingleSystem)`,
  just delete the status line instead of duplicating.)

- **`LingeringAoeSpawnExpansionSystem`**
  ([AoeSpawnExpansionSystem.cs:363](../../Assets/Scripts/System/Aoes/AoeSpawnExpansionSystem.cs#L363)):
  same repoint.

- **`ProjectileSpawnExpansionSystem`**
  ([ProjectileSpawnExpansionSystem.cs:38](../../Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs#L38)):
  same repoint.

- **`CombatTargetProxy`** flow comment
  ([CombatTargetProxy.cs:40](../../Assets/Scripts/System/Targets/CombatTargetProxy.cs#L40)):
  update "CombatApplyFinalizeSingleSystem accrues entries, then StatusProcessSystem
  fizzles or detonates them" → describe the single merged system doing both passes.

## Acceptance criteria

- `rg "typeof\(StatusProcessSystem\)"` over `Assets/Scripts` returns no matches.
- Each expansion system still has an ordering edge that puts it after the merged
  finalize system (whether via the repointed attribute or a pre-existing one).
- `CombatTargetProxy` comment no longer names `StatusProcessSystem`.

## Scope / risk

Low. Attribute + comment edits only. No logic change.

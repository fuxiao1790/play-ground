---
name: generic-interval-spawn
description: Merge ProjectileIntervalSpawnTrigger + AoeIntervalSpawnTrigger into one generic IntervalSpawnTrigger authoring type
---

# Generic Interval Spawn

## Summary

Collapse `ProjectileIntervalSpawnTrigger` and `AoeIntervalSpawnTrigger` — two
`TriggerLink` subclasses with byte-for-byte identical fields, differing only
in the declared `TargetSkillTags` — into a single `IntervalSpawnTrigger` type
whose `TargetSkillTags` is `Projectile | Aoe`. `SkillSetCompiler` already
dispatches per-child based on the *compiled runtime type* of the effect slot
(`RuntimeProjectileDefinition` vs `RuntimeAoeDefinition`), not on the trigger's
declared target tag, so both `ApplyChildSpawn` and `ApplyAoeIntervalSpawn`
collapse into one `ApplyIntervalSpawn` that branches on the compiled child
type instead of two call sites gated by trigger type. Result: one trigger
asset placed between any Projectile/lingering-AOE source and any
Projectile/AOE target — all 4 source x target combinations authored the same
way. See [info.md](./info.md) for full exploration detail.

## Constraints & Invariants

- **ECS/runtime boundary is untouched.** `RuntimeChildSpawnSetup` and
  `RuntimeAoeIntervalSpawnSetup` remain two distinct runtime setup shapes
  (projectile children carry `ProjectileChildSpawnBehavior`; AOE children
  carry `Count`/`SideSpreadDegrees`) stored in two distinct fields
  (`ChildSpawnSetup`, `AoeIntervalSpawnSetup`) on both runtime definitions.
  `TimedSpawnComponent`, `IntervalChildKind`, `TimedSpawnSystem`, and all
  materialization call sites already key off these compiled shapes, not the
  authoring trigger class. Source: [info.md § ECS / runtime side](./info.md).
  This task only touches the authoring/compile layer.
- **Pulse-AOE source guard preserved.** A pulse (non-lingering) AOE source has
  no lifetime to tick an interval against; `ApplyIntervalSpawn` must keep the
  existing `parent is RuntimeAoeDefinition { LifetimeSeconds: <= 0f } => return`
  guard and the validator must keep warning on it. Source:
  [SkillSetCompiler.cs:309](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L309),
  [SkillLoadoutValidator.cs:181-192](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs#L181-L192).
- **Asset GUID continuity.** Unity resolves a `ScriptableObject` asset's type
  by the GUID of its `.cs` file, not by class name, so renaming the class
  inside `ProjectileIntervalSpawnTrigger.cs` in place (same `.meta` GUID)
  keeps the existing `.asset` fixture resolvable. `AoeIntervalSpawnTrigger.asset`
  is already broken (references the wrong script GUID — see
  [info.md § Data migration hazard](./info.md)) and unreferenced elsewhere, so
  it is deleted outright rather than migrated.

## Mechanisms Reused vs. Introduced

- **Reused**: the existing "compile child, branch on its runtime type" pattern
  already present inside both `ApplyChildSpawn` and `ApplyAoeIntervalSpawn` —
  merging just removes the redundant outer branch that picked *which* method
  to call based on trigger class.
- **Introduced**: nothing new. This is a pure consolidation — one trigger
  class, one compiler method, one validator check, replacing two of each.

## Design Validation

| Invariant | Held by |
|---|---|
| ECS/runtime untouched | `ApplyIntervalSpawn` still produces the same two setup types via the same field assignments; only the dispatch that decides *whether* to run projectile-child or AOE-child logic changes, from "which trigger class" to "what the compiled child actually is" (a strictly more accurate check, since that's what the existing code already asserted internally with `is not RuntimeXDefinition childDef => return`) |
| Pulse-AOE guard | Copied verbatim into the merged method |
| Asset GUID continuity | `ProjectileIntervalSpawnTrigger.cs`/`.asset` renamed in place (same GUIDs); `AoeIntervalSpawnTrigger.cs`/`.asset` deleted (already non-functional, unreferenced) |

## Minimal/Additive vs. Refactor Comparison

- **Minimal/additive** (keep both trigger classes, just relax `TargetSkillTags`
  on each to `Projectile | Aoe`):
  - resulting data flow: two authoring types remain, both now behaviorally
    identical, offered as separate menu items (`Create > ... > Projectile
    Interval Spawn` and `... > Aoe Interval Spawn`) that do the same thing —
    a duplicate-type smell the user explicitly asked to remove ("generic
    interval spawn").
  - new concepts/types introduced: none, but the existing duplication persists.
  - copies/translations added: none.
  - long-term cost: two SO types + two compiler branches + two validator
    references forever describe one concept; every future field/behavior
    change must be kept in sync across both.
- **Refactor** (this plan — merge into one `IntervalSpawnTrigger`):
  - resulting data flow: one authoring type, one compile-time dispatch method
    keyed on the compiled child's runtime type.
  - existing concepts/types changed or removed: `AoeIntervalSpawnTrigger`
    removed; `ProjectileIntervalSpawnTrigger` renamed/widened in place;
    `ApplyChildSpawn`/`ApplyAoeIntervalSpawn` merge into `ApplyIntervalSpawn`;
    `SkillLoadoutValidator.IsIntervalSpawnTrigger` narrows to one `is` check.
  - copies/translations removed: the redundant outer type-branch in
    `SkillSetCompiler.Compile` (two `if (chain.link is X)` blocks -> one).
  - long-term benefit: single source of truth for "periodic child spawn"
    authoring; new fields/behavior only need to be added once.
  - **Decision: refactor.** This is exactly the outcome the user asked for,
    and the codebase already has precedent for this shape of consolidation
    (see `[[project_combat_gate_consolidation]]` in memory — merging
    near-duplicate tag/trigger types into one generic one before further work
    proceeds).

## Task List

1. [001-merge-trigger-type.md](./001-merge-trigger-type.md) — rename
   `ProjectileIntervalSpawnTrigger` -> `IntervalSpawnTrigger` in place, widen
   `TargetSkillTags`, delete `AoeIntervalSpawnTrigger` (+ matching `.asset`
   rename/delete).
2. [002-collapse-compiler-dispatch.md](./002-collapse-compiler-dispatch.md) —
   merge `ApplyChildSpawn`/`ApplyAoeIntervalSpawn` into one
   `ApplyIntervalSpawn` in `SkillSetCompiler`, dispatch on compiled child type.
3. [003-update-validator.md](./003-update-validator.md) — collapse
   `SkillLoadoutValidator.IsIntervalSpawnTrigger` to the single merged type.
4. [004-update-tests.md](./004-update-tests.md) — rename type usages across
   `SkillValidationEditModeTests.cs` and `AoePlayModeTests.cs`; delete the two
   EditMode tests whose premise (target-tag mismatch is rejected) the merge
   intentionally removes.
5. [005-update-docs.md](./005-update-docs.md) — update
   `Docs/reference/game-logic/skill-system.md` to describe one
   `IntervalSpawnTrigger` instead of two trigger types.

## Open Questions / Dependencies

- None outstanding — scope was confirmed with the user: all 4 source/target
  combinations (proj->proj, proj->aoe, aoe->proj, aoe->aoe) must work through
  one generic trigger; runtime/ECS layer stays as-is.
- Tasks 001-003 must land together (they're one compile-correctness unit);
  004 and 005 can follow immediately after since nothing else depends on the
  old names.

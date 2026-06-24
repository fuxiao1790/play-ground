# 006 — Validation, docs, tests

**Scope:** Small–Medium.
**Dependencies:** 001–005.

## Changes

1. Validation [SkillLoadoutValidator.cs](Assets/Scripts/Skills/SkillLoadoutValidator.cs):
   - The generic source/target tag check already covers both interval triggers via
     `SourceSkillTags`/`TargetSkillTags`.
   - Add a warning when an interval trigger's **source is an AOE that is not lingering**
     (`Definition` is `AoeDefinitionBase` but not `LingeringAoeDefinition`) — a pulse AOE has
     no duration to tick on, so the link compiles to a no-op. Model it on
     `ValidateAoeHitSpawnLink`'s definition-type inspection.

2. Docs [Docs/game-logic/skill-system.md](Docs/game-logic/skill-system.md):
   - Replace the `ChildSpawnTrigger` subsection with `ProjectileIntervalSpawnTrigger` +
     `AoeIntervalSpawnTrigger`. Document the 2×2 source/child matrix, the "duration source
     only (projectile or lingering AOE)" rule, the additive `spawnCount`, and the
     directionality defaults (radial fan for AOE-source projectiles).
   - Update the Trigger Types table, the compile pseudocode (the
     `if chain.link is ChildSpawnTrigger` block), the Root Detection example, and the deep-
     chain examples that name `ChildSpawn`.

3. Tests:
   - [SkillValidationEditModeTests.cs](Assets/Tests/EditMode/SkillValidationEditModeTests.cs):
     update references to the renamed type; add cases asserting (a) the correct runtime setup
     field is populated per trigger×source combo, (b) a non-lingering AOE source yields the new
     warning.
   - [BareMinimumPrototypePlayModeTests.cs](Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs):
     update `ChildSpawnConfig`/child-spawn references; keep the proj→proj regression. Optionally
     add a PlayMode smoke test for one new combo (e.g. aoe→aoe) if the harness supports
     spawning a lingering-AOE root.

## Acceptance criteria

- All EditMode + PlayMode tests pass.
- Docs accurately describe the two triggers and the 2×2 behavior.
- A loadout wiring an interval trigger from a pulse-AOE source surfaces a validation warning
  and compiles to a no-op (no crash).

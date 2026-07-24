# 007 — Verify, guard against regression, and document

**Depends on:** 006. **Scope:** medium.

## Objective
Prove the boundary holds, make it hard to violate again, and update the docs so the
next reader finds the code.

## Changes
1. **Compile & test (user, Unity):**
   - Project compiles clean with the three-package layout.
   - Run EditMode + PlayMode suites (`PlayGround.Tests.*`). AOE/projectile/spawn
     tests must pass unchanged — this refactor moved files and deleted dead code
     only; no runtime data path changed.
   - Smoke-run `BenchmarkLarge`: projectiles/AOEs spawn, skill-edit UI works.
2. **Guardrails (prevent re-tangling):**
   - Keep asmdef `references` minimal so a new upward `using` fails to compile
     (the reference simply isn't there). This is the primary guard.
   - Optional: an EditMode test that asserts `PlayGround.Sim`'s referenced
     assemblies contain no `PlayGround.GameLogic`/`PlayGround.SkillUi`/`PlayGround.Debugging`.
   - Confirm the exempt leaf is truly a leaf: no core-layer asmdef references
     `PlayGround.Debugging`.
3. **Docs:**
   - `Docs/folder-structure.md`: document the four assemblies under `Assets/Scripts/`
     (Sim at `System/`, GameLogic at root, SkillUi at `SkillUi/`, Debugging at
     `Debugging/`) and their one-way references; note `com.playground.skill-ui` is no
     longer a package.
   - `Docs/architecture/layer-rules.md` / layer docs: state the assembly boundary and
     that the combat bridge + presentation live in the Sim assembly (serve-ECS rule);
     existing `Assets/Scripts/System/...` paths stay valid (code did not move out).
   - Fix the stale scene `m_EditorClassIdentifier` for `SkillLoadoutUi` /
     `SkillUiCatalog` (Unity rewrites these on reimport; confirm they resolve).

## Acceptance criteria
- All tests green; benchmark scene runs.
- `folder-structure.md` names the four assemblies and their `Assets/Scripts/` locations.
- A deliberate test `using PlayGround.Skills;` added to a sim file fails to compile
  (sanity check the guard), then reverted.

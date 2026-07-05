# 006 — Verify & Cleanup

## Goal

Confirm the combat pipeline no longer reaches into other systems' fields, that behavior and
performance are preserved, and that the guardrail is documented.

Depends on: 001–005.

## Checks

1. **No remaining combat-lane reach-ins.** Grep confirms zero matches for the migrated sinks:
   - `GetExistingSystemManaged<CombatVfxDispatchSystem>`
   - `GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>`
   - `GetExistingSystemManaged<ProjectileSpawnExpansionSystem>`
   - `GetExistingSystemManaged<ImpactAoeSpawnExpansionSystem>`
   - `GetExistingSystemManaged<LingeringAoeSpawnExpansionSystem>`

   Remaining `GetExistingSystemManaged` uses are expected/allowed only where documented
   (e.g. `CombatApplyBridge` lookup inside finalize — a managed presentation bridge, not a
   shared native container; note it explicitly if it stays).

2. **No leaked encapsulation.** The five sink systems expose no `internal` `NativeQueue` /
   `NativeList` / `JobHandle` / `AsParallelWriter()` members.

3. **Every singleton carries an `ECS Lifecycle:` comment** describing create/fill/drain/dispose.

4. **Full PlayMode suite green** — `AoeSimulationTests`, `ProjectileCollisionSimulationTests`,
   `ProjectileSpawnPipelineTests`, `CombatPoolCleanupSystemTests`, and any others touching
   the combat pipeline.

5. **Determinism spot-check.** Run a seeded stress scenario before/after the branch; projectile
   and AOE spawn IDs, counts, and damage totals match.

6. **Safety + leaks.** Full session in Editor with Jobs Debugger + Native Leak Detection
   (full) on: no safety violations during play, no leaks reported on exit/domain reload.

7. **Performance.** Capture a profiler frame on a high-count stress scene before/after. Confirm
   no new sync points and no worker-idle bubbles from accidental over-`Complete()`. Spawn,
   collision, and VFX-dispatch marker costs within noise of the pre-refactor baseline.

## Documentation

- Cross-link the implemented pattern from
  [coding-standards.md](../../Docs/coding-standards.md) "System Encapsulation": cite the five
  new singletons as the canonical examples alongside `TargetSpatialHashSingleton`.
- If any `GetExistingSystemManaged` is intentionally retained (e.g. `CombatApplyBridge`),
  add a one-line justification comment at the call site so future reviewers don't flag it.

## Acceptance criteria

- All checks above pass.
- Coding standard references real, landed singletons.
- Branch is behavior- and performance-neutral versus baseline.

## Scope estimate

Small (verification + doc), but gated on a clean full-suite + profiler pass.

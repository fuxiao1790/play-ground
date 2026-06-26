# 006 — Compiler registration walk + translator invocations

## Goal

Register a command template for every compiled spawn (root + each follow-up +
detonation), store the returned keys on the runtime defs / follow-up slots, and
make firing a skill emit a root `SpawnInvocation`. Enforce the depth cap.

## Changes

- [PlayerSkillDriver.cs](../../Assets/Scripts/Skills/PlayerSkillDriver.cs):
  extend the existing registration walk (alongside `RegisterProjectileTypes` /
  `RegisterAoeTypes` / interval template registration) to call
  `CombatRoot.RegisterSpawnTemplate` for the root cast and every
  `RuntimeProjectileDefinition` / `RuntimeAoeDefinition` / `RuntimeStackingDetonation`
  reachable in the compiled tree. Store the returned `Hash128` on the def and on the
  parent's `OnHitSpawnRef` / `StackEffect` detonation key.
- Enforce `MaxSpawnChainDepth = 3`: refuse to register a 4th level; emit a
  validation warning (reuse the `SkillValidationWarning` channel) and drop the
  deeper link, replacing the per-type "flat snapshot drops nested chain" warning in
  [SkillSetCompiler.cs](../../Assets/Scripts/Skills/SkillSetCompiler.cs).
- [SkillSpawnTranslator.cs](../../Assets/Scripts/Skills/SkillSpawnTranslator.cs):
  `Spawn(...)` builds a root `SpawnInvocation` (kind + root template key + origin +
  aim) instead of constructing fat requests/snapshots. Delete the
  `BuildProjectileDetonationBurstSnapshot` / `BuildImpactAoeSnapshot` /
  `BuildImpactProjectileSnapshot` / `BuildAoeOnHitSpawnSnapshot` family.
- Registration happens at compile time (managed, pre-tick) — honors the registry
  write-only-external contract, including on loadout recompile.

## Acceptance criteria

- Authored chains register fully, including: lingering AOE -> OnImpactProjectile ->
  projectile -> StackTrigger -> stacking detonation (the originally-broken case),
  fired end-to-end with stack accrual.
- A 4-level chain warns and drops the overflow link; 3 levels work.
- Re-compile on equipment swap re-registers and produces stable keys for unchanged
  behavior (dedup), all before the next tick.

## Dependencies

001, 004.

## Scope

Large. The compile-side counterpart to 005.

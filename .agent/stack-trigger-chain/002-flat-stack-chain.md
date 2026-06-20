# 002 — Flat stack chain: StackStage + carrier + flatten

## Structural role
Defines the plain-data form the stack chain travels in, and the single point where
the compiled `RuntimeAoeDefinition.StackTriggerSetup` tree is flattened into it.
This replaces the single-level `CombatStatusEffectSnapshot` with a bounded,
self-describing chain — the homogeneous analogue of `ProjectileChildSpawnerComponent`.

## Ownership / data flow
- `StackStage` (new value struct, `PlayGround.System.Common`): one chain stage.
  ```
  struct StackStage {
      int   DebuffStatusId;          // this AOE's trigger
      int   StacksPerHit;
      int   StackThreshold;
      int   AoeTypeId;               // what this stage spawns on threshold
      float AoeDamage;
      float AoeLifetimeSeconds;
      float AoeTickIntervalSeconds;
      AoeSpawnGeometry AoeGeometry;  // plain, resolved at root spawn
  }
  ```
- Carrier: a `FixedList…<StackStage>` capped at `MAX_STACK_DEPTH`, plus the
  constant-down-chain `CombatFaction Faction` and `int TargetMask`. This carrier
  is the replacement payload for `CombatStatusEffectSnapshot` (see 003 for where
  it is embedded). `Enabled` ⇔ `Length > 0`.
- Flatten: in `SkillSpawnTranslator`, walk `RuntimeAoeDefinition.StackTriggerSetup`
  → ordered `[stage0, stage1, …]` where `stage_k` carries the trigger params of
  the k-th AOE and the spawn params of the (k+1)-th AOE's target. Geometry via
  `CreateSpawnGeometry()` (already plain data).

## Change
- New `StackStage` struct + `MAX_STACK_DEPTH` constant (alongside
  `CollisionConstants`).
- `SkillSpawnTranslator`: add `BuildStackChain(RuntimeAoeDefinition root, faction, mask)`
  producing the carrier; replace `BuildAoeStackEffectSnapshot`. Truncate at
  `MAX_STACK_DEPTH` and surface a validation signal (below).
- `SkillLoadoutValidator`: add a non-blocking warning when an authored stack chain
  exceeds `MAX_STACK_DEPTH`.

## Structural notes
- The carrier is fully self-describing plain data: no `TypeId → setup` registry,
  no per-hop authoring lookup. Document: "Chain is resolved to plain data at root
  spawn; in-flight entities never read authoring or a registry."
- `MAX_STACK_DEPTH` is the documented fixed-nesting constraint.

## Acceptance criteria
- Flattening the compiled `LingerA → LingerB → Impact` tree yields a 2-stage chain
  `[{trig=A, spawn=B}, {trig=B, spawn=Impact}]`.
- Authoring a chain deeper than `MAX_STACK_DEPTH` produces a
  `SkillValidationWarning`, and the chain is truncated (not crashed).
- Unit-testable without ECS (pure managed flatten).

## Dependencies
001 (correct nested tree to flatten).

## Scope
Small–medium.

# 009 — Runtime definitions, compiler, template registration

**Depends on:** 007, 008. **Scope:** large. **Risk:** medium — touches the compile path all skills use.

## Why

Turns authored `TargetedSkill` assets into registered spawn templates. Mirrors how
`SkillSetCompiler` handles `AoeDefinition` / `LingeringAoeDefinition`.

## New file

`Assets/Scripts/Skills/Runtime/RuntimeTargetedDefinition.cs`

- `RuntimeTargetedDefinition : RuntimeSkillDefinition` carrying the folded values: `Count`,
  `AcquireRadius`, `MaxTargets`, `ChainRadius`, `ChainDamageFalloff`, `ChainDelaySeconds`,
  `LifetimeSeconds`, `TickIntervalSeconds`, `ArmSeconds`, `ManaCost`, `DirectDamageEnabled`,
  `Prefab`, VFX ids, `LinkWidth`, plus the compiled trigger slots:
  - `RuntimeAoeDefinition OnHitAoeSpawnDefinition` / `RuntimeProjectileDefinition
    OnHitProjectileSpawnDefinition` — **v2**, declared but not compiled (requirements §7 says
    targeted-as-source is v2 but must not be blocked).
  - `RuntimeStackingDetonation StackingDetonation` — **v1**, `StackTrigger` works once `Any` widens.
- `LifetimeSeconds == 0` selects the single-hit lane via `TargetedVariant.ChildKindFor`, mirroring
  `AoeVariant.AoeChildKindFor`.

## Changes

`Assets/Scripts/Skills/SkillSetCompiler.cs`

- `BuildRuntime` gains a `TargetedDefinition` / `LingeringTargetedDefinition` branch producing a
  `RuntimeTargetedDefinition`, alongside the existing projectile and AOE branches.
- Numeric folding through `StatModifierAccumulator` (C15):
  - `Damage`, `ManaCost`, `Rate` — existing stats, no change.
  - `acquireRadius` **and** `chainRadius` fold through the existing **`AreaSize`** stat
    (requirements decision 5), so the player's `areaSizeMultiplier` scales a chain's reach. Both
    radii scale together; a build cannot lengthen jumps without widening the initial grab.
  - `count` folds like projectile `Count` / AOE `EchoCount`.
  - **No new `SkillStat` entries.**
- `RecoveryTime = 1 / max(0.01, rate)` as for every other skill.
- **Single-hit fail-safe lifetime:** when `LifetimeSeconds == 0` and `ChainDelaySeconds > 0`, stamp
  the compiled command's lifetime to `maxTargets * chainDelaySeconds` plus a margin, so a walk that
  cannot terminate still expires (task 005).
- Type registration walk: register the prefab sprite and each VFX asset through `CombatRoot`, store
  the returned `TypeId` and `RenderId`, then build a `TargetedSpawnCommand` template and store the
  returned `Hash128` on `SpawnTemplateKey`.
- Extend the existing recursive walk in `SkillDriver` that registers projectile and AOE types so it
  descends into targeted definitions too — including targeted definitions reached as trigger
  effects (task 010).

`Assets/Scripts/Skills/SkillLoadout.cs` / node compile paths

- Targeted sets are valid roots and valid trigger effects. No special casing beyond the tag.

## Acceptance criteria

- EditMode: a `TargetedSkill` with no supports compiles to a `RuntimeTargetedDefinition` whose
  fields match the authored values.
- EditMode: a `LingeringTargetedSkill` compiles with `LifetimeSeconds > 0` and routes to
  `IntervalChildKind.LingeringTargeted`.
- EditMode: `AddedDamageSupport` on a targeted set raises compiled `Damage`; `IncreasedRateSupport`
  lowers `RecoveryTime`.
- EditMode: `areaSizeMultiplier` on the stat snapshot scales **both** `AcquireRadius` and
  `ChainRadius`.
- EditMode: compiling twice with identical values yields the same `SpawnTemplateKey`; changing
  `count` yields a different one.
- EditMode: a single-hit definition with `chainDelaySeconds = 0.2` and `maxTargets = 6` compiles a
  non-zero fail-safe lifetime of at least `1.2`.
- EditMode: a targeted skill's registered `RenderId` is `0` when its prefab has no sprite.
- EditMode: registration is idempotent across recompiles — re-registering the same compiled
  definition does not grow the registry.
- All existing compiler tests pass unchanged.

## Notes

Registry entries are never recycled (existing behaviour), which keeps pooled or in-flight spawners
valid across loadout recompiles. Do not add reference counting here.

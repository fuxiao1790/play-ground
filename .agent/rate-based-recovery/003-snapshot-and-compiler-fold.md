# 003 — Snapshot + compiler fold (core semantic change)

## Goal
Fold the rate through the generic per-stat fold and derive cooldown as `1 / rate`.
Move the player-level speed contribution into the increased bucket.

## Changes

### `Assets/Scripts/Skills/PlayerStatSnapshot.cs`
- Rename field `CastSpeedMultiplier` → `IncreasedRatePercent` (float; `0` = identity).
- Constructor: rename the first parameter `castSpeedMultiplier` → `increasedRatePercent`.
- `Identity`: first argument flips from `1f` to `0f`:
  - `new PlayerStatSnapshot(1f, 1f, 0f, 1.5f, 1f)` → `new PlayerStatSnapshot(0f, 1f, 0f, 1.5f, 1f)`.
- `PlayerStatAggregator.Aggregate` still returns `Identity` (stub) — now `0` increased.

### `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `SnapshotModifiers.Contribute`: add the player rate contribution to the increased
  bucket, alongside the existing Damage/Area Post multipliers:
  ```csharp
  modifiers.AddIncreased(SkillStat.Rate, snapshot.IncreasedRatePercent);
  ```
- Replace `ResolveRecoveryTime`. It no longer needs the snapshot (the player term is
  already in `modifiers`) and no longer special-cases cast speed:
  ```csharp
  private static float ResolveRecoveryTime(float baseRate, StatModifierAccumulator modifiers)
  {
      float rate = modifiers != null
          ? modifiers.Resolve(SkillStat.Rate, baseRate)
          : baseRate;
      return 1f / Mathf.Max(0.01f, rate);
  }
  ```
- Update the call site in `Compile`:
  - `runtime.RecoveryTime = ResolveRecoveryTime(set.Skill.BaseRecoveryTime, compiled.Modifiers, snapshot);`
    → `runtime.RecoveryTime = ResolveRecoveryTime(set.Skill.BaseRate, compiled.Modifiers);`

Ordering note: `SnapshotModifiers.Contribute` runs inside `CompileDefinition` before
`ResolveRecoveryTime`, and both the support increases and the snapshot increase land
in the same `compiled.Modifiers` accumulator, so the single `Resolve(Rate, baseRate)`
sums them.

## Untouched (already internal — confirm no edits needed)
- `RuntimeSkillDefinition.RecoveryTime` (derived cooldown; stays).
- `SkillSlotState` (cooldown tick/clamp; stays).
- `PlayerSkillDriver` slot wiring (`SetRecoveryTime(def.RecoveryTime)`; stays).

## Worked example (matches migrated test math)
- `baseRate = 5`, no modifiers → `rate = 5` → `recoveryTime = 0.2`.
- `baseRate = 2`, one support `+0.5` and player `+0.5` → `increased = 1.0` →
  `rate = 2 * 2.0 = 4` → `recoveryTime = 0.25`.

## Acceptance
- No reference to `SkillStat.RecoverySpeed`, `CastSpeedMultiplier`, or
  `BaseRecoveryTime` remains in `SkillSetCompiler.cs` / `PlayerStatSnapshot.cs`.
- Recovery is `1 / max(0.01, rate)` with the player term folded as increased.

## Dependencies
- 001 (`SkillStat.Rate`, `Skill.BaseRate`), 002 (support).

## Scope
Medium. The behavioral heart of the rework; two files.

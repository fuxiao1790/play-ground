# 013 — Validation warnings

**Depends on:** 009, 010, 011. **Scope:** medium. **Risk:** low.

## Why

Most of these catch authoring mistakes that are **silent at runtime** — the skill compiles, fires,
and quietly does nothing. Two of them are traps this project has already been bitten by.

## Changes

`Assets/Scripts/Skills/SkillLoadoutValidator.cs` and `SkillValidationWarning.cs`

Add the cases below. Severity is **warning** unless marked. Results surface through
`SkillDriver.ValidationWarnings`, the existing path.

### Clamps (clamp the value, then warn)

| Case | Clamp |
|---|---|
| `maxTargets < 1` | 1 |
| `maxTargets > MaxChainTargets` | `MaxChainTargets` |
| `count < 1` | 1 |
| `chainDelaySeconds < 0` | 0 |
| `acquireRadius`/`chainRadius` above `MaxTargetedSearchRadius` | cap |
| `tickIntervalSeconds <= 0` on the interval variant | `MinTickInterval` |

### Errors (block the spawn, not a no-op)

- `acquireRadius <= 0` — the skill can never acquire anything. Blocking beats firing a silent no-op.
- `TargetedPrefab` has a `Hurtbox` child — a physics shape on a non-physics skill means the prefab
  was copied from a projectile or AOE template. Task 008 implements the prefab-side check; this
  task surfaces it as a loadout validation error.

### Silent-failure warnings

- `maxTargets > 1` with `chainRadius <= 0` — the chain can never jump; behaves as single target.
- `chainDamageFalloff <= 0` with `maxTargets > 1` — every link after the first deals zero.
- **`tickIntervalSeconds > lifetimeSeconds`** — fires exactly once; the authored "ticking" behaviour
  never appears. Same trap class as the interval-spawn threshold bug already recorded in project
  memory.
- **`maxTargets * chainDelaySeconds > tickIntervalSeconds`** — later links are cut off by the next
  tick restarting the walk, so the authored chain length is never reached (requirements §13.3).
- **`maxTargets * chainDelaySeconds > lifetimeSeconds`** on the single-hit variant — the instance
  expires before its walk finishes.
- **Interval trigger whose energy threshold exceeds what the source can accrue over its lifetime** —
  the child never spawns. This is the documented interval trap; it applies to targeted children
  unchanged.
- Missing link VFX, or one that does not decode to `VfxDataShape.LineSegment` — this is the shipped
  visual, so its absence means an invisible skill in play. **A debug sprite does not excuse it.**
- Sprite supplied but failing the `BasicAoePrefab` material rules (missing material, no texture,
  instancing disabled, unsupported shader) — reuse those checks, do not re-implement.
- Pulse-AOE source for `TargetedIntervalSpawnTrigger` — no timed-child setup compiled.
- Projectile-only support on a targeted set — existing tag-mismatch path, no new code.

## Acceptance criteria

- EditMode: one test per case above, asserting the warning or error appears in
  `SkillDriver.ValidationWarnings` with the right severity.
- EditMode: `acquireRadius <= 0` blocks the spawn — casting produces no spawn request.
- EditMode: clamped values are actually clamped in the compiled definition, not just warned about.
- EditMode: a fully valid targeted loadout produces **zero** warnings. This is the test that stops
  the warning set from becoming noise everyone ignores.
- EditMode: `tickInterval > lifetime` warns **and** the runtime still behaves as task 005 asserts
  (exactly one walk) — warning and behaviour agree.

## Notes

Validation is advisory except where marked error, matching the existing model: authored
combinations that compile to no-ops warn rather than throw. The two errors here are cases where
firing anyway would waste a cast or run a physics assumption on a non-physics skill.

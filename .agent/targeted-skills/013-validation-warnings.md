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
| `tickIntervalSeconds <= 0` on the interval variant | `MinTickInterval` |

There is **no** upper clamp on `acquireRadius` or `chainRadius`. Search cost is bound by target
count in the region, not by radius, so clamping authored reach would cost content flexibility for
no measured benefit.

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
- **`maxTargets * chainDelaySeconds > tickIntervalSeconds`** (interval variant) — later links are
  cut off by the next tick restarting the walk, so the authored chain length is never reached
  (requirements §13.3).
- **`maxTargets * chainDelaySeconds > lifetimeSeconds`** (interval variant) — the instance expires
  before even one walk completes, so the authored chain length never manifests at all.

  These two are distinct, not one condition phrased twice. A walk can be cut short by the **next
  tick restarting it** (first case) or by the **instance dying** (second). With
  `lifetime = 0.5, tickInterval = 2.0, maxTargets = 6, chainDelay = 0.2`, a walk needs `1.2s`:
  the tick check does not fire (`1.2 < 2.0`) but the lifetime check does (`1.2 > 0.5`).

  **This condition does not exist on the single-hit variant.** `TargetedDefinition` authors no
  `lifetimeSeconds` — `lifetimeSeconds > 0` is the variant discriminator itself — and task 009
  *computes* its fail-safe lifetime as `maxTargets * chainDelaySeconds` plus a margin. The
  inequality is therefore false by construction there, and an earlier draft of this task asserted
  it as a reachable warning. It is not. The single-hit fail-safe is covered by behaviour instead:
  task 005's test that a walk which cannot terminate still expires and returns to its pool.
- **Interval trigger whose energy threshold exceeds what the source can accrue over its lifetime** —
  the child never spawns. This is the documented interval trap; it applies to targeted children
  unchanged.
- Missing link VFX, or one that does not decode to `VfxDataShape.LineSegment` — this is the shipped
  visual, so its absence means an invisible skill in play. **A debug sprite does not excuse it.**
- A spawn / hit / expire / arming effect assigned but `vfxEffectSize <= 0` — those are circular
  effects dispatched at that radius, so they resolve to nothing visible.
- A link VFX assigned but `linkWidth <= 0` — same failure for the segment.
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
- EditMode: a **single-hit** skill with a long `chainDelaySeconds` and a high `maxTargets` produces
  **no** lifetime warning — its lifetime is computed to fit, so a warning here would be
  permanently unreachable noise.
- EditMode: the two interval truncation warnings fire independently — a case that trips the
  lifetime check while passing the tick-interval check, and vice versa.
- EditMode: `tickInterval > lifetime` warns **and** the runtime still behaves as task 005 asserts
  (exactly one walk) — warning and behaviour agree.

## Notes

Validation is advisory except where marked error, matching the existing model: authored
combinations that compile to no-ops warn rather than throw. The two errors here are cases where
firing anyway would waste a cast or run a physics assumption on a non-physics skill.

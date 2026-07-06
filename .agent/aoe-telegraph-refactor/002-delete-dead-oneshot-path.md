# 002 — Delete the dead pulse-one-shot enable-bit reads

## Goal
Remove the unreachable "disabled lifetime = pulse one-shot" logic from the two systems that
still read the lifetime enable bit. Behavior-identical, because nothing in the codebase ever
disables `CombatLifetimeComponent` (all writes are `true`), so `!enabledLifetime` is never true.

## Precondition to verify first
Grep the whole repo for `SetComponentEnabled<CombatLifetimeComponent>` and confirm there is no
`false` write in production code (only `true`). If any producer disables it, STOP — the branch
is live and this task's premise is wrong. (Current state: only `true` writes exist.)

## Changes

1. **[LingeringAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs)**
   `LingeringAoeCollisionJob.Execute`:
   - Remove the `EnabledRefRO<CombatLifetimeComponent> lifetimeEnabled` parameter.
   - Delete the `bool enabledLifetime = lifetimeEnabled.ValueRO;` and the `if (enabledLifetime)`
     wrapper — the hit-gate tick (`hitGate.Remaining -= DeltaTime; ... RepeatHitCooldownSeconds`)
     now always runs (it always did: `enabledLifetime` was always true).
   - Pass the `deactivateAfterPass` argument to `AoeCollisionCore.RunCollision` as constant
     `false` (was `!enabledLifetime`, always false for lingering — lingering AOEs die via
     lifetime expiry in `CombatLifetimeSystem`, never after a single pass).
   - Remove the `[WithPresent(typeof(CombatLifetimeComponent))]` job attribute and its comment
     (lines 125-127). The `WithAll<LingeringAoeTag>` from 001 already selects the lingering set;
     the job no longer needs the component present at all in this system.
   - Note: after this, `LingeringAoeCollisionJob` no longer references `CombatLifetimeComponent`.

2. **[AoePulseVfxSystem.cs](../../Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs)**
   `AoePulseVfxJob.Execute`:
   - Remove the `EnabledRefRO<CombatLifetimeComponent> lifetimeEnabled` parameter and the
     `|| !lifetimeEnabled.ValueRO` term (and its comment, line 56). The guard becomes just
     `if (pulseVfx.Interval <= 0f) return;`.
   - **Keep** the `ref AoePulseVfxComponent pulseVfx` parameter — it is what makes this job
     lingering-only (impacts lack `AoePulseVfxComponent`). Verify the job still compiles as
     lingering-only after the lifetime param is gone.

## Acceptance criteria
- Project compiles.
- Grep: no `EnabledRefRO<CombatLifetimeComponent>` / `EnabledRefRW<CombatLifetimeComponent>`
  remain in AOE systems.
- Lingering AOE tests: repeat-hit cadence and lifetime-expiry death unchanged; pulse VFX cadence
  unchanged; impacts still emit no pulse VFX.
- Impact AOE tests: single contact pass + deactivate unchanged.

## Scope / complexity
Low. Two job signatures + a couple of deleted branches. No new types.

## Dependencies
Depends on 001 (the lingering query now keys on `LingeringAoeTag`, so dropping the
`WithPresent`/`EnabledRef` on the component is safe). Precedes 003.

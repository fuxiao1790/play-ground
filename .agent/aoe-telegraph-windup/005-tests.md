# 005 — Windup tests

## Goal
Lock the windup contract with headless simulation tests (no VFX art required) plus a telegraph-emit
assertion.

## Tests to add (in `Assets/Tests/PlayMode/`, alongside `AoeSimulationTests`)

1. **Zero-delay identity.** Spawn impact + lingering with `InitialDelaySeconds == 0`; assert
   `AoeWindupComponent` is present-but-disabled and every other enable state (`Active`,
   `AoeCollisionActiveTag`, `TimedSpawnComponent`, `CombatRenderActiveTag`) exactly matches the
   pre-windup baseline. Assert first-frame collision behaves as today.

2. **Suppression during windup.** Spawn with `InitialDelaySeconds = D > dt`; tick one frame; assert:
   `AoeWindupComponent` enabled, `Active` enabled, `AoeCollisionActiveTag` disabled, render enabled,
   no hit events produced against an overlapping target, lingering `CombatLifetimeComponent.Remaining`
   unchanged (frozen), `TimedSpawnComponent` disabled (no interval children emitted), no pulse VFX.

3. **Activation timing.** Tick past `D`; assert `AoeWindupComponent` disabled, `AoeCollisionActiveTag`
   enabled iff `NeedsCollision`, `TimedSpawnComponent` enabled iff `HasTimedSpawner`, and that a hit is
   produced against an overlapping target on the activation frame/next collision pass. Assert lingering
   lifetime now counts down.

4. **Impact inert-guard.** Spawn a visual-only impact (`NeedsCollision == false`) with
   `InitialDelaySeconds > 0`; assert it does **not** enter windup (born inert as today), so it cannot
   become an immortal never-detonating entity.

5. **Pool-reuse reset.** Reuse a dead slot that previously held a windup AOE for a `InitialDelaySeconds
   == 0` command; assert no stale windup (`AoeWindupComponent` disabled, `Remaining == 0`). And the
   reverse: reuse a non-windup slot for a windup command; assert windup correctly enabled.

6. **Telegraph emit.** With a registered trigger-4 effect and `InitialDelaySeconds > 0`, assert the
   expansion enqueues exactly one trigger-4 `VfxPendingSpawn` (plus the usual trigger-0), and zero
   trigger-4 when `InitialDelaySeconds == 0`. (Mirror the existing spawn-VFX emit test.)

## Acceptance criteria
- All new tests pass; full existing projectile + AOE suites remain green.

## Scope / complexity
Medium. Reuses `AoeSimulationTests` harness patterns for spawning + ticking + querying enable state.

## Dependencies
001-004.

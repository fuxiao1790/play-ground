# 006 — Tests

## Goal
Lock the windup contract and guard the `delay == 0` no-regression path.

## Where
Extend the existing AOE sim tests: `Assets/Tests/PlayMode/AoeSimulationTests.cs`
(and `AoePlayModeTests.cs` if a play-mode entity assertion is easier there). Reuse
their AOE-spawn harness; add `InitialDelaySeconds` to the command/request they build.

## Cases
1. **Impact damage suppressed during windup** — spawn an impact AOE with
   `InitialDelaySeconds = D` over a target in range. Assert: 0 hit events for
   frames while elapsed `< D`; exactly one hit pass on the frame the delay elapses;
   entity deactivates after.
2. **Lingering lifetime not consumed during windup** — spawn a lingering AOE with
   `lifetimeSeconds = L`, `InitialDelaySeconds = D`. Assert: no hits and no interval
   children before activation; `CombatLifetimeComponent.Remaining` still `== L` at
   activation (did not tick during windup); AOE then lives ~`L` and ticks/spawns.
3. **Entity + render exist during windup** — assert the AOE entity is `Active` and
   `CombatRenderActiveTag` enabled while `AoeDelayComponent` is enabled (sprite
   shows), with `AoeCollisionActiveTag` disabled.
4. **Telegraph emitted at spawn** — with a registered trigger-`4` VFX (or by
   asserting the staged `VfxPendingSpawn` in a dispatcher-level check like existing
   VFX tests), confirm exactly one trigger-`4` event per echo at spawn and none for
   `delay == 0`.
5. **`delay == 0` regression** — existing AOE tests must pass unchanged; add an
   explicit `InitialDelaySeconds = 0` assertion that behavior/first-frame hits match
   the pre-change path.
6. **Pool reuse across windup/immediate** — spawn a windup AOE, let it activate and
   die, then reuse the slot for an immediate (`delay == 0`) AOE and confirm
   `AoeDelayComponent` is disabled and collision fires immediately (and vice versa).

## Acceptance criteria
- All new cases pass; full existing AOE suite stays green.
- Run under `[BurstCompile(CompileSynchronously = true)]`-equivalent fixture setup
  if the harness already forces synchronous compile (see
  `reference_ijob_run_not_burst`), so timing assertions aren't skewed by async warmup.

## Dependencies
001–005.

## Scope
Medium.

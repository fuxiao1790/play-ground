# 003 — Materialize windup state at spawn (both archetypes, both paths)

## Goal
Add `AoeWindupComponent` to both AOE archetypes and set the windup entry state from
`cmd.InitialDelaySeconds` in all four materialization sites (impact reuse-job + ECB reset, lingering
reuse-job + ECB reset). `InitialDelaySeconds <= 0` stays byte-identical to today.

## Windup entry rule (shared)

```
windup = cmd.InitialDelaySeconds > 0f
         && (LingeringAoeTag present || NeedsCollision(cmd));   // impact guard: no windup on inert impact
AoeWindupComponent = {
    Remaining        = windup ? cmd.InitialDelaySeconds : 0f,
    ActivateCollision = NeedsCollision(cmd),
    ActivateTimedSpawn = HasTimedSpawner(cmd),   // lingering only; impact stores but ignores
};
SetEnabled<AoeWindupComponent> = windup;

if (windup) {
    Active               = true;    // occupancy — must stay on during windup
    AoeCollisionActiveTag = false;   // suppressed until activation
    TimedSpawnComponent   = false;   // lingering: suppressed until activation
    CombatRenderActiveTag = true;    // sprite visible during windup
} else {
    // exactly today's values (unchanged)
}
```

## Changes

1. **Archetypes** in [AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs):
   add `typeof(AoeWindupComponent)` to `_impactArchetype` ([:33](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L33))
   and `_lingeringArchetype` ([:242](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L242)).

2. **Impact reuse job** `ImpactAoeSpawnJob` ([:189-214](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L189)):
   add a `WindupHandle` + enabled-mask; apply the entry rule. Note impact's current
   `activeMask[i] = collisionEnabled` becomes: `activeMask[i] = windup ? true : collisionEnabled`;
   `collisionActiveMask[i] = windup ? false : collisionEnabled`;
   `renderActiveMask[i] = windup ? true : collisionEnabled`. (Impact guard makes `windup` false when
   `!NeedsCollision`, preserving the inert-visual-only path.)

3. **Impact ECB reset** `RecordImpactReset` ([:466-486](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L466)):
   set `AoeWindupComponent` + `SetComponentEnabled<AoeWindupComponent>` and the same
   Active/collision/render enable pattern.

4. **Lingering reuse job** `LingeringAoeSpawnJob` ([:420-456](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L420)):
   add windup handle/mask; apply entry rule. Lingering currently sets
   `collisionActiveMask[i] = collisionEnabled; renderActiveMask[i] = true; timedSpawnMask[i] = hasTimedSpawner`.
   Under windup: `collisionActiveMask[i] = false; timedSpawnMask[i] = false;` (render stays true;
   `Active` already true).

5. **Lingering ECB reset** `RecordLingeringReset` ([:488-515](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L488)):
   same pattern; `SetComponentEnabled<AoeCollisionActiveTag>` / `<TimedSpawnComponent>` become `false`
   under windup.

6. **Reset helper** — add `AoeSpawnApplyUtility.WindupFor(cmd)` returning the `AoeWindupComponent`
   value + a `bool` windup flag, so the four sites share one source of truth (mirror
   `PulseVfxFor`/`HasTimedSpawner`).

## Acceptance criteria
- Compiles; land together with 001 (the `WithDisabled<AoeWindupComponent>` filters now have the
  component present).
- `InitialDelaySeconds == 0`: pool reuse and cold-create produce identical component/enable state to
  pre-change (assert in tests, 005).
- `InitialDelaySeconds > 0`: entity born `Active` on, collision off, render on, timed off, windup
  enabled with correct `Remaining`/flags; a reused slot never inherits a stale windup.

## Scope / complexity
Medium–high. Four near-parallel materialization sites; the shared `WindupFor` helper keeps them in sync.

## Dependencies
001 (component + system), 002 (`cmd.InitialDelaySeconds`). Green-checkpoint with 001.

# 004 - Lifecycle Tick And Death Commit

## Goal

Replace immediate lifetime expiry with a lifecycle tick that computes armed time
for the frame, transitions `Arming -> Armed`, and commits death only after
armed systems can consume the frame's armed delta.

## Required Behavior

Large frame example:

```text
ArmingRemaining = 0.10
LifetimeRemaining = 0.35
DeltaTime = 0.35
```

The entity transitions to `Armed`, gets `CombatArmedDeltaComponent.Value = 0.25`,
armed systems consume that 0.25 seconds, then death may be committed if lifetime
is exhausted.

## Scope

- Replace or split `CombatLifetimeSystem` into:
  - lifecycle tick: subtract lifetime/arming time, compute armed delta, derive
    gates on transition.
  - death commit: disable `Active`, sprite gate, collision gate, timed-spawn
    gate, arming VFX state/request.
- Preserve expire VFX emission currently owned by lifetime/collision deactivation.
- Provide shared deactivate helpers used by:
  - projectile lifetime expiry
  - projectile pierce/exhaustion death
  - AOE lifetime expiry
  - impact AOE post-collision death
  - invalid faction death
- Keep systems domain-tagged.
- Ensure death commit ordering lets timed spawn, pulse VFX, and collision consume
  positive armed delta in the same frame before the entity becomes reusable.

## Acceptance Criteria

- `Active` is not disabled until death commit.
- An entity crossing arming and expiring in one large frame still exposes armed
  delta to armed systems.
- Dead entities end with all derived gates disabled.
- Expire VFX behavior remains equivalent for current zero-arming content.
- Same-frame dead-slot reuse remains intentional and tested.

## Dependencies

002 and 003.

## Estimated Scope

High. This is the behavioral center of the refactor.

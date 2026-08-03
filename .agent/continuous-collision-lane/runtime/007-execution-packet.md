# Task Execution Packet

## Task

007-continuous-collision-system.md

## Goal

Add swept collision lane using full-motion oriented box, nearest-first fixed candidate list, impact-position hit emission, and existing hash/narrowphase/dispatch lanes.

## Files Allowed To Modify

- `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs`

## Files Allowed To Create

- `Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs`

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- Discrete collision system, collision math/sweep math, target spatial hash singleton/system, hit/spawn dispatch singleton components, contact-gate system.

## Behavior To Preserve

- Discrete collision/hit semantics and existing collision-to-finalize ordering/dispatch contracts.

## Behavior To Change

- Extract shared hit emission helpers with explicit impact position; add swept full-path detection and impact snap on expiry.

## Relevant Global Context

- No clamp/config/new broadphase/new overlap solver. Full segment only.
- Require projectile domain + swept tag + active collision tags; exclude arming.
- Existing target cells are center-cell only; expand swept AABB by `MaxTargetRadius`.
- Fixed stack candidate storage, dedupe indices, retain nearest when full, insertion sort ascending t.
- Fetch four dispatch lanes through `GetSingletonRW`; combine scheduled handle into all producers and target-hash consumer.

## Dependencies Confirmed

- Sweep math and swept mover/archetype exist.
- Discrete collision system already owns exact early-out/dispatched event semantics, shared spatial hash, four producer lanes.

## Step-By-Step Instructions

1. Extract listed hit/gate/deactivate helper operations from discrete collision into one internal static helper accepting explicit impact position; discrete passes current position.
2. Add swept sibling system same ordering/handle wiring; `GetSingletonRW` lane fetches fail loudly.
3. Build full swept box; derive world AABB, expand hash query by target radius, gather/dedupe existing narrowphase hits into fixed array.
4. Retain nearest candidates under cap; insertion-sort ascending t. Emit hits/on-hit spawns/gates in that order at lerped impact.
5. On pierce exhaustion, set kinematic position impact before deactivate.

## Acceptance Criteria

- Full path only; rectangle through existing Hit; no allocation/new overlap.
- Nearest first, deduped, correct child impact/snap.
- All dependency handles wired; existing hash + radius expansion; shared helper used two lanes.

## Validation Required

- Static structural searches/diff check; compile baseline may remain blocked. Task 008 supplies tests.

## Hard Boundaries

- No changes outside listed files. Do not alter hash build or tracking path.

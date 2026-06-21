# 006 — AOE on-hit-spawn link (composition)

## Change kind: add

## Structural role
Restores multi-stage composition (e.g. three stacking explosions in sequence) using an
ordinary typed spawn link, NOT the stack mechanic. A detonation AOE, on hit, spawns the
next stacking skill's applicator.

## Change
- Add an on-hit-spawn typed link for AOE → spawn (AOE/applicator), mirroring the existing
  bounded snapshot links (`AoeProjectileBurstSnapshot`, `ProjectileImpactAoeSnapshot`).
  The AOE carries a single-level spawn snapshot; on hit it emits the corresponding spawn
  event through the normal expansion/apply path.
- Compatible-tag validation like the other links; non-blocking warnings.

## Structural notes
- This is the only "linking" in the stacking feature, and it is the same family as existing
  cross-domain spawn links — bounded, snapshot-carried, no mid-game retarget ambiguity
  because each spawn is a fresh snapshot.
- Keep depth bounded by snapshot shape (no recursive struct); document the cap.

## Acceptance criteria
- A detonation AOE with an on-hit-spawn link spawns the next applicator on hit.
- Three stacking skills compose into a working sequence via these links.

## Dependencies
004 (detonation spawning must exist).

## Scope
Medium.

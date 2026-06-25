# 006 — Docs

**File:** `Docs/reference/game-logic/skill-system.md`
**Depends on:** 001–004
**Scope:** trivial

## Change

Update the `OnImpactProjectileTrigger` section (and the compile-pseudocode if relevant) to state
that an AOE-source on-hit projectile now carries its own terminal impact, so a 2-link chain
`AOE → OnImpactProjectile → projectile → trigger2 → skill2` fires `skill2` on the projectile's hit.

Replace the previously documented limitation ("From an AOE source the burst is a flat
`AoeProjectileBurstSnapshot`, so the spawned projectile's own impact AOE/projectile chains cannot
fire") with the new behavior, and note the remaining bound: only **one** further trigger (the
2-link rule); a 3rd trigger off the burst projectile's impact is not represented.

## Acceptance criteria

- Docs match the implemented behavior and the 2-link bound.

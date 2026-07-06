# 002 — Unify despawn behind one `Kill` helper

## Goal

Collapse the four scattered despawn gate-drop sites into one shared helper so
"what dying does to gates" has a single definition. Behavior-identical.

## Current death sites (all drop the same gates + expire VFX)

- [CombatLifetimeSystem.cs](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs)
  — `ProjectileLifetimeJob` and `AoeLifetimeJob`: on `Remaining <= 0` disable
  `Active`, emit `Trigger = 2` expire VFX. (AOE job also cleared the old collision
  bit — now `CombatCollisionActiveTag`.)
- [ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs)
  — `Deactivate(...)`: sets `lifetime.Remaining = 0`, disables `Active`, emits
  expire VFX. Called on faction-none, lifetime-expired, pierce-exhausted.
- [AoeCollisionCore.cs](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs)
  — `Deactivate(active, collisionActive)`: disables `Active` +
  `CombatCollisionActiveTag`. Called on faction-none and `deactivateAfterPass`
  (impact one-shot).

## Approach

- Add one shared static helper (in `System.Common`, e.g. `CombatDeathUtility.Kill`)
  that takes the enable refs it must drop (`EnabledRefRW<Active>`, optional
  `EnabledRefRW<CombatCollisionActiveTag>`) plus the expire-VFX parameters, and
  performs the disable(s) + optional `Trigger = 2` enqueue in one place.
- Because the two collision tags are already merged, both projectile and AOE death
  drop the same `CombatCollisionActiveTag`, so the helper's gate set is uniform.
- Replace each site above with a call to the helper. Keep each site's existing
  domain-specific pre-work (projectile sets `lifetime.Remaining = 0`; AOE
  faction-none path).
- Keep VFX parameter plumbing (TypeId/Position/AreaSize) as passed args; the helper
  does not reach for singletons.

## Acceptance criteria

- Exactly one definition of the despawn gate-drop (+ expire VFX); the four sites
  call it.
- Compiles; existing PlayMode suite passes unchanged: lifetime expiry, projectile
  faction-none / pierce-exhausted despawn, impact AOE one-shot deactivate, AOE
  faction-none.

## Scope

Small–medium. One new helper + four call-site swaps.

## Dependencies

None. Independent of 001 and 003. (003 will later add an `ArmingTag` disable to this
helper if it exists; if 002 is not done, 003 is still safe — see index design
validation.)

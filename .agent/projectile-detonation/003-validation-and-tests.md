# 003 — Validation, tests, docs

## Change kind: add

## Validation
- Remove any gate that warned/blocked `detonationKind = Projectile` (added earlier to mask the
  no-op). With 002 the path is real, so the gate must go — leaving it would be misleading.
- Keep the `BuildDetonationSpawn` `default` assert/log as the structural guard against a future
  unhandled kind.

## Tests
1. **aoe → projectile** (PlayMode): an AOE/lingering applicator builds X stacks on a mob →
   at threshold a projectile nova fires; `Count == SummedProjectileCount`, total damage
   `== SummedDamage`.
2. **projectile → projectile** (PlayMode): a projectile applicator builds X stacks → nova fires.
   Confirms the applicator kind is irrelevant to detonation.
3. **Fizzle still holds** (EditMode/PlayMode): a projectile-detonation entry below threshold
   that lapses is discarded — no nova.
4. **No silent kind** (EditMode): an unhandled/`None` detonation kind does not produce a spawn
   and trips the guard rather than vanishing.

## Docs
- Update `Docs/game-logic/skill-system.md` and `Docs/snapshotting.md`: detonation supports AOE
  and projectile; projectile reuses the burst path; contribution maps to nova count/damage.

## Acceptance criteria
- Both projectile rows of the detonation matrix fire; tests green.
- No reachable detonation kind is a silent no-op.

## Dependencies
002.

## Scope
Small–medium.

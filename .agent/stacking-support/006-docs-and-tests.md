# 006 — Docs and tests

## Change kind: add

## Docs
- Rewrite the stacking authoring section in `Docs/game-logic/skill-system.md`:
  - a stacking detonation = a normal skill set + a `StackingSupport` (config on the support);
    the support makes the set triggered-only.
  - composition = an applicator set wired to the stacking set by a `StackTrigger` link;
    `SetA → trigger → SetB → StackTrigger → StackSet`.
  - the applicator is any normal skill and composes with all existing links.
  - replace the `StackingSkill`/`OnAoeHitSpawn`-wrapper guidance.
- Update `Docs/snapshotting.md` if the applicator's stack payload field names changed.

## Tests
1. **Full chain** (PlayMode): `SetA → OnImpactAoe → SetB(applicator) → StackTrigger → StackSet`.
   SetB builds X stacks on a mob → StackSet detonates with summed contribution.
2. **Triggered-only** (EditMode): a set with a `StackingSupport` is not a player-cast root.
3. **Fizzle** (PlayMode): below-threshold lapse discards the partial debuff — no detonation.
4. **Composition fits normal links** (EditMode): an applicator set is accepted as the target
   of `ChildSpawn`/`OnImpactAoe`/`OnImpactProjectile` while also carrying a stacking payload.
5. **Validation** (EditMode): unreached stacking set, `StackTrigger` to a non-stacking set,
   and stacking set under a normal link each warn.

## Acceptance criteria
- Docs describe the support+trigger model only; all tests green.
- The chain that motivated this redesign (`… → projectile → applicator → StackTrigger → stacking`)
  works.

## Dependencies
005.

## Scope
Medium.

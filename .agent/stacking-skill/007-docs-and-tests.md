# 007 — Docs and tests

## Change kind: add

## Docs
- Update `Docs/game-logic/skill-system.md`: remove `OnStackTrigger` as a trigger link;
  document `StackingSkill` (applicator + detonation + threshold + lifetime), the
  registration-derived debuff key, fizzle expiry, contribution-sum detonation, and
  composition via the on-hit-spawn link.
- Update `Docs/snapshotting.md` §Stack Effect Resolution to the single-level applied-stack
  payload and the id-keyed target buffer; note the debuff key is registration-derived.

## Tests
1. **Detonate at threshold** (PlayMode): an applicator builds X stacks on a mob → exactly
   one detonation with the summed contribution; entry cleared.
2. **Fizzle** (PlayMode/EditMode on accrual): stop refreshing below threshold → lifetime
   lapses → no detonation, entry removed.
3. **Contribution sum under supports** (EditMode): per-stack contributions snapshot at fire
   time; changing a support mid-build changes only subsequent stacks; total = sum.
4. **Independence / no collision** (EditMode): two distinct `StackingSkill` instances get
   distinct registration keys and accumulate independently — no authored name involved.
5. **Composed chain** (PlayMode): three stacking explosions linked by on-hit-spawn fire in
   sequence.

## Acceptance criteria
- Docs match the shipped design; all tests added and green.

## Dependencies
005, 006.

## Scope
Medium.

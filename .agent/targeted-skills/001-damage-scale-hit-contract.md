# 001 — `CombatHitEvent.DamageScale`

**Depends on:** nothing. **Scope:** small. **Risk:** touches the damage contract every domain uses.

## Why

A targeted chain emits N hits at N damages from **one** source entity, but `CombatHitPayload` is
one component per entity. There is no per-hit damage channel today. Rejected alternatives are in
[index.md §2](./index.md#2-rationale-for-major-decisions).

## Change

`Assets/Scripts/System/Application/CombatHitEvent.cs`

- Add `public float DamageScale;` to `CombatHitEvent`.

`Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`

- In `FinalizeCombatSingleJob.Execute`, apply the scale to the base amount before the crit roll:

  ```csharp
  float scale = hit.DamageScale <= 0f ? 1f : hit.DamageScale;
  float baseAmount = math.max(0f, payload.DamageAmount * scale);
  ```

  Treating `0`/unset as `1` keeps every existing producer correct without touching it — a default
  `CombatHitEvent` still deals full damage.

- Crit ordering is unchanged: the scale multiplies the base amount, then the crit multiplier
  applies to the scaled value. A crit on a 50%-falloff link is 50% of a full crit.

## Explicitly not in scope

Do **not** update existing producers to pass `1f`. The zero-means-one rule exists so this task is
a pure addition with no call-site churn.

## Acceptance criteria

- Every existing EditMode and PlayMode combat test passes unchanged.
- New EditMode test: two hits on one target from one source, `DamageScale` `1.0` and `0.5`, with
  crit chance `0` — total damage is `1.5x` the payload amount.
- New EditMode test: a `CombatHitEvent` constructed without setting `DamageScale` deals the full
  payload amount (guards the zero-means-one rule).
- New EditMode test: with crit chance `1.0` and crit multiplier `2.0`, a `0.5`-scaled hit deals
  `payload * 0.5 * 2.0`.

## Notes

`Docs/coding-standards.md` forbids widening damage events with **spawn-routing** fields. This is a
damage field on a damage contract, which is the permitted direction. Task 015 records the addition
in `Docs/contracts/combat-hit-and-tick-results.md`.

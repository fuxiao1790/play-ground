# 001 — Support taxonomy

## Change kind: adapt

## Structural role
Splits "support" into two categories so a stacking support can change *what a skill
is* without bending the stat-modifier contract. This is the foundation the stacking
support sits on.

## Ownership / data flow
- New abstract `SkillSupport : ScriptableObject` (common base).
- `AdditiveSupport : SkillSupport` — keeps the existing `Apply(SkillDefinition)`
  stat/field-mutation contract; existing support assets reparent unchanged.
- `ConversionSupport : SkillSupport` — abstract; declares
  `bool ConvertsToTriggeredOnly { get; }` and a compile hook (no stat mutation).
- `SkillSet.supports` retyped from `AdditiveSupport[]` to `SkillSupport[]`
  (field name unchanged so serialized references resolve).
- `SkillSetCompiler` dispatches by category: additive supports call `Apply`;
  conversion supports take a separate compile branch (a no-op hook here — populated in 002).

## Phase/order
Equip-time compile. No runtime change.

## Structural notes (focus items 1, 3)
- This is a structural split, not a stat tweak — `AdditiveSupport`'s contract is left
  intact; conversion behavior lives in its own category rather than overloading `Apply`.
- Keep `ApplySupports` iterating only additive supports; conversion supports are handled
  explicitly by the compiler, not via `Apply`.

## Acceptance criteria
- Existing skills with additive supports compile and behave identically.
- `SkillSet.supports` holds both categories; serialized references to existing
  `AdditiveSupport` assets still resolve.
- A `ConversionSupport` present in a set is reachable to the compiler (even though it
  does nothing yet).

## Dependencies
None. Independently green.

## Scope
Medium (touches the support base, `SkillSet`, and the compiler's support loop).

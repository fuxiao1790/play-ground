# 004 — Root detection + validation

## Change kind: adapt

## Structural role
Enforces "stacking sets are triggered-only" and surfaces misauthored stacking wiring,
replacing silent failure with explicit warnings.

## Ownership / data flow
- Root detection ([PlayerSkillDriver.CompileAndRegister](Assets/Scripts/Skills/PlayerSkillDriver.cs#L81)):
  a set is **not** a player-cast root if it is a chain effect **or** any of its supports is a
  `ConversionSupport` with `ConvertsToTriggeredOnly`. This is the "different tags → triggered
  only" rule, implemented as a support-presence check.
- Validation ([SkillLoadoutValidator](Assets/Scripts/Skills/SkillLoadoutValidator.cs)):
  - warn if a stacking set (has `StackingSupport`) is **not** the effect of a `StackTrigger`
    (it would never fire);
  - warn if a `StackTrigger`'s effect set has **no** `StackingSupport` (nothing to bake);
  - warn if a stacking set is targeted by a non-`StackTrigger` link (rejected, won't wire).

## Phase/order
Compile-time, non-blocking warnings (existing validation model).

## Structural notes (focus item 7)
- The unreached-stacking-set case is the silent no-op risk; validation makes it visible.
- Root detection by support-presence avoids inventing a tag enum value while giving the same
  "manual cast disabled" guarantee.

## Acceptance criteria
- A set with a `StackingSupport` is never bound to player input.
- Each misauthoring case above produces a `SkillValidationWarning`.

## Dependencies
003.

## Scope
Small–medium.

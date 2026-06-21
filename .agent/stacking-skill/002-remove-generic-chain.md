# 002 — Remove the generic stack link and chain

## Change kind: remove

## Structural role
Deletes the fragile generic-link machinery now that the stacking mechanic is intrinsic to
`StackingSkill`. This is the "restructure, don't keep a compatibility layer" step.

## Remove
- `OnStackTrigger : TriggerLink` and its asset(s); any slot parsing / tag validation
  specific to it.
- `StackChainSnapshot` stages/tail and `StackStage` chain semantics (the multi-hop carrier).
- The stack-recursion branch in `SkillSetCompiler` and `RuntimeStackTriggerSetup` chaining;
  the flatten in `SkillSpawnTranslator` (`BuildStackChain` tail walk).
- `StackAccrualSystem` tail-forwarding (`Tail(evt)` → spawned AOE's chain).

## Collapse
- `StackApplyEvent` becomes single-level:
  `{ Entity TargetProxy; int DebuffKey; int Threshold; float Lifetime; StackContribution Contribution; DetonationSnapshot Detonation; }`
  where `StackContribution` is the per-stack 1/X share (damage, count, area) and
  `DetonationSnapshot` is the detonation type id + geometry. No `FixedList<StackStage>`.

## Structural notes
- After this task there is no downstream "chain" anywhere; a stacking skill knows only its
  own detonation. Composition is reintroduced via the separate on-hit-spawn link (006).
- Keep the snapshot path (request/event/command/component) but carry the collapsed payload.

## Acceptance criteria
- No reference to `OnStackTrigger`, `StackChainSnapshot.Tail`, or `RuntimeStackTriggerSetup`
  chain recursion remains.
- `StackApplyEvent` carries exactly one level; project compiles.

## Sequencing
Not independently green. The `StackApplyEvent` collapse here only compiles together with the
buffer (003), accrual (004), and emit (005). Lands as one commit with them — see index
*Sequencing & reviewability*.

## Dependencies
001. Atomic with 003–005.

## Scope
Medium (mostly deletion + one struct collapse).

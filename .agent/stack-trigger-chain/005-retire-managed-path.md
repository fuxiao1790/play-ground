# 005 — Retire the managed stack path

## Structural role
Removes the now-dead managed round-trip so there is exactly one owner of
stack-triggered spawning (the ECS accrual phase). Prevents a lingering
"temporary compatibility layer."

## Ownership / data flow
- `MobRoot.ReceiveHit` keeps direct-damage application only.
- Stack-triggered AOE spawning is owned solely by `StackAccrualSystem` (004).

## Change
- `MobRoot.cs`: delete `ApplyStackEffect` and its call site in `ReceiveHit`; drop
  the `aoeCombatRoot` wiring used only for stack spawns (verify no other consumer:
  `OnStatusEffectTriggered` uses `aoeCombatRoot` too — keep that path if the
  status-effect system still needs it; only remove the stack-trigger usage).
- `MobDebuffStackState` / `MobRoot.AddDebuffStacks`/`GetDebuffStackCount`/
  `ClearDebuffStacks`: remove if unused after 004; otherwise keep strictly as a
  managed test/inspection API and document that runtime accrual is ECS-owned.
- Reconcile tests that asserted managed accrual:
  `Assets/Tests/EditMode/MobRuntimeEditModeTests.cs`,
  `Assets/Tests/PlayMode/AoePlayModeTests.cs` — move stack assertions to the ECS
  path (full behavior covered in 006).

## Structural notes
- Do not leave `ApplyStackEffect` behind a flag "just in case." The ECS path is
  the single source of truth after 004.
- If `MobDebuffStackState` survives only for tests, that is acceptable but must be
  explicitly documented as non-runtime.

## Acceptance criteria
- No code path spawns a stack-triggered AOE from managed `MobRoot`.
- `CombatStatusEffectSnapshot`/chain is consumed only by the ECS accrual phase.
- EditMode/PlayMode suites compile and pass (with assertions moved to ECS).

## Dependencies
004 (ECS path must be live before the managed path is removed).

## Scope
Small–medium. Mostly deletion + test reconciliation.

# 006 — Tests

## Structural role
Locks the two behaviors that defined the bug so future logic edits cannot
silently regress them.

## Tests
1. **Compiler same-asset chain (EditMode).** Build a loadout
   `[SetX] [OnStack] [SetX] [OnStack] [SetImpact]` where the two `SetX` slots
   reference the **same** asset. Assert the compiled root is a 2-deep nested
   `RuntimeAoeDefinition` (`X → X' → Impact`), i.e. two independent instances with
   the second carrying its own `StackTriggerSetup → Impact`. Guards against the
   asset-identity collapse (001).

2. **Flatten depth + cap (EditMode).** Flatten a 2-hop compiled tree → assert a
   2-stage chain with correct trigger/spawn fields per stage. Author a chain past
   `MAX_STACK_DEPTH` → assert a `SkillValidationWarning` and truncation (002).

3. **Full chain fires (PlayMode).** Spawn `lingering → stack → lingering → stack →
   impact` against a mob with enough hits to cross both thresholds. Assert all
   three stages occur in order: first lingering accrues and spawns the second
   lingering; the second accrues and spawns the impact; the impact spawns with an
   empty chain and does not re-trigger. Confirm no managed `MobRoot` stack spawn is
   invoked (004 + 005).

4. **Independent statuses (PlayMode or EditMode on accrual).** Two different
   `MobDebuffStatus` chains on the same mob accrue independently and trigger
   separately (regression for the per-status accrual in `TargetStackStateComponent`).

## Acceptance criteria
- All four tests added and green.
- Test 3 fails on `main` (pre-fix) and passes after 001–005.

## Dependencies
004, 005. (Tests 1–2 only need 001–002 and may land earlier with those tasks.)

## Scope
Medium.

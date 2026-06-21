# Stacking Skill — self-contained stack-detonation skill (migration)

## Status: supersedes `.agent/stack-trigger-chain/`

`stack-trigger-chain` (commits 001–005) is implemented: a **generic** `OnStackTrigger`
link, a flat `StackChainSnapshot` carried+forwarded per hop, ECS accrual keyed by the
`DebuffStatus` enum, and the managed path retired. Playtesting exposed that the generic
*link* has irreducible complexity — mid-game retarget, projectile/AOE type mismatch
across the link, deep-chain bookkeeping — and the enum keying is fragile (shared-counter
collisions; the `StatusCount` out-of-range silent drop).

This plan migrates to a **self-contained stacking skill**. The stack trigger stops being
a generic link and becomes intrinsic to a skill that owns its debuff and detonation.
Composition of multiple stacking skills is done with an ordinary on-hit-spawn link, not
the stack mechanic.

## Locked design

A **StackingSkill** owns:
- an **applicator** output (the AOE/projectile the player casts; what hits and applies stacks),
- a **detonation** effect (the explosion fired at threshold),
- a **threshold** `X` and a debuff **lifetime**,
- an optional cosmetic debuff **name** (UI/VFX only — not the key).

Behavior:
- On each applicator hit: apply 1 stack, snapshot this stack's **1/X contribution**
  (damage, projectile count, area) at fire-time stats, and **refresh** the debuff lifetime.
- The target accumulates **summed contributions** per debuff key.
- At `X` stacks: detonate the summed effect, clear the entry.
- Lifetime lapses below `X`: **fizzle** — discard, no detonation.

## Identity model (collision impossible by construction)

The debuff key is a **dedicated id minted per compiled stacking-skill instance during the
registration walk** — held separate from the detonation type id. There is no authored key,
so two distinct stacking skills cannot share one; uniqueness is a property of registration,
not authoring, and no validation pass is needed. Per-instance keying means each set's copy
is independent and self-consistent with its own supports.

Rationale (focus item 8): the key is *dedicated*, not the reused detonation type id, so a
later optimization that dedups or shares detonation visuals across skills cannot reintroduce
key collisions. One id, one responsibility.

## Mid-game safety

A swapped-in skill is a different compiled instance → a different key → a fresh
accumulator. The unequipped skill's stale stacks fizzle on lifetime lapse. Each stack's
contribution and the detonation are snapshotted at apply time, so support/set changes
mid-build stay correct.

## Ownership / phases

| Phase | System | Responsibility |
|---|---|---|
| Simulation | applicator collision (`AoeCollisionCore` / projectile collision) | emit `StackApplyEvent` intent (debuff key + per-stack contribution + detonation snapshot) |
| Aggregation | `StackAccrualSystem` (sole owner of target stack state) | tick lifetimes + fizzle; apply + sum + refresh; detonate at threshold; clear |
| Expansion/apply | existing AOE/projectile pipeline | materialize the detonation |

## Migration tasks

| # | File | Change kind | Summary | Depends |
|---|---|---|---|---|
| 001 | [001-stacking-skill-authoring.md](001-stacking-skill-authoring.md) | add | `StackingSkill` type + compile + registration-assigned debuff key | — |
| 002 | [002-remove-generic-chain.md](002-remove-generic-chain.md) | remove | delete `OnStackTrigger` link, `StackChainSnapshot` chain/tail, compiler stack recursion; collapse `StackApplyEvent` to one level | 001 |
| 003 | [003-id-keyed-target-state.md](003-id-keyed-target-state.md) | adapt | `TargetStackStateComponent` → buffer keyed by debuff id storing summed contribution + lifetime + detonation snapshot; drop enum array/`StatusCount` | 002 |
| 004 | [004-accrual-lifecycle.md](004-accrual-lifecycle.md) | adapt | `StackAccrualSystem` owns tick/fizzle/accumulate/detonate; remove tail-forwarding | 003 |
| 005 | [005-applicator-emit.md](005-applicator-emit.md) | adapt | applicator collision emits debuff key + per-stack contribution | 004 |
| 006 | [006-on-hit-spawn-link.md](006-on-hit-spawn-link.md) | add | AOE on-hit-spawn typed link to compose stacking skills | 004 |
| 007 | [007-docs-and-tests.md](007-docs-and-tests.md) | add | docs update + tests (detonate, fizzle, contribution-sum, independence, composed chain) | 005, 006 |

## Decisions (no open structural questions)

- **Debuff key:** dedicated registration-minted id per compiled instance (see Identity model).
- **Detonation form:** a **kind-tagged** effect — AOE or projectile. The data model
  (`DetonationSnapshot` carries a kind), the generic summed contribution, and the accrual are
  **not** AOE-specific. Only the **AOE** concrete path is implemented and authorable now;
  projectile detonation is a localized later addition — an authoring field plus one dispatch
  case plus the existing projectile spawn event — with **no change to the accrual or buffer**
  (focus item 5). Structure is not limited to AOE; implementation is.
- **Same-skill instances:** per-compiled-instance keying — two sets running the same skill
  with different supports accumulate independently and self-consistently.
- **Carry-over:** none. Threshold = full detonation; partial builds fizzle, no remainder.

## Sequencing & reviewability

- **001, 006, 007 are independently green** and reviewable on their own.
- **002–005 are one atomic data-model migration.** The `StackApplyEvent` shape (002), the
  id-keyed buffer (003), the accrual rewrite (004), and the applicator emit (005) must change
  together to compile; the build is not green between them. They are split into sub-task files
  for review granularity but land as a single commit. Stated, not hidden (focus item 8: expose
  the real structure rather than fake independence).

## Constraints (document in code)

- Single writer: only `StackAccrualSystem` writes `TargetStackStateComponent`. It emits
  detonations to the separate AOE spawn-event channel; it never consumes its own output.
- Debuff key = registration id, never an authored field (collision-impossible).
- Per-stack contribution + detonation are snapshotted at apply time (snapshot purity).
- Target stack buffer is bounded; **size the cap above realistic concurrent stacking skills
  so eviction is not reached in practice. If reached, eviction must be observable (counter),
  never a silent drop** (focus item 7: no misleading safety).
- `DebuffStatus` enum is cosmetic flavor only; it is not the accrual key.

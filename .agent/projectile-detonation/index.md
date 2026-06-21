# Projectile Detonation — complete the stacking-skill detonation matrix

## Problem

Stacking-skill detonation is implemented for AOE only. [StackAccrualSystem.BuildDetonationSpawn](Assets/Scripts/System/Aoe/StackAccrualSystem.cs#L239-L251)
has a single `case StackDetonationKind.Aoe`; `StackDetonationKind.Projectile` falls through
and **silently spawns nothing**. Authoring already exposes `detonationKind = Projectile`
([StackingSkillDefinition](Assets/Scripts/Skills/StackingSkillDefinition.cs#L28)), so this is
a *reachable* silent no-op — it contradicts the "no misleading safety" intent (focus item 7).

Two capability rows are blocked, both by this one gap:

| Applicator | Detonation | Status today |
|---|---|---|
| aoe / lingering | projectile | silent no-op |
| projectile | projectile | silent no-op |

Every applicator already emits stacks (`AoeCollisionCore`, `ProjectileCollisionSystem`).
The fix is entirely on the detonation side, so it unlocks **both rows at once**.

## Goal

Projectile (nova) detonation fires at threshold with the accumulated contribution:
`Count = SummedProjectileCount`, total `Damage = SummedDamage`, using the authored
projectile-detonation params (spread, speed, lifetime, shape, pierce). No silent no-op
remains for any reachable detonation kind.

## Key decision (reuse, not invent)

A projectile detonation is "N projectiles from a point" — which is exactly what the existing
AOE→projectile **burst** path already does. Reuse it:
- The projectile-detonation params ride as an `AoeProjectileBurstSnapshot`
  ([CombatHitElement.cs](Assets/Scripts/System/Common/CombatHitElement.cs)) inside
  `DetonationSnapshot`, used when `Kind == Projectile`.
- At threshold, build that snapshot with the summed count/damage and emit via
  [ProjectileSpawnPipeline.BuildBurstEvent](Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs#L115)
  into `ProjectileSpawnExpansionSystem.EventQueue`.

This keeps one data path (the accrual/buffer/contribution model is unchanged), adds no new
projectile-spawn machinery, and mirrors how AOE bursts already fire projectiles
(focus item 8: clearer data flow, fewer new responsibilities).

## Ownership / phases

| Phase | System | Responsibility |
|---|---|---|
| Compile/register | `SkillSpawnTranslator` / `PlayerSkillDriver` | bake the projectile-detonation burst snapshot; register the detonation projectile template → `TypeId` |
| Aggregation | `StackAccrualSystem` (sole owner of stack state) | at threshold, dispatch on `Detonation.Kind`; for `Projectile`, emit a burst event with summed count/damage |
| Expansion/apply | `ProjectileSpawnExpansionSystem` → projectile apply | materialize the nova (existing path) |

## Constraints (document in code)

- `BuildDetonationSpawn` must handle **every** reachable `StackDetonationKind`; an unhandled
  kind asserts/logs, never silently returns (focus item 7).
- `StackAccrualSystem` stays the single writer of stack state; it emits to the projectile
  expansion queue and never reads it back.
- Order: `StackAccrualSystem` gains `[UpdateBefore(ProjectileSpawnExpansionSystem)]` alongside
  its existing `[UpdateBefore(AoeSpawnExpansionSystem)]`.
- Contribution mapping (`SummedDamage` = total vs per-projectile) is a tunable logic detail —
  document the chosen rule; default: `SummedDamage` is the nova total, split across `Count`.

## Tasks

| # | File | Change | Summary | Depends |
|---|---|---|---|---|
| 001 | [001-bake-projectile-detonation.md](001-bake-projectile-detonation.md) | adapt | carry projectile-detonation params as a burst snapshot in `DetonationSnapshot`; bake + register at fire/compile time | — |
| 002 | [002-accrual-projectile-spawn.md](002-accrual-projectile-spawn.md) | adapt | `BuildDetonationSpawn` `Projectile` case → burst event via `ProjectileSpawnExpansionSystem`; no silent fallthrough | 001 |
| 003 | [003-validation-and-tests.md](003-validation-and-tests.md) | add | drop any projectile-detonation gate; tests for both rows; docs | 002 |

001 and 002 are atomic (shared snapshot shape); 003 follows.

## Out of scope (follow-up)

Composition *from* a projectile detonation (a nova whose projectiles spawn the next stacking
applicator) would use the projectile's existing impact links (`OnImpactAoe`/`OnImpactProjectile`),
not `AoeOnHitSpawn`. Not required to make the two rows fire; tracked separately.

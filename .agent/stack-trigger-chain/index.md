# Stack Trigger Chain — ECS multi-hop stack-triggered AOE

## Problem

A loadout chaining `lingering AOE → stack → lingering AOE → stack → impact AOE`
fires only one stack hop in game (`lingering → stack → impact`). Three distinct
defects, surfaced in order of discovery:

1. **Compile collapse (asset identity).** `TriggerChain`/`SkillSetCompiler` key
   the chain graph by `SkillSet` *asset reference*, not slot position. When the
   same asset fills two slots, both outgoing chains match the one compiled
   instance and the later one overwrites the earlier (`→ Impact` overwrites
   `→ LingerB`). The `selfRef` guard (`chain.effect == set`) misfires on the same
   key. This contradicts the design doc: slots are independent compilation units.
2. **Runtime truncation (single level).** `CombatStatusEffectSnapshot` carries
   exactly one spawned AOE and no field for *that* AOE's own stack effect.
   `BuildAoeStackEffectSnapshot` drops the spawned AOE's `StackTriggerSetup`.
3. **Asymmetric control flow.** The projectile child-spawn follow-up is handled
   entirely inside ECS (`ProjectileChildSpawnerComponent` carries the descendant
   inline; `TimedProjectileSpawnSystem` forwards it into the child). The stack
   trigger instead round-trips to managed `MobRoot.ApplyStackEffect`, which
   forwards nothing into the spawned AOE.

## Goal

Multi-hop stack chains fire every stage, handled the same way as the in-ECS
projectile child-spawn / burst follow-ups: descendant carried as a snapshot,
forwarded per hop, spawned through the normal AOE pipeline — **no managed
round-trip**. Stack accrual becomes an owned ECS phase.

## Architecture decisions

1. **Slot-position identity.** The chain graph keys on slot index, not asset
   reference. Each slot is an independent compilation unit (same asset twice →
   two instances). Forward-only adjacency (`i → i+2`) cannot cycle, so the
   `selfRef` recursion guard is **deleted**, not patched.

2. **Bounded flat chain carried by value.** A `StackStage` value struct, carried
   as a `FixedList…<StackStage>` capped at `MAX_STACK_DEPTH`. Element `[0]` is the
   current AOE's own trigger + what it spawns; the tail `[1..]` is forwarded to
   the spawned AOE. This is the projectile `ProjectileChildSpawnerComponent`
   pattern generalized to homogeneous stages — chosen over distinct nested structs
   precisely because AOE-stack chains run deeper than the value-struct recursion
   wall the projectile path hit (`proj→proj→proj`). Fully resolved to plain data
   at root spawn; no authoring reads in flight.

3. **Stack accrual owned by ECS.** Per-target `TargetStackStateComponent` on the
   target proxy, written by exactly one system (`StackAccrualSystem`). The AOE
   collision job emits a `StackApplyEvent` intent (alongside its existing
   `DamageReplayEvent` / `ProjectileSpawnEvent` emissions) and mutates no target
   state. `StackAccrualSystem` aggregates per (target, status), crosses the
   threshold, and emits an `AoeSpawnEvent` carrying the forwarded tail. **Stacks
   leave the damage path entirely** — `DamageReplayEvent` no longer carries
   `StackEffect`.

## Ownership / phase boundaries

| Phase | System | Responsibility | Must not |
|---|---|---|---|
| Simulation | `LingeringAoeCollisionSystem`, `ImpactAoeCollisionSystem` → `AoeCollisionCore` | Detect hit; emit typed intents (damage, stack, spawn, vfx) | Mutate target state; read authoring |
| Aggregation | `StackAccrualSystem` (new) | Sole owner/writer of `TargetStackStateComponent`; aggregate stacks; cross threshold; emit `AoeSpawnEvent` | Detect collisions; touch damage |
| Expansion/apply | `AoeSpawnExpansionSystem` → `AoeSpawnApplySystem` (existing) | Materialize spawned AOE with its forwarded chain | — |

## Constraints (document in code)

- `MAX_STACK_DEPTH` is a fixed cap; `SkillLoadoutValidator` warns past it.
- Single-writer: only `StackAccrualSystem` writes `TargetStackStateComponent`.
- Phase order: `StackAccrualSystem` `[UpdateAfter]` both AOE collision systems,
  `[UpdateBefore(AoeSpawnExpansionSystem)]`.
- Snapshot purity: the chain is plain data resolved at root spawn; faction +
  target mask travel on the carrier (constant down-chain), sourced from the
  originating AOE's spawn parameters.
- Target lifetime is owned by `MobRoot` (proxy create/delete); stack state dies
  with the proxy. Accrual guards against stale/destroyed proxies with one
  existence check, not scattered defensive copies.

## Tasks

| # | File | Summary | Depends |
|---|---|---|---|
| 001 | [001-compiler-slot-index.md](001-compiler-slot-index.md) | Key chain graph by slot index; drop `selfRef` guard | — |
| 002 | [002-flat-stack-chain.md](002-flat-stack-chain.md) | `StackStage` + bounded carrier; flatten compiled tree; depth-cap validation | 001 |
| 003 | [003-snapshot-plumbing.md](003-snapshot-plumbing.md) | Thread chain through request/event/command/component; remove `StackEffect` from damage path | 002 |
| 004 | [004-ecs-stack-accrual.md](004-ecs-stack-accrual.md) | `TargetStackStateComponent`, `StackApplyEvent`, `StackAccrualSystem`, collision wiring | 003 |
| 005 | [005-retire-managed-path.md](005-retire-managed-path.md) | Remove `MobRoot` stack spawn; reconcile stack API + tests | 004 |
| 006 | [006-tests.md](006-tests.md) | Compiler same-asset test + full-chain PlayMode test | 004, 005 |

Task 001 is independently mergeable and unblocks the rest. 002→003→004 form the
data-and-system spine; 005 removes the old path only after the new one is live;
006 locks behavior.

## Out of scope (follow-up)

Projectile stack follow-ups still cap at one hop (`BuildChildStackEffect` does not
recurse). Once `StackStage`/the carrier exists, the projectile child-spawn and
impact paths can adopt the same carrier for symmetry. Tracked as a follow-up, not
a blocker for this work.

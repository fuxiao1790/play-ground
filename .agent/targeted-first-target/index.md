---
name: targeted-first-target
description: Move first-target acquisition for cast chains into the external spawn gate, so a chain enters the world already holding its first target position
---

# Targeted First Target — Acquisition At Spawn

Follows the shipped design in [targeted-skills](../targeted-skills/index.md); that plan's
constraint numbering (C1–C15) is reused here by reference.

## 1. Summary

Today a chain enters the world with **no target**. `TargetedSpawnApplySystem` seeds
`Origin`, `LinkSource`, `LinkTarget`, and `CombatKinematicsComponent.Position` all to
`cfg.Origin` — the caster — and the first real target only exists one update later, inside
`TargetedResolveSystem.Walk`
([TargetedSpawnApplySystem.cs:167-182](../../Assets/Scripts/System/Targeted/TargetedSpawnApplySystem.cs#L167-L182)).
`LinkTarget = Origin` is a placeholder that reads as a valid pose to render prep, which is the
root cause of the sprite appearing on the caster.

This change makes `ExternalSpawnGateSystem` acquire the first target for cast chains and stamp
it into the spawn event, so the entity materializes already positioned on that target.

Acquisition rule: **nearest hostile proxy to the cast's aim point (the cursor), bounded by the
template's `chainDistance`** — the same reach link 0 already uses.

## 2. Decisions

| # | Decision | Source |
|---|---|---|
| D1 | Acquisition reach is the authored `chainDistance`; no new authored field, and the deleted `acquireRadius` is not revived. | user, this session |
| D2 | Acquisition happens in `ExternalSpawnGateSystem` — cast path only. Interval-child and on-hit chains keep resolve-side acquisition from their impact/source anchor. | user, this session |
| D3 | The gate stamps an **acquired anchor position**, not a target entity. Fork *i* still ranks off that anchor in resolve, so fork 0 lands on the acquired target and `MultipleChainsSupport` keeps its distinct forks. | this plan, §3 |
| D4 | Selection logic is extracted once and shared by the gate and the resolve walk. Two independent nearest-hostile implementations would be a second source of truth for the same rule. | plan-changes reuse-first |

**D3 is the load-bearing one.** The user chose gate-side acquisition, and the gate runs *before*
echo fan-out — it cannot know `echoCount` or a fork's rank, so stamping a target *identity* would
collapse every fork of a multi-chain cast onto one victim. Stamping the anchor instead satisfies
"the spawner provides the first target" (fork 0's nearest-to-anchor *is* the acquired target, at
distance 0) while leaving fan-out ranking untouched. This is the one place the implementation
reads the request more loosely than its literal wording, and the reason is recorded here.

## 3. Data Flow After The Change

```
SkillDriver (cursor)  →  ExternalSpawnRequest.AcquireAnchor = aim world pos
ExternalSpawnGateSystem
    ├─ mana gate (unchanged)
    ├─ template lookup for chainDistance
    ├─ TargetedAcquisition.TryNearestHostile(anchor, chainDistance, faction)
    └─ TargetedSpawnEvent.AcquireAnchor = acquired target position   (HasAcquiredTarget = 1)
                                        or the raw cursor            (HasAcquiredTarget = 0)
TargetedSpawnExpansionSystem   (unchanged — fan-out and ranking as today)
TargetedSpawnApplySystem       chain.LinkTarget / kinematics.Position ← AcquireAnchor
                               chain.LinkSource ← Origin   (bolt still starts at the caster)
                               armed only when HasAcquiredTarget = 0
TargetedResolveSystem          link 0 ranks off AcquireAnchor as today; hops unchanged
```

## 4. Constraints This Change Must Respect

| # | Invariant | Source |
|---|---|---|
| C3 | Events carry gameplay intent, commands carry allocation intent; expansion owns the registry dereference and the multiplicity explosion. The gate may add intent to an event but must not fan out. | `Docs/contracts/spawn-events-and-commands.md` |
| C4 | The spawn-template registry is read-only during the tick. The gate's `chainDistance` lookup is a read. | same |
| C8 | Spatial-hash consumers complete `BuildHandle` before reading the snapshot arrays. The gate is a main-thread `SystemBase`, so it completes and reads; it schedules no job, so it publishes nothing into `ConsumerHandle`. | [TargetSpatialHashSystem.cs:88-99](../../Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs#L88-L99) |
| C9 | Allocation-light: acquisition is a bounded cell walk with a single best-candidate scalar, no container. | `Docs/coding-standards.md` Allocation Rule |
| C10 | Apply runs after resolve; a chain spawned this frame first walks next update. Unchanged — this change only fixes what the entity *holds* in between. | `Docs/architecture/phase-order.md` |
| C12 | `ECS Lifecycle:` comments updated in the same change. | `Docs/coding-standards.md` |
| N1 | Faction rule for acquisition must be identical to hop selection: skip proxies whose faction equals the chain's. | [TargetedResolveSystem.cs:285](../../Assets/Scripts/System/Targeted/TargetedResolveSystem.cs#L285) |

**Ordering check.** `ExternalSpawnGateSystem` gains `[UpdateAfter(TargetSpatialHashSystem)]`. No
cycle: the hash is `UpdateBefore` the tracking/collision systems, the gate is `UpdateBefore` the
four expansion systems, and every expansion system already sorts after those collisions. Proxies
created this frame are included, because `TargetProxyCreateApplySystem` is `UpdateBefore` the hash.

## 5. Mechanisms Reused vs Introduced

Reused: `TargetSpatialHashSingleton.AoeOccupiedCells` and its parallel snapshot arrays,
`CombatCollisionMath` narrowphase, the existing `AcquireAnchor` field on event and command, the
existing arming gate, the template registry read pattern from
`TargetedSpawnExpansionSystem.OnUpdate`.

Introduced:

| New thing | Why it cannot reuse | Task |
|---|---|---|
| `TargetedAcquisition` static (shared nearest-hostile query) | The rule currently lives inside a private job method in the resolve system; the gate needs the identical rule. Extracting is strictly better than a second copy (D4). | 001 |
| `TargetedSpawnEvent.HasAcquiredTarget` / `TargetedSpawnCommand.HasAcquiredTarget` | Apply must distinguish "anchor is a confirmed target" from "anchor is a raw cursor" to decide whether the spawn-frame pose is safe to render. No existing field carries that. | 002 |

## 6. Design Validation

| Invariant | How it holds |
|---|---|
| C3 | The gate adds one resolved field to an event it already builds; fan-out stays in expansion. |
| C4 | Template map read only, on the main thread, before expansion runs. |
| C8 | `BuildHandle.Complete()` before `AsArray()`; no job scheduled, so no consumer handle to publish. |
| C9 | One `int2` cell range walk plus a best-distance scalar per request. No list, no allocation. |
| C10 | Walk timing unchanged; only the pose held between apply and the first link changes. |
| N1 | Both call sites go through `TargetedAcquisition`, so the faction and shape tests cannot drift. |

Failure modes checked: no hostile within `chainDistance` of the cursor → anchor unchanged,
`HasAcquiredTarget = 0`, chain spawns armed and dies on its first link-less update exactly as
today. Acquired target dies before the next update → resolve's link 0 re-ranks from the anchor and
takes the next nearest, which is the existing behaviour, so no stale-entity handling is needed —
this is the direct benefit of D3 stamping a position rather than an entity.

## 7. Minimal/Additive vs Refactor

**Additive** — gate does its own nearest-hostile scan inline.
Data flow: two independent selection implementations. New types: none. Copies: the faction/shape
rule duplicated. Long-term cost: the two drift, and the faction rule is exactly the thing this
session already had to audit once.

**Refactor (chosen)** — extract the selection rule into `TargetedAcquisition`, called by both the
gate and the resolve walk.
Data flow: one selection rule, two callers. Types changed: `TargetedResolveSystem` loses a private
method, gains a call. Copies removed: the would-be duplicate. Long-term benefit: the faction/shape
contract has one source of truth, and hop selection and acquisition can never disagree.

**Decision: refactor.** Default rule applies — two representations of the same domain concept
collapse to one unless there is a concrete migration reason, and there is none here.

## 8. Task List

| # | Task | Depends on |
|---|---|---|
| [001](./001-shared-acquisition-helper.md) | Extract `TargetedAcquisition` from the resolve job; no behaviour change | — |
| [002](./002-gate-acquires-first-target.md) | Gate acquires, snaps `AcquireAnchor`, stamps `HasAcquiredTarget` | 001 |
| [003](./003-apply-seeds-pose-from-anchor.md) | Apply seeds walk state and render mirror from the anchor; arming becomes conditional | 002 |
| [004](./004-docs-and-tests.md) | Doc updates + EditMode coverage | 001–003 |

## 9. Open Questions

- **Does an unacquired cast still cost mana?** Yes today — the gate spends mana before this
  acquisition runs, and this change deliberately does not move the spend. If a whiffed cast should
  be free, that is a separate gameplay decision, not part of this plan.
- **Interval-child and on-hit chains keep resolve-side acquisition (D2).** If they should also
  enter holding a target, the natural site is expansion, and that is a follow-up, not a change to
  these tasks.

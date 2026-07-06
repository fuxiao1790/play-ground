# Reduce AOE Activation Gates (Problem #2)

## Summary

An AOE carries four enableable gates that mostly move together, so every phase transition and every
materialization site has to keep several bits in sync. This plan **grounds** which gates are truly
independent and reduces the count where it is safe. Grounding shows only **one** gate is genuinely
redundant: `CombatRenderActiveTag`'s enabled-state always equals `Active` for AOEs (and, pending
confirmation, projectiles), so render/stats could gate on `Active` and the render tag could be
removed. The other three are independent and stay.

This is problem #2 of three (after `../aoe-spawn-state-unify/`). It **depends on #1** (`SpawnStateFor`
as the single derivation point), and is higher-risk/cross-cutting, so it leads with a
characterization/proof step and a flagged design decision before any deletion.

## The gates today (grounded)

| gate | meaning | queried by | scope |
|---|---|---|---|
| `Active` | occupancy; disabled == dead pool slot | dead-slot reuse `WithDisabled<Active>`, lifetime, collision, movement, tracking, pulse, pool cleanup, … | universal — **keep** |
| `AoeCollisionActiveTag` | AOE armed for collision | `ImpactAoeCollisionSystem`/`LingeringAoeCollisionSystem` `WithAll` | AOE-specific — **independent** (visual-only / future windup-suppressed) |
| `TimedSpawnComponent` (enabled) | armed for timed child spawn | `TimedSpawnSystem` `WithAll` | shared w/ projectiles — **independent** |
| `CombatRenderActiveTag` | should render / counts as active visual | `CombatBatchedRenderSystem`, `CombatRenderPrepareSystem`, `CombatStatsGatherSystem` `WithAll`; collision cores + lifetime toggle it | **common** (AOE + projectile) |

Evidence: readers enumerated at [CombatBatchedRenderSystem.cs:54](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L54),
[CombatRenderPrepareSystem.cs:28](../../Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs#L28),
[CombatStatsGatherSystem.cs:26](../../Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs#L26);
gate is a common component ([CombatRenderComponents.cs:100](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L100)).

## The redundancy (grounded)

For AOEs, `CombatRenderActiveTag` == `Active` at every write:
- impact materialization: both = `NeedsCollision` ([AoeSpawnApplySystem.cs:482-484](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L482))
- lingering materialization: both = `true` ([:510-512](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L510))
- despawn: `CombatLifetimeSystem`/`AoeCollisionCore.Deactivate` disable both together
  ([CombatLifetimeSystem.cs:109-111](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs#L109), [AoeCollisionCore.cs:234-241](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs#L234))

Projectiles likewise set render on at spawn and disable render+Active together on death — **to be
confirmed by the characterization step (001)** before relying on it.

## The design decision this plan turns on

Removing `CombatRenderActiveTag` forecloses any "**Active but not rendered**" state. There is exactly
one foreseeable use for that: a *telegraph-only* windup (sprite hidden during windup). Current windup
design keeps the sprite visible, so it does **not** need the split — but a future variant might.

- **Keep the render tag** ⇒ 4 gates stay; the anti-desync win comes entirely from #1's single-source
  derivation (materialization) + doing the same for transitions.
- **Merge render→Active** ⇒ 3 gates; render/stats query `Active`; delete `CombatRenderActiveTag`.
  Cross-cutting (render, stats, projectiles). Forecloses telegraph-only windup.

**This is a user decision** (see Open questions) because it trades a permanent gate reduction against
a specific future visual capability. The plan is structured so the decision is made *after* the proof
step, not guessed.

## Constraints & invariants the change must respect

- **`Active` semantics must not change.** `Active` disabled == dead pool slot (reuse query
  `WithDisabled<Active>`). If render gates on `Active`, dead slots must already be non-rendering — they
  are (despawn disables both) — but the characterization test must lock this. (code)
- **Cross-cutting: the render tag is common.** Any change touches projectiles + render + stats, not
  just AOE. Must not regress projectile rendering or the active-visual stat. (code)
- **Perf: chunk-skip.** Render/stats currently skip non-`CombatRenderActiveTag` chunks. Gating on
  `Active` instead must preserve the same skipping (both are enableable; equivalent cost). (code)
- **Depends on #1.** `SpawnStateFor` should own the derivation; with render==Active, `SpawnState.Render`
  becomes `SpawnState.Active` (or is dropped). (plan `../aoe-spawn-state-unify/`)

## Mechanisms reused vs. introduced

- Reused: `Active` as the occupancy+visibility gate; enableable chunk-skip.
- Removed (if merge chosen): `CombatRenderActiveTag` component + all its handles/queries/toggles.
- Introduced: nothing (this is a deletion, not an addition).

## Design validation

- *Occupancy:* unchanged (`Active` keeps its meaning). ✓
- *Redundancy proof:* 001 characterization test asserts render==Active across spawn/despawn for AOE +
  projectile before deletion. ✓ (gate for proceeding)
- *Independence:* `AoeCollisionActiveTag` and `TimedSpawnComponent` are NOT merged — they legitimately
  differ from `Active` (visual-only impact is `Active=false`? no — visual-only impact is inert; but a
  windup AOE is `Active=true`, collision off). Keep. ✓

## Minimal/additive vs. refactor comparison

- **Minimal (keep 4 gates):** data flow unchanged; no types removed; long-term cost: render bit stays a
  redundant copy of `Active` that every AOE/projectile spawn+despawn path must keep in sync (a standing
  desync opportunity, though currently consistent).
- **Refactor (merge render→Active):** render/stats read `Active`; one component + its handles/toggles
  deleted; removes a redundant copy; long-term benefit: one fewer bit to sync everywhere. Cost: cross-
  cutting change + forecloses telegraph-only.
- **Decision: ask user** (the telegraph-only tradeoff is a product/visual call), *then* refactor if merge
  is chosen. Do not merge silently.

## Default decision rule

`CombatRenderActiveTag` (for AOE/projectile) and `Active` are two representations of the same concept
("this pooled entity is live/visible"); default is to collapse to one source of truth — unless the
foreseeable telegraph-only need is a concrete reason to keep them separate. Hence the explicit decision.

## Task list

- [001](001-gate-coupling-characterization.md) — Characterization tests proving `CombatRenderActiveTag`
  == `Active` across spawn/despawn for AOE **and** projectile. Grounds (or refutes) the merge. No behavior
  change.
- [002](002-merge-render-into-active.md) — *(conditional on the user decision)* Delete
  `CombatRenderActiveTag`; render/stats gate on `Active`; drop `SpawnState.Render`. Cross-cutting,
  behavior-preserving.

Order: 001 first (proof). 002 only if 001 confirms the invariant **and** the user accepts the tradeoff.

## Verification

Harness cannot build/run Unity here. User runs the PlayMode suites (`AoeSimulationTests` **and**
projectile suites, since the render tag is shared) after each task; each is the green checkpoint.

## Open questions / considerations (decision required)

- **Telegraph-only windup:** do you ever want an AOE that is `Active` but **not** rendered (sprite
  hidden during windup, telegraph only)? If **yes**, keep `CombatRenderActiveTag` (do 001 only, skip
  002). If **no / don't care**, merge render→`Active` (001 then 002) for a permanent gate reduction.
- `AoeCollisionActiveTag` and `TimedSpawnComponent` are **not** reduced — grounding shows they are
  genuinely independent of `Active`. If you believed those were the redundant ones, say so and I'll
  re-ground.

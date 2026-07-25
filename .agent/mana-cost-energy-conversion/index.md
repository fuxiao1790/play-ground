# Mana Cost → Energy Cost Conversion + Unified Resource Model

## Status
- Tasks **001–003 (skill mana cost → energy)**: implemented on branch
  `energy-cost-based-spawn-trigger`. Kept for reference; see per-task files.
- Task **004** (first-pass player mana ECS resource): implemented, but the
  Player/Target split it produced is being **superseded** by the unified resource
  model below (user feedback: "mana and life should simply be a resource tied to a
  game obj, doesn't matter what the game obj is").
- New work: **004 (rewritten), 007, 008** — collapse health + mana into one
  agnostic resource with a clear GameObject/ECS ownership split and ECS regen.

## Summary

Three related changes:

1. **Mana cost → energy cost (done).** `spawnEnergyCost` became a folded
   `manaCost` (`SkillStat.ManaCost`); supports modify it; the interval-spawn
   **trigger link** converts the child's folded mana cost into the child
   `EnergyThreshold`. See 001–003.

2. **Unified resource model (this update).** Health ("life") and mana are the
   *same* concept — a bounded, regenerating pool bound to a unit — and must not be
   duplicated per GameObject type. Today the same idea is coded four ways:
   `PlayerHealth` (managed, player), `PlayerMana` (managed, player), `MobRoot`
   inline health (managed, mob), and the ECS `TargetHealth`/`TargetMana`. Collapse
   the managed side into **one reusable plain `Resource` class** used by any unit
   for any resource, and give the ECS side **neutral, typed resource components**
   with a defined ownership split. Health and mana behave identically — health is
   just mana with a different name; both default `RegenPerSecond` to `0`.

3. **Event-based spend via external spawn events (this update).** The GameObject
   emits an **external spawn event** (spawn intent) to ECS during Update. An ECS
   gate system reads the list, checks + deducts the caster's resource, and either
   emits the **internal ECS spawn events** (accept) or a **rejection event**
   (reject). Rejections return to the GameObject to handle; acceptance just spawns.
   Root casts go through the gate; interval-spawned children stay energy-funded
   (change 1) and never touch the pool — the parallel spawn job stays free of
   shared-pool writes. See 009–010.

## Resource Ownership Model (authoritative — from user)

| Concern | Owner |
|---|---|
| Initial value (seed) | **GameObject** (from `UnitStatSheet`) |
| `Max` | **GameObject** — pushes to ECS whenever Max changes (buff/level) |
| Regen rate | **GameObject** (authored on `UnitStatSheet`), pushed to ECS |
| `Current` at runtime | **ECS** (authority: damage, spend, regen all write here) |
| Regen tick | **ECS** (a resource-regen system) |
| Presentation (bars, death, flash) | **GameObject** — reads `Current` back from ECS |

This formalizes what health already half-does (ECS applies damage to
`TargetHealth.Current`, the root mirrors it back) and extends it to mana with
regen. The GameObject stops owning `Current`; it seeds it once, owns `Max`/regen,
and mirrors `Current` for presentation.

## ECS Representation Decision — typed components, not a keyed buffer

- **Chosen:** keep resources as **separate strongly-typed `IComponentData`**
  (`Health`, `Mana`), each `{ float Current; float Max; float RegenPerSecond; }`.
  Drop the `Target` prefix — a resource is not a "target" concept (the player
  spends mana to cast while also being a target).
- **Why:** health is read/written **per accrued target on the hot apply path** via
  `ComponentLookup<TargetHealth>` (`CombatApplyFinalizeSingleSystem.cs` L301-306:
  `health.Current -= acc.DamageTaken`). A generic `ResourceKind`-keyed
  `DynamicBuffer` would turn that direct component mutate into a per-hit buffer
  scan — a regression on the project's primary constraint (extreme hit counts).
  Regen touches only a handful of unit entities per frame, so iterating two typed
  components for regen is negligible; no buffer needed.
- **Agnostic without a buffer:** genericity comes from the *shared managed
  `Resource` type* and *uniform seeding*, not from a single ECS type. Adding a
  future resource (e.g. stamina) = one small typed component + one seed line + one
  regen query — cheap and explicit.

## Skill Use = External Spawn Event, Gated in ECS

Today `CombatRoot.SpawnRegisteredProjectile/Aoe` run on the managed thread and
**directly append** the internal `ProjectileSpawnEvent`/`AoeSpawnEvent` to the
scope buffer (`CombatRoot.cs` L207-291) — no resource check. The gate is inserted
exactly there: the GameObject emits an *external* spawn event; ECS turns it into
the internal event on accept, or a rejection on reject.

Flow:
1. **Emit (managed, during Update).** On a ready+fired slot, `SkillDriver` submits
   an `ExternalSpawnRequest { Kind, TemplateKey, Caster, ManaCost, Position,
   AimDirection, Count, Faction, CastToken }` via the updated `CombatRoot`
   overload. `Caster` = the caster's proxy entity; `ManaCost` = the compiled slot's
   `RuntimeSkillDefinition.ManaCost` (from 001). The slot fires optimistically
   (cooldown resets now, as today) — no pending state.
2. **Gate (ECS, serial).** `ExternalSpawnGateSystem` (SimulationSystemGroup, before
   the expansion systems, **single-threaded** so per-caster `Mana` writes never
   race): if `Mana.Current >= ManaCost`, subtract and emit the internal spawn event
   exactly as `SpawnRegistered*` does today; else append a `SpawnRejectedEvent`. No
   `Mana` component → accept (avoid soft-lock).
3. **Rejection back (managed).** `SpawnRejectionBridge` (PresentationSystemGroup,
   like `CombatApplyBridge`) resolves the caster via `TargetCompanion` and hands the
   rejection (with `CastToken`) to its `SkillDriver`, which reacts (default: refund
   the slot's cooldown so an out-of-mana press isn't wasted; optional feedback).
   **Acceptance is not signaled back — the spawn happening is the signal.**

Key consequences (see 009/010):
- **No added latency for accepted casts** — they spawn at the current root-cast
  latency (gate runs in the same ECS update that already drains the spawn buffer).
  Only rejections round-trip (~1 frame) to the GameObject.
- **Root-only.** Only GameObject-initiated (player/mob) casts pass through the gate.
  Interval/impact/child spawns already emit internal events and never spend.
- **Caster wiring.** `SkillDriver` needs its caster proxy `Entity`; `PlayerRoot`/
  `MobRoot` own both the driver and the proxy and pass it in.

## Constraints & Invariants (with sources)

- **Health apply is hot and direct.** `CombatApplyFinalizeSingleSystem` mutates
  `TargetHealth.Current` via `ComponentLookup` per target (L301-306). Renaming the
  component is fine; changing its access shape is not. → keep typed direct access.
- **ECS resource lifecycle.** `TargetHealth`/`TargetMana` are seeded once at
  `CombatTargetProxy.Create` and owned by ECS thereafter (`CombatTargetProxy.cs`
  L36, L105-110, archetype L307). The unified components keep this lifecycle;
  only naming + a `RegenPerSecond` field + a regen system are added.
- **Compile-time stat math only.** Mana cost still folds at compile time
  (`skill-system.md` L116); unaffected by the resource rework.
- **Shared target-proxy archetype.** All `ICombatTarget`s (player + mobs) get the
  resource components; mobs seed from their own sheet (mana/ regen may be 0).
- **Managed unit reactions stay per-root.** Death freeze / hurt flash are
  unit-specific presentation, not part of the pool. The `Resource` type raises
  events (`Depleted`, `Changed`); `PlayerRoot`/`MobRoot` subscribe.
- **Per-frame read-back cost.** With ECS owning `Current` (+regen), a resource can
  change without a hit, so presentation must pull `Current` when it can change
  outside the hit lane. Pull only for units that need it / resources that regen;
  don't blanket-read every mob's mana each frame. (Perf note, see 008.)

## Mechanisms Reused vs. Introduced

- **Reused:** the `TargetHealth` seed/own/mirror pattern (generalized); the combat
  result lane + `CombatApplyBridge`/`TargetCompanion` read-back (the spend result
  path mirrors it); the `CombatRoot` managed→ECS submission door (spend requests
  reuse it); `UnitStatSheet` as the authored source of Max/regen.
- **Introduced:** one managed `Resource` class; a small ECS `ResourceRegenSystem`;
  a `RegenPerSecond` field on the resource components; a `PushResourceMax`-style
  helper on `CombatTargetProxy`; an external spawn gate (`ExternalSpawnRequest` +
  serial `ExternalSpawnGateSystem` + `SpawnRejectedEvent` lane + `SpawnRejectionBridge`).
- **Removed:** `PlayerHealth`, `PlayerMana`, and `MobRoot`'s inline health
  fields/methods — folded into the shared `Resource` class.

## Additive vs. Refactor Comparison (resource model)

**Additive (current state):** one bespoke holder per (unit × resource) plus a
per-resource ECS component with a `Target` name.
- Data flow: 4 managed representations of one concept, each mirroring ECS
  differently; mob has no mana path at all.
- Long-term cost: every resource feature (regen, spend, UI) is implemented N times;
  the exact duplication the user flagged.

**Refactor (chosen):** one managed `Resource` type + neutral typed ECS components +
one regen system + defined ownership.
- Data flow: `UnitStatSheet` → GO seeds `Current/Max/Regen` → ECS owns `Current`
  (+regen/damage/spend) → GO mirrors `Current` for presentation. One path per
  resource, identical for health and mana, player and mob.
- Long-term benefit: new resources and new units are near-free; single source of
  truth for pool logic; matches the "resource tied to a game obj" intent.

**Decision:** refactor. Collapses four representations of one domain concept to one
managed type + uniform ECS components.

## Task List

- [001](001-skill-mana-cost-stat-fold.md) — Skill mana-cost fold. **(done)**
- [002](002-supports-contribute-mana-cost.md) — Supports contribute mana cost. **(done)**
- [003](003-trigger-link-mana-to-energy-conversion.md) — Trigger conversion function. **(done)**
- [004](004-unified-ecs-resource-components.md) — Neutral typed ECS resource
  components (`Health`/`Mana`) + GO/ECS ownership split + `RegenPerSecond`.
  *(rewritten; supersedes the first-pass `TargetMana`)*
- [007](007-ecs-resource-regen-system.md) — ECS resource regen system.
- [008](008-unified-managed-resource-and-roots.md) — Shared managed `Resource`
  class; rewire `PlayerRoot` + `MobRoot`; stat-sheet regen fields; remove
  `PlayerHealth`/`PlayerMana`/mob inline health.
- [009](009-ecs-resource-spend-pipeline.md) — External spawn event + serial
  `ExternalSpawnGateSystem` (check/deduct → internal spawn events or rejection) +
  `SpawnRejectionBridge`; `CombatRoot` submission API change.
- [010](010-skilldriver-spend-gated-cast.md) — `SkillDriver`/`SkillSpawnTranslator`
  emit external spawn events (caster + cost); handle rejections; player + mob.
- [005](005-test-data-seeding-and-verification.md) — Test values + verification. *(updated)*
- [006](006-docs-update.md) — Docs. *(updated)*

## Dependencies
- 004 → 007 (regen needs the `RegenPerSecond` field) → 008 (managed side consumes
  the neutral components + ownership helpers).
- 009 depends on 004 (writes `Mana.Current`) and the managed→ECS submission surface.
- 010 depends on 009 (consumes accept/reject) and 001 (`RuntimeSkillDefinition.ManaCost`).
- 005, 006 depend on 004/007/008/009/010.

## Resolved Decisions
1. **Managed shape:** plain reusable `Resource` **class** held by each root (no
   `Vitals` MonoBehaviour, no prefab churn). *(user)*
2. **Health = mana, different name:** identical behavior; `RegenPerSecond` defaults
   to `0` for both (mana test value non-zero). *(user)*
3. **Spending in scope:** event-based, ECS-authoritative, root-only, serial. *(user)*

## Open Questions
1. **Rejection reaction:** a rejected (out-of-mana) cast — refund the cooldown so it
   retries when mana returns (default), or burn the cooldown, or add SFX/UI cue?
   The GameObject owns this; default is refund + no cue.
2. **Zero-cost casts:** submit as a cost-0 external request (always accepted, uniform
   path) or short-circuit to a direct internal spawn? Default: uniform path.
3. **Deliverable:** plan is now complete across all three changes. Say the word to
   move from plan to implementation (004/007/008/009/010).

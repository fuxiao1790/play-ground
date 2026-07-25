# Mana Cost → Energy Cost Conversion

## Summary

Skills gain a **mana cost** that replaces today's directly-authored
`spawnEnergyCost`. Mana cost is routed through the existing skill stat-fold
(`SkillStat` + `StatModifierAccumulator`) so **supports modify it** (additional
projectiles, piercing, homing, etc.). When a skill is the *child* (effect) of an
interval-spawn trigger, the **trigger link** owns a **mana → energy conversion
function** that turns the child's folded mana cost into the per-child
`EnergyThreshold`. This makes the interval-spawn cadence auto-scale with the
child set's supports instead of requiring hand-retuned `spawnEnergyCost` values —
the "easier to balance" goal from `Docs/todo.md`.

Separately, per the user's direction, the player character gets a **mana
resource** authored on `UnitStatSheet` and **owned by ECS like health**
(`TargetMana` mirroring `TargetHealth`), seeded once at proxy creation and
mirrored MB-side by a `PlayerMana` twin of `PlayerHealth`. This is
infrastructure the user asked for ("mana owned by ECS similar to health"); its
*consumption* by casting/spawning is an explicit open decision (see below), not
part of this pass.

## Scope

In scope:
1. Rename `spawnEnergyCost` → `manaCost`; fold through `SkillStat.ManaCost`.
2. Supports contribute to mana cost.
3. Mana → energy conversion function on the interval-spawn trigger link.
4. Player mana resource: stat-sheet field + ECS `TargetMana` seeded like health +
   MB-side `PlayerMana` mirror.
5. Test-data seeding (editor steps for the user) + verification.
6. Doc updates.

Out of scope (open decision E): a live link where per-source energy accrual
**consumes** the owner unit's mana pool and gates spawning when depleted.

## Constraints & Invariants (with sources)

- **Compile-time stat math only, no per-frame folding.** Scaling is rebaked on
  equipment/buff/level change, not per frame
  (`Docs/reference/game-logic/skill-system.md` L116). → Mana cost must fold at
  compile time inside `SkillSetCompiler.BuildRuntime`, not in ECS.
- **Energy threshold is a baked plain float consumed by a Burst job.**
  `TimedSpawnComponent.EnergyThreshold` is set at registration and read by the
  parallel `TimedSpawnJob` (`Assets/Scripts/System/Spawning/TimedSpawnSystem.cs`
  L143-152). → The conversion output is just a float; no ECS component or job
  change is required for the core feature.
- **Threshold source today bypasses the fold.** `SkillSetCompiler.ApplyChildSpawn`
  / `ApplyAoeIntervalSpawn` read the *raw* SO field
  (`childSkillDefinition.spawnEnergyCost`, `SkillSetCompiler.cs` L332, L371),
  which is exactly why supports cannot affect it today. → Move the read to the
  *compiled* child's folded `ManaCost`.
- **ECS resource lifecycle "like health".** `TargetHealth` is seeded once at
  `CombatTargetProxy.Create` from `ICombatTarget.CombatMaxHealth`/`CombatCurrentHealth`
  and then owned by ECS until proxy destroy (`CombatTargetProxy.cs` L36, L97-99,
  archetype L263-280). MB side holds runtime health in `PlayerHealth` and mirrors
  ECS via `ReceiveCombatTick`. → `TargetMana` must follow the identical pattern.
- **Target-proxy archetype is shared by all targets** (player + mobs; both
  implement `ICombatTarget`). Adding `TargetMana` seeds every proxy. Non-casters
  default to `0` max mana via the interface default; harmless.
- **Serialized asset values must survive the rename.** Existing skill assets carry
  authored `spawnEnergyCost` values in YAML (never hand-edit YAML — see memory
  `editor-steps-are-user-steps`). → Use `[FormerlySerializedAs("spawnEnergyCost")]`
  so Unity migrates values automatically.

## Mechanisms Reused vs. Introduced

- **Reused:** the `SkillStat` enum + `StatModifierAccumulator` per-stat fold and
  the `IBaseValueModifier`/`IIncreasedModifier`/`IMultiplierModifier` support
  sinks — mana cost becomes just another folded stat. Supports contribute exactly
  as `AddedDamageSupport` already does for `SkillStat.Damage`.
- **Reused:** the `TargetHealth` seed/own/mirror pattern for the new
  `TargetMana`; the `PlayerHealth` runtime holder pattern for `PlayerMana`.
- **Introduced (small):** an abstract `IntervalSpawnTrigger : TriggerLink` base
  that hoists the duplicated `energyPerSecond` / `energyJitterPercent` and owns
  the `ManaToEnergyCost(...)` conversion. Justified: removes existing duplication
  between the two interval triggers and gives the conversion function a single
  home, as directed ("conversion function on the trigger link").

## Minimal/Additive vs. Refactor Comparison

**Additive:** keep `spawnEnergyCost` and add a separate `manaCost` field plus a
side conversion.
- Data flow: two fields describing the same thing (energy-threshold source), one
  folded and one not; compiler must decide which wins.
- New concepts/types: a duplicate cost field.
- Copies/translations: a shim to reconcile the two.
- Long-term cost: two sources of truth for the child spawn threshold; every future
  balance change must touch both; classic structural warning.

**Refactor (chosen):** `spawnEnergyCost` *becomes* `manaCost`, folds through
`SkillStat.ManaCost`, and the trigger converts the folded value.
- Data flow: single field → fold (with support terms) → trigger conversion →
  `EnergyThreshold`. One path.
- Existing types changed: `ProjectileDefinition`/`AoeDefinitionBase` field renamed;
  `SkillStat` gains `ManaCost`; runtime defs gain `ManaCost`.
- Copies/translations removed: the raw-SO threshold read is deleted; the value now
  flows through the same fold as every other stat.
- Long-term benefit: supports auto-scale the threshold; one source of truth; the
  "easier to balance" outcome the request asks for.

**Decision:** choose refactor. Two representations of one domain concept
(per-child spawn cost) collapse to one folded source of truth — matches the
default decision rule.

## Player-Mana ↔ Energy Interaction (Open Decision E)

The three user directives (mana on the sheet; mana ECS-owned like health;
conversion on the trigger link) fully specify the *skill-mana-cost → energy-cost*
path but leave one thing open: **should the per-source energy accrual draw down
the owner unit's mana pool** (making mana a live consumable that gates spawning),
or is the pool scaffolded-but-inert for now?

- **Recommendation: defer consumption (scaffold only) in this pass.** The request
  says "doesn't have to be balanced, just to check if things are working." The
  checkable behavior is the mana-cost → energy-threshold conversion. A live drain
  is a *cross-entity* change: source entities carry only `SourceId` + `Faction`
  (`TimedSpawnComponent`), not an owner `Entity`, and `TimedSpawnJob` is a Burst
  `ScheduleParallel` job — many sources draining one shared owner-mana component
  would need owner resolution plus parallel-safe accumulation (a real concurrency
  hazard). That is a separate, larger design and is not required to validate this
  feature.
- If the user wants consumption *now*, the safe first cut is main-thread
  consumption at **root cast time** in `SkillDriver.Tick` (pool write happens on
  the managed side, no job race), gating the cast when mana is insufficient —
  captured as a follow-up task stub, not built here.

This is recorded for the user to confirm on plan review; the plan proceeds with
scaffold-only.

## Task List

- [001](001-skill-mana-cost-stat-fold.md) — Skill mana-cost field + `SkillStat.ManaCost` fold + runtime storage.
- [002](002-supports-contribute-mana-cost.md) — Supports contribute mana cost.
- [003](003-trigger-link-mana-to-energy-conversion.md) — Interval-spawn trigger conversion function; compiler uses folded child mana cost.
- [004](004-player-mana-ecs-resource.md) — Player mana on stat sheet + `TargetMana` ECS resource (seeded/owned like health) + `PlayerMana` MB mirror.
- [005](005-test-data-seeding-and-verification.md) — Non-zero test values (editor steps for user) + verification.
- [006](006-docs-update.md) — Update skill-system doc and related references.

## Dependencies

- 002 depends on 001 (needs `SkillStat.ManaCost` + folded `ManaCost`).
- 003 depends on 001 (reads compiled child `ManaCost`).
- 004 is independent of 001–003 (parallel resource scaffolding).
- 005 depends on 001–004.
- 006 depends on 001–004.

## Open Questions

1. **Decision E** above — confirm scaffold-only vs. include consumption/gating now.
2. Should supports scale mana cost as a **flat add** (`SkillStat.ManaCost` via
   `IBaseValueModifier`, mirrors `AddedDamageSupport`) or an **increased %**? Plan
   assumes flat add for simplicity; easy to switch per support.
3. Should a player-level snapshot term (e.g. a "mana efficiency" on
   `UnitStatSheet`) also scale mana cost via `SkillStatSnapshot`? Not requested;
   left out. The conversion staying on the trigger keeps this cleanly addable
   later.

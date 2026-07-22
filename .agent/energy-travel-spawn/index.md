# Energy-Based Travel Spawn

Replace the interval-timer model that governs **travel spawn** (children emitted by a
projectile or lingering AOE *while it travels*) with an **energy-accrual** model. The
spawner accumulates energy over time at an authored rate and fires a child the moment its
accumulated energy crosses a threshold; the threshold is the child skill's authored cost.

"Travel spawn" is exactly the two interval triggers driven by `TimedSpawnSystem`:
`ProjectileIntervalSpawnTrigger` and `AoeIntervalSpawnTrigger`. Event-driven triggers
(`OnImpact*`, `OnAoeHitSpawn`, `StackTrigger`) are out of scope.

## Model (decided)

- **Gain rate** lives on the **trigger**: rename `intervalSeconds` -> `energyPerSecond`
  (the traveling source's charge speed). This is the direct replacement for the per-link
  cadence knob that exists today.
- **Cost / threshold** lives on the **child skill**: new `spawnEnergyCost` field on the
  skill definition. "Different skills need different amounts of energy to spawn."
- Emergent cadence `= spawnEnergyCost / energyPerSecond`. One spawner + different child
  skills => different cadence, with no shared code path change.
- **Jitter** survives as a per-tick threshold jitter: rename `intervalJitterPercent` ->
  `energyJitterPercent`, applied as a percent of the threshold each tick. Preserves the
  existing cross-source desync capability at no added complexity.

### Runtime tick (energy accrual)

```
energy += energyPerSecond * dt                       // accrue
threshold = max(MinThreshold, cost + jitter(tick+1)) // per-tick, deterministic
while energy >= threshold and ticks < MaxTicksPerUpdate:
    tickIndex++; ticks++
    spawn child (deterministic id from tickIndex)
    energy -= threshold
    threshold = max(MinThreshold, cost + jitter(tick+1))
```

Starts at `energy = 0` (empty), so the first child fires after `cost / rate` seconds,
matching the current "wait one interval before first spawn" behavior. Cross-source desync
is preserved because the per-tick jitter hashes the source id (see invariants).

## Rationale

This is a **refactor of the existing timed-spawn path**, not a parallel system. The
interval fields (`IntervalSeconds`, `IntervalJitterSeconds`) and the cooldown counter
(`CooldownRemaining`) are the *only* things whose meaning changes. They are replaced
in place by energy fields (`EnergyPerSecond`, `EnergyThreshold`, `EnergyThresholdJitter`)
and an energy counter (`EnergyAccumulated`). No new component, no second data path, no
translation shim. The child **template registry, spawn expansion, spawn command, and
apply pipeline are untouched** because timing has always been slim per-source timer
config, separate from the content-hashed child template (see invariants below).

The only genuinely new authoring surface is `spawnEnergyCost` on the skill definition —
and it removes complexity rather than adding it, because it relocates the cadence cost
from the wiring onto the skill where "how expensive is this skill to spawn" naturally
belongs, and it sets the model up for future per-skill cost scaling / shared energy pools
without another rewrite.

## Constraints & invariants the change must respect

1. **Timing is slim per-source timer config, NOT part of the child template hash.**
   Source: `skill-system.md` Spawn-Template Registry section ("Jitter seed is not part of
   the template hash because it belongs to the individual timer config"); `TimedSpawnComponent`
   carries `TemplateKey` *and* timing side by side in
   [TimedSpawnComponents.cs](../../Assets/Scripts/System/Spawning/TimedSpawnComponents.cs).
   => Energy fields replace interval fields inside `TimedSpawnComponent` only. The
   `TemplateKey`, stored `ProjectileSpawnEvent`/`AoeSpawnCommand`, and every downstream
   expansion/apply system stay byte-for-byte unchanged.

2. **The tick job is Burst, parallel, per-entity, deterministic.** Source:
   [TimedSpawnSystem.cs](../../Assets/Scripts/System/Spawning/TimedSpawnSystem.cs)
   `TimedSpawnJob : IJobEntity` with `DeterministicJitter(sourceId, jitterSeed, tickIndex)`.
   => `EnergyAccumulated` stays per-entity local state (replacing `CooldownRemaining`);
   no cross-entity reads. Deterministic child ids keep flowing from `tickIndex`. Threshold
   jitter reuses the same `DeterministicJitter` hash so identical spawners with distinct
   source ids stay desynced.

3. **A bad authored value must not freeze the editor.** Source:
   `TimedSpawnSystem` `MinIntervalSeconds` + `MaxTicksPerUpdate` guards; `skill-system.md`
   "clamps every tick advance to a positive minimum and caps catch-up iterations".
   => Keep `MaxTicksPerUpdate`. Replace `MinIntervalSeconds` with a positive
   `MinEnergyThreshold` clamp on the per-tick threshold so a zero/negative cost cannot spin
   the while-loop. Guard `energyPerSecond <= 0` as "disabled" (no accrual).

4. **`IsTimedSpawnEnabled` is the compile/spawn gate and doubles as the "has a real setup"
   sentinel.** Source: `SkillDriver.IsTimedSpawnEnabled` and `CombatRoot.IsTimedSpawnEnabled`
   both test `IntervalSeconds > 0 && JitterSeed > 0 && TemplateKey != default`.
   => Retest on `EnergyPerSecond > 0 && EnergyThreshold > 0 && JitterSeed > 0 &&
   TemplateKey != default`. `JitterSeed` stays the compile-assigned (`> 0`) sentinel.

5. **Set isolation / ownership boundary.** Source: `skill-system.md` Set Isolation Rules —
   a trigger carries no stats into the target set; the child set fully determines its own
   behavior. => `spawnEnergyCost` is the child skill's own property, read from the compiled
   *child* definition, not injected by the parent or the trigger. `energyPerSecond` is the
   trigger's (wiring) property. Clean split, no cross-set leakage.

6. **Authored Unity assets are edited by the user, never by hand-written YAML.** Source:
   memory `editor-steps-are-user-steps`. => Re-authoring the three trigger `.asset` files
   and setting `spawnEnergyCost` on child skills are user editor steps (task 005), not code
   edits. The rename deliberately does **not** auto-migrate the rate value, because the old
   number (interval seconds) would be wrong under the new meaning (energy/second).

## Mechanisms reused vs introduced

- **Reused:** the `TimedSpawnComponent` (slim timer config) / `TimedSpawnStateComponent`
  (hot state) / template-registry split; the `DeterministicJitter` hash; the
  `MaxTicksPerUpdate` catch-up cap; `JitterSeed > 0` enabled-sentinel; the compile ->
  `Runtime*Setup` -> `TimedSpawnComponent` build chain; the enable/disable of
  `TimedSpawnComponent` on cold-create/pool-reuse.
- **Introduced:** one authoring field `spawnEnergyCost` on the skill definition, and the
  field renames within the already-existing timer config and runtime setup types. No new
  type, system, event, or data path.

## Design validation (against each invariant)

- **(1)** Change is confined to `TimedSpawnComponent`/`State` fields, the two `Runtime*Setup`
  timing fields, the compile mapping, the tick loop, and the initial-state seed. Template
  hashing inputs (child behavior/render/damage) are not touched -> registry keys unchanged.
- **(2)** `EnergyAccumulated` is a drop-in replacement for `CooldownRemaining` (same slot,
  same per-entity locality). Jitter still hashes `sourceId` -> desync preserved. Child ids
  still derive from `tickIndex`.
- **(3)** `MinEnergyThreshold` + `MaxTicksPerUpdate` bound the inner loop; `energyPerSecond<=0`
  short-circuits accrual. No unbounded emission in one update.
- **(4)** Both `IsTimedSpawnEnabled` implementations updated identically; `JitterSeed`
  sentinel retained.
- **(5)** Cost read from compiled child def; rate read from trigger. No parent/trigger stat
  bleed into the child.
- **(6)** Asset re-authoring isolated to task 005 as user steps.

## Minimal/additive vs. refactor comparison

- **Minimal/additive** (keep interval fields, add parallel energy fields, translate):
  - resulting data flow: two timing representations in `TimedSpawnComponent`, a branch in the
    tick loop choosing interval-vs-energy, interval retained on the trigger alongside a new
    rate/cost.
  - new concepts/types: energy fields *plus* surviving interval fields; a mode flag.
  - copies/translations added: interval<->energy reconciliation at compile and tick time.
  - long-term cost: two ways to author the same cadence, ambiguous source of truth, dead
    interval path to keep in sync.
- **Refactor** (replace interval with energy in place — chosen):
  - resulting data flow: single timing representation (energy) end to end; one tick loop.
  - existing concepts/types changed: `TimedSpawnComponent`/`State`, `RuntimeChildSpawnSetup`,
    `RuntimeAoeIntervalSpawnSetup`, `ProjectileChildSpawnConfig`, the two trigger fields, the
    compile mapping, the tick loop. Interval removed.
  - copies/translations removed: no interval<->energy reconciliation ever exists.
  - long-term benefit: one cadence model, cost owned by the skill, ready for cost scaling /
    shared energy pools.
- **Decision:** **choose refactor.** The domain concept ("how often does a traveling source
  emit a child") gets exactly one representation. Additive would create a second timing
  representation for the same concept — the precise structural warning to avoid.

## Default decision rule applied

Interval-seconds and energy describe the same domain concept (emission cadence). Per the
"one source of truth" rule, collapse to a single representation (energy) rather than keeping
both. No compatibility/migration reason to retain interval — the only consumers are internal
(compiler, driver, tick system, tests) plus three authored assets re-authored in task 005.

## Task list

- [001-runtime-energy-fields.md](001-runtime-energy-fields.md) — Replace interval fields with
  energy fields across the ECS timer config/state and the runtime setup types (the vocabulary).
- [002-authoring-and-compile.md](002-authoring-and-compile.md) — Trigger fields
  (`energyPerSecond`, `energyJitterPercent`), new `spawnEnergyCost` on skill definitions,
  `SkillSetCompiler` mapping, and `SkillDriver` build helpers + `IsTimedSpawnEnabled`.
- [003-tick-system.md](003-tick-system.md) — `TimedSpawnSystem` energy-accrual loop,
  `ProjectileSpawnApplySystem` initial state, `CombatRoot` gate + legacy `ChildSpawnConfig`
  fallback.
- [004-tests-and-docs.md](004-tests-and-docs.md) — Update edit/play-mode tests, add an
  energy-cadence test, update `skill-system.md` and simulation docs.
- [005-editor-reauthoring.md](005-editor-reauthoring.md) — User editor steps: re-author the
  three trigger assets and set `spawnEnergyCost` on child skills.

Dependencies: 002 and 003 both depend on 001 (shared field vocabulary). 004 depends on
002 + 003. 005 depends on 002 (new fields must exist to author against).

## Open questions / considerations

- **Legacy `ProjectileChildSpawnConfig` fallback** in `CombatRoot.TimedSpawnFor` may be dead
  (no producer found feeding `ChildSpawn` with a real interval in the compiled flow). Task 003
  verifies; if live, it is converted to energy fields; if dead, it is removed. Either way it
  must not keep interval semantics.
- **`spawnEnergyCost` default.** Recommend a sensible non-zero default (e.g. `1`) so an
  un-migrated child skill still spawns rather than clamping to `MinEnergyThreshold`. Confirm
  during task 002 against existing child-skill assets.
- **Shared energy pool (future).** A source currently carries one `TimedSpawnComponent`
  (single child stream). The energy model leaves room for a future shared pool across
  multiple child triggers, but that is explicitly out of scope here.

# Hit Energy Trigger Plan

## Summary

Replace hit-count stacking system with target-local hit energy, using fresh
domain names throughout authoring, compiled graph, snapshots, ECS state, and
presentation.

For adjacent edge:

```text
S1 -> T1 -> S2
```

effective values are:

```text
energyPerHit   = S1.TriggerEnergy * T1.EnergyContributionMultiplier
energyRequired = S2.TriggerEnergy * T1.EnergyRequirementMultiplier
```

Every accepted S1 hit deposits `energyPerHit` into T1's target-local
accumulator. Each complete `energyRequired` activates registered S2 spawn and
leaves fractional remainder banked.

For longer chain:

```text
S1 -> T1 -> S2 -> T2 -> S3
```

S1 carries only T1 payload. Spawned S2 carries only T2 payload. Each compiled
edge owns distinct `AccumulatorId`, so S1 cannot activate S3 directly. Rule
holds when S1, S2, and S3 reference same Skill or SkillSet asset.

## Target Vocabulary

| Concept | Target name |
|---|---|
| Skill-authored base value | `TriggerEnergy` |
| Trigger ScriptableObject | `HitEnergyTrigger` |
| Source-side scale | `EnergyContributionMultiplier` |
| Target-side scale | `EnergyRequirementMultiplier` |
| Accumulator lifetime | `RetentionSeconds` |
| Compiled edge data | `RuntimeHitEnergyTrigger` |
| Triggered runtime skill | `TriggeredSkill` |
| Per-compiled-edge identity | `AccumulatorId` |
| Fire-time hit payload | `HitEnergyPayload` |
| Energy deposited by one accepted hit | `EnergyPerHit` |
| Energy consumed by one activation | `EnergyRequired` |
| Target buffer element | `TargetHitEnergy` |
| Banked target value | `StoredEnergy` |
| Registered activation output | `HitEnergySpawn` |
| ECS threshold processor | `HitEnergyActivationSystem` |
| Managed presentation value | `HitEnergyProgress` |

Legacy stack/debuff/detonation names are migration inputs only. Do not keep
aliases, compatibility properties, adapter types, or mixed vocabulary in final
runtime code.

## Architectural Decisions

1. **Skill owns base energy.** Every Skill ScriptableObject authors positive
   `TriggerEnergy`. Compiler copies it to each independent
   `RuntimeSkillDefinition`.
2. **Trigger owns two independent scales.** `HitEnergyTrigger` authors positive
   `EnergyContributionMultiplier` and `EnergyRequirementMultiplier`, both
   default `1f`, plus `RetentionSeconds`.
3. **Compiled trigger is edge data, not skill data.** Introduce
   `RuntimeHitEnergyTrigger` as composition attached to source runtime skill. It
   does not inherit `RuntimeSkillDefinition`. It contains TriggeredSkill,
   multipliers, retention, and AccumulatorId.
4. **One-way snapshot boundary.** Authoring values compile into runtime graph;
   registration bakes effective EnergyPerHit/EnergyRequired into
   `HitEnergyPayload`; ECS never reads authoring or managed runtime objects.
5. **Edge identity stays separate from skill identity.** Registration assigns
   unique AccumulatorId to every compiled edge. Never derive identity from asset
   GUID, runtime type id, template key, energy values, or skill equality.
6. **Registered spawn is output source of truth.** `HitEnergySpawn` holds only
   kind, faction, and template key. Damage, area, count, crit, launch behavior,
   and descendants come from registered template. Remove unused accumulated
   output-stat totals.
7. **Interval energy remains separate.** `IntervalSpawnTrigger` and
   `TimedSpawnComponent` keep time-based energy and mana-derived cost. Hit energy
   uses TriggerEnergy vocabulary to avoid conflating two systems.
8. **No compatibility runtime.** Migrate current assets once, then remove old
   serialized/runtime fields and old type names. Preserve asset GUIDs rather
   than preserving obsolete APIs.

## Migration Baseline

Set every existing Skill asset `triggerEnergy: 1`.

For each current trigger asset:

```text
energyContributionMultiplier = old stacksPerHit
energyRequirementMultiplier  = old stackThreshold
retentionSeconds              = old debuffLifetimeSeconds
```

This preserves current hit timing while changing runtime model. Migration is
asset-data transformation, not permanent `FormerlySerializedAs`/compatibility
code. Later balance pass can distribute values differently between Skills and
edge multipliers.

## Constraints And Invariants

### Adjacency and independent compilation

- `SkillSetCompiler.CompileInternal` follows only `nodeIndex -> nodeIndex + 1`
  and creates runtime definition per compiled path
  (`Assets/Scripts/Skills/SkillSetCompiler.cs`).
- Incoming-trigger nodes are excluded from direct-cast roots
  (`Assets/Scripts/Skills/SkillLoadoutCompiler.cs`).
- Repeated asset use still means independent slot instances
  (`Docs/contracts/skill-loadout-editing.md`,
  `Docs/reference/game-logic/skill-gameplay-system.md`).
- Validation: source runtime owns only its outgoing RuntimeHitEnergyTrigger;
  TriggeredSkill owns only its own outgoing trigger.

### Edge-local identity

- Current implementation already assigns separate id per compiled edge and
  groups target-local state by it (`SkillDriver.cs`,
  `CombatApplyFinalizeSingleSystem.cs`).
- Refactor retains behavior under AccumulatorId name.
- Validation: same asset/value/type used in several nodes produces distinct
  AccumulatorId values and independent TargetHitEnergy entries.

### Snapshot and ownership boundary

- Game logic owns authoring/compiler semantics; ECS owns high-count state
  (`Docs/layers/game-logic.md`, `Docs/layers/ecs-simulation.md`).
- Snapshots must remain unmanaged/Burst-safe and contain no ScriptableObject,
  GameObject, or managed graph references
  (`Docs/contracts/skill-runtime-snapshots.md`, ADR-002).
- In-flight entities retain copied values across loadout recompilation.
- Validation: `Skill.TriggerEnergy -> RuntimeSkillDefinition.TriggerEnergy ->
  HitEnergyPayload` is one-way copy.

### Frame ordering and lifetime

- Current status processor runs before current-update hit finalization, so new
  deposits become eligible next simulation update
  (`StatusProcessSystem.cs`, `Docs/flows/collision-to-combat-result.md`).
- Finalization owns deposit and expiry refresh. HitEnergyActivationSystem owns
  expiry, threshold consumption, and HitEnergySpawn emission.
- RetentionSeconds `<= 0` disables payload, matching current zero-lifetime
  behavior.
- No same-frame shortcut or system reordering.

### Burst and hot-path bounds

- No managed allocation or authoring access per hit
  (`Docs/performance.md`, `Docs/reference/simulation/ecs-notes.md`).
- Keep maximum 32 accumulator entries per target.
- Rename 256-per-target cap around activations, preserve overflow in StoredEnergy
  for later update.
- Reuse native spawn event lanes and registered templates; add no event lane or
  structural change.
- Removing old output totals shrinks target buffer element.

### Activation output

- Register TriggeredSkill template before source HitEnergyPayload is built.
- HitEnergySpawn references registered template by kind/faction/key.
- StoredEnergy controls only activation count/timing; never scales output stats.

## Mechanisms Reused Vs Introduced

### Reused

- Skill ScriptableObject authoring and compiler copy step.
- Adjacent recursive runtime graph and direct-root filtering.
- Per-compiled-edge id assignment behavior.
- Fire-time payload copied through projectile, AOE, and targeted entities.
- Target-local bounded dynamic buffer and expiry refresh.
- Next-update processing, per-target activation cap, registered templates, and
  existing projectile/AOE event queues.
- Compact managed progress presentation path.

### Introduced

- Fresh HitEnergy domain vocabulary and types listed above.
- Skill TriggerEnergy scalar.
- Two HitEnergyTrigger multipliers and RetentionSeconds.
- RuntimeHitEnergyTrigger composition object.
- Float payload/buffer/progress values.
- No second registry, event lane, authoring lookup, or compatibility mode.

## Design Validation

| Invariant | Result |
|---|---|
| Only adjacent source funds target | Source payload contains only outgoing edge AccumulatorId and scaled values. |
| Same Skill reused in S1/S2/S3 stays isolated | Compiler creates separate runtime nodes; T1/T2 receive distinct AccumulatorId values. |
| Triggered nodes never direct-cast | Root selection remains unchanged. |
| In-flight data survives recompilation | Entities carry copied HitEnergyPayload values and old accumulator id. |
| ECS remains Burst-safe | Payload/buffer fields are primitives, enum, faction, and Hash128 only. |
| Current-update hits do not activate early | Existing process-before-finalize order remains. |
| Dense hits preserve overflow | Processor floors StoredEnergy/EnergyRequired, caps emitted activations, subtracts emitted cost only. |
| Output stats have one source | Registered TriggeredSkill template remains authoritative. |

## Minimal/Additive Vs Refactor Comparison

### Minimal/additive approach

- Resulting data flow: energy added beside legacy stack fields/types.
- New concepts/types: mode flag or implicit count/energy compatibility rule.
- Copies/translations: integer-to-float conversions and two progress models.
- Long-term cost: mixed stack/debuff/energy vocabulary, fake skill wrapper,
  larger buffer, branching hot path, unclear ownership.

### Refactor approach

- Resulting data flow: TriggerEnergy and two edge multipliers compile into one
  HitEnergyPayload; one TargetHitEnergy entry owns progress.
- Existing concepts changed/removed: remove legacy trigger type, fake runtime
  skill wrapper, count payload, stack buffer names, debuff identity names,
  detonation snapshot names, and unused output totals.
- Copies/translations avoided: no compatibility mode or duplicate progress
  representation.
- Long-term benefit: names reflect domain roles, runtime graph uses composition,
  one source of truth, smaller buffer, clearer edge ownership.

### Decision

- **Choose full refactor.** User requires old hit-count behavior removed and
  fresh names. Final code has one HitEnergy model; legacy names exist only in
  migration history/commit diff.

## Task Index

1. [001-author-skill-energy.md](001-author-skill-energy.md) - author and compile
   TriggerEnergy.
2. [002-build-hit-energy-runtime.md](002-build-hit-energy-runtime.md) - replace
   legacy trigger/runtime wrapper with HitEnergy authoring, runtime edge, and
   payload.
3. [003-convert-ecs-accumulator.md](003-convert-ecs-accumulator.md) - rename and
   convert ECS accumulator, activation processor, and progress presentation.
4. [004-regression-tests.md](004-regression-tests.md) - replace count tests and
   prove multiplier semantics plus same-asset adjacency isolation.
5. [005-assets-and-docs.md](005-assets-and-docs.md) - migrate assets/GUID-safe
   type names and update contracts/docs.

## Open Considerations

- Future balance values unspecified. Compatibility baseline derives new asset
  values from current count settings, preserving hit timing.
- TriggerEnergy is unaffected by supports, global interval-energy stats, damage
  scale, crit, projectile count, area, chain falloff, or delta time.
- Use one named positive floor (recommended `1e-3f`) for TriggerEnergy,
  multipliers, EnergyPerHit, and EnergyRequired.
- Empty `.agent/hit-energy-trigger/runtime/` directory remains untouched; no
  pre-planning `info.md` existed.

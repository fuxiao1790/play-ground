# 002 — Authoring surface & compile mapping

## Goal
Move the cadence knobs to their decided owners: gain rate on the travel triggers, cost on
the child skill definition. Map them through the compiler into the `Runtime*Setup` energy
fields and through `SkillDriver` into `TimedSpawnComponent`.

## Changes

### Triggers
`Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs` and
`Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs`:
- Replace `[Min(0.01f)] float intervalSeconds` with `[Min(0.01f)] float energyPerSecond = 2f`
  (accrual rate). **Do not** carry the old value via `FormerlySerializedAs` — the meaning
  changed; re-authoring is task 005.
- Replace `[Range(0,100)] float intervalJitterPercent` with
  `[Range(0,100)] float energyJitterPercent`. `FormerlySerializedAs("intervalJitterPercent")`
  is safe here (percent semantics unchanged) and avoids losing authored jitter.
- Leave the burst-geometry fields untouched: `projectileCount`/`sideSpreadDegrees` on the
  projectile trigger, `echoCount`/`scatterRadius` on the AOE trigger.

### Skill definition — new cost field
`Assets/Scripts/Skills/SkillDefinition.cs`:
- Add `[Min(0f)] public float spawnEnergyCost = 1f;` to `ProjectileDefinition` and to
  `AoeDefinitionBase` (so both AOE definition subclasses inherit it). Default `1f` keeps an
  un-migrated child skill firing at `1 / energyPerSecond` cadence rather than clamping to the
  min threshold. `MemberwiseClone`-based `DeepCopy()` already carries the new field.

### Compiler
`Assets/Scripts/Skills/SkillSetCompiler.cs` — `ApplyChildSpawn` and `ApplyAoeIntervalSpawn`:
- Replace the `intervalSeconds`/`intervalJitterSeconds` computation with:
  - `EnergyPerSecond = Mathf.Max(0.01f, trigger.energyPerSecond)`
  - `EnergyThreshold = Mathf.Max(<MinThresholdConst>, childSkillDefinition.spawnEnergyCost)`
  - `EnergyThresholdJitter = EnergyThreshold * Mathf.Clamp(trigger.energyJitterPercent,0,100) * 0.01f`
  - The child's `spawnEnergyCost` comes from the **authored child `SkillDefinition`**, read
    at the same point the child is compiled (the compiled `childDef` originates from that
    definition). Keep cost read from the child, honoring set isolation (index invariant 5).
- Burst geometry mapping (`Behavior`/`Count`/`ScatterRadius`) is unchanged.

### SkillDriver build helpers
`Assets/Scripts/Skills/SkillDriver.cs`:
- `ProjectileTimedSpawnFromSetup` / `AoeTimedSpawnFromSetup`: populate
  `EnergyPerSecond`/`EnergyThreshold`/`EnergyThresholdJitter` from the setup instead of the
  interval fields. Keep `JitterSeed`, `ChildKind`, `TemplateKey`.
- `IsTimedSpawnEnabled` (SkillDriver copy): test
  `JitterSeed > 0 && EnergyPerSecond > 0f && EnergyThreshold > 0f && TemplateKey != default`.

## Acceptance criteria
- Triggers expose `energyPerSecond` + `energyJitterPercent`; skills expose `spawnEnergyCost`.
- Compiler produces `Runtime*Setup` with energy fields sourced correctly (rate<-trigger,
  cost<-child skill).
- `TimedSpawnComponent` built by the driver carries energy fields; enabled gate updated.

## Scope
Medium. 5 files. Straightforward once 001's fields exist.

## Dependencies
001 (field vocabulary). Coordinated with 003 for a compiling build.

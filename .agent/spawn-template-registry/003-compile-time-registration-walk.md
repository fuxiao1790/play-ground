# 003 — Compile-time registration walk (build events, delete conversion)

## Scope

At compile time, build each interval child's **spawn event** (behavior filled, per-instance
fields default), register it, and store the returned `TemplateKey` on the setup object. Delete
the template-data structs and all template→event conversion.

## Changes

1. **Setup objects** (`Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`,
   `RuntimeAoeDefinition.cs`): keep `Hash128 TemplateKey` on `RuntimeChildSpawnSetup` /
   `RuntimeAoeIntervalSpawnSetup`; remove `SpawnConfig`/`HasRegisteredTemplate`-style fields that
   exist only to cache converted template data.

2. **Registration walk** (`Assets/Scripts/Skills/PlayerSkillDriver.cs` `RegisterIntervalTemplates()`,
   after `RegisterProjectileTypes`/`RegisterAoeTypes`): for each `ChildSpawnSetup` /
   `AoeIntervalSpawnSetup`, build the child **`ProjectileSpawnEvent`/`AoeSpawnEvent`** with all
   behavior fields (type, count/spread/pattern, geometry, damage, lifetime, tracking, render,
   impact snapshots, stack effect) and per-instance fields left default; call
   `combatRoot.RegisterTimedSpawnTemplate(evt)`; store the returned `Hash128` in
   `setup.TemplateKey`.

3. **Delete** `ProjectileSpawnTemplateData`/`AoeSpawnTemplateData` and the conversion helpers
   (`BuildProjectileChildSpawnConfig`, `ToIntervalProjectileChild`, the per-field
   `Build*IntervalSpawner`/`BuildIntervalProjectileChild`/`BuildIntervalAoeChild` in
   `SkillSpawnTranslator`). The translator no longer constructs child templates; it only reads
   `setup.TemplateKey` (+ timer config) to populate the source's `TimedSpawnComponent`.

## Acceptance criteria

- After `CompileAndRegister`, every interval setup has a non-default `TemplateKey` whose stored
  event exists in the matching registry map.
- No `…TemplateData` type or template→event conversion remains in the codebase.
- Identical child behavior across two setups yields the same `TemplateKey`.

## Dependencies

001, 002.

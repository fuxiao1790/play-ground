# 005 — Authoring: per-graph shape selection (prefab → registration)

## Goal
Let the author choose each VFX graph's data shape and thread it to registration
(which encodes it into the returned id), and feed authored duration/tick into
`VfxTimingData`. Keep the pulse slot and `AoePulseVfxSystem` unchanged.

## Changes

### Prefab authoring — `Assets/Scripts/Skills/Validator/LingeringAoePrefab.cs` and `BasicAoePrefab.cs`
- Next to each effect asset add a `VfxDataShape` selector, defaulting to `Basic`
  (backward-compatible). e.g. `spawnEffectShape`, `hitEffectShape`,
  `expireEffectShape`, `pulseEffectShape`, `armingEffectShape`. Expose getters.
- `BasicAoePrefab` may keep everything defaulting to `Basic`; the shape field still
  exists so it *could* use a `Timed`-shaped graph.
- Keep `pulseEffect` on `LingeringAoePrefab` (pulse slot kept).

### Definition layer — `Assets/Scripts/Skills/SkillDefinition.cs`
- Add abstract `VfxDataShape *EffectShape` getters mirroring the effect getters;
  `AoeDefinition`/`LingeringAoeDefinition` forward them from the prefab
  (`AoeDefinition` returns `Basic` for the pulse slot, matching its
  `PulseEffect => null`).

### Compile → runtime — `Assets/Scripts/Skills/SkillSetCompiler.cs`, `RuntimeAoeDefinition.cs`
- Carry the five shapes through the compiler into `RuntimeAoeDefinition`
  (new `SpawnEffectShape`… properties). `LifetimeSeconds` and `TickIntervalSeconds`
  already exist on `RuntimeAoeDefinition` (source for `VfxTimingData`); confirm the
  compiler maps `LingeringAoeDefinition.tickIntervalSeconds` into
  `RuntimeAoeDefinition.TickIntervalSeconds`.

### Registration — `Assets/Scripts/Skills/SkillDriver.cs` `RegisterAoeVfx`
- Register each asset with its authored shape; `Register` encodes the shape into the
  returned id, so `AoeVfxIds` stays 5 ints:
  ```csharp
  vfxIds = new AoeVfxIds {
    SpawnId  = vfxRoot.Register(def.SpawnEffect,  aoeDef.SpawnEffectShape),
    HitId    = vfxRoot.Register(def.HitEffect,    aoeDef.HitEffectShape),
    ExpireId = vfxRoot.Register(def.ExpireEffect, aoeDef.ExpireEffectShape),
    PulseId  = vfxRoot.Register(def.PulseEffect,  aoeDef.PulseEffectShape),
    ArmingId = vfxRoot.Register(def.ArmingEffect, aoeDef.ArmingEffectShape),
  };
  ```
  `Register` returning `0` (null asset or validation failure) leaves an inert slot
  (the emit helper skips id `0`).
- Ensure `AoeSpawnCommand` carries `Lifetime` + `TickIntervalSeconds` so
  `AoeSpawnApplySystem` (task 004) can populate `VfxTimingData`.

### Type registry plumbing — `AoeTypeRegistry.cs` / `AoeConfig.cs` / `AoeTypeDefinition`
- No signature changes needed — `AoeVfxIds` is still 5 ints; `SetVfxIds`/`Configure`
  keep passing the same struct. Verify they compile.

### Keep pulse path
- Do **not** delete `AoePulseVfxSystem`, `AoePulseVfxComponent`, `PulseId`, or
  `PulseEffect`. The per-tick pulse remains available; the single-duration field is
  the opt-in alternative (author fills a `Timed`-shaped Spawn/Pulse graph and, if
  using it as the field, leaves the pulse path unused).

## Acceptance criteria
- Compiles; existing prefabs default all slots to `Basic` and behave as today.
- Setting a slot's shape to `Timed` in a prefab registers that graph as a timed graph
  (validated) and makes its emits carry `Duration`/`TickInterval`.
- Pulse slot + system still function unchanged.

## Dependencies
001, 002. Pairs with 004.

## Scope
Large (authoring surface across several files, mostly mechanical).

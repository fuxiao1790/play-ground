# 004 — Telegraph VFX (trigger 4) + `initialDelaySeconds` / `TelegraphEffect` authoring

## Goal
Author the windup duration and telegraph effect on the AOE definitions, register the telegraph as VFX
trigger `4`, and emit it at spawn (in expansion) when `InitialDelaySeconds > 0`. Reuses the existing
VFX contract — no dispatcher change.

## Changes

### Authoring source for `initialDelaySeconds`
Add a serialized `[Min(0)] float initialDelaySeconds` and surface it, mirroring how lifetime/effects flow:
- **[BasicAoePrefab.cs](../../Assets/Scripts/Skills/Validator/BasicAoePrefab.cs)** and
  **[LingeringAoePrefab.cs](../../Assets/Scripts/Skills/Validator/LingeringAoePrefab.cs)** — add the
  serialized field + `InitialDelaySeconds` getter, plus a `telegraphEffect` `VisualEffectAsset` +
  getter (mirror `spawnEffect`/`hitEffect`).
- **[AoeConfig.cs](../../Assets/Scripts/System/Aoe/AoeConfig.cs)** — expose `InitialDelaySeconds`
  (virtual, default from prefab) and `TelegraphEffect` (mirror `PulseEffect`); pass `TelegraphEffect`
  into `CreateTypeDefinition` (extend `AoeTypeDefinition.Configure`).
- **[RuntimeAoeDefinition.cs](../../Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs)** — add
  `float InitialDelaySeconds { get; set; }` and `VisualEffectAsset TelegraphEffect { get; set; }`;
  thread `TelegraphEffect` into `CreateTypeDefinition`.
- **`AoeTypeDefinition`** — add a `TelegraphEffect` slot + parameter in `Configure`.
- **`SkillSetCompiler`** ([:258 area](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L258)) and any
  definition-copy site — carry `InitialDelaySeconds` + `TelegraphEffect` across.
- Ensure the value reaches the `child.InitialDelaySeconds` read added in 002
  ([PlayerSkillDriver BuildAoeTemplate](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L695)).

### Register the telegraph trigger
- [PlayerSkillDriver.RegisterAoeTypeDefinition](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L570):
  after the trigger-3 register, add
  `vfxRoot.Register(aoeDef.TypeId, 4, definition.TelegraphEffect, requireAreaSizeContract: true);`.
  A null `TelegraphEffect` registers nothing (same as other optional effects) — safe when unauthored.

### Emit the telegraph at spawn
- [AoeSpawnExpansionSystem.cs:88-91](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs#L88)
  (the trigger-0 spawn emit) — in **both** the impact and lingering expansion jobs, additionally
  enqueue a telegraph `VfxPendingSpawn { TypeId, Trigger = 4, Position, AreaSize }` **when
  `cmd.InitialDelaySeconds > 0f`**. Keep the trigger-0 spawn emit unchanged (both fire). The command
  is already in scope in the expansion job; the queue + `ProducerHandle` chaining are already wired.

## Notes
- **No dispatcher/contract change.** Telegraph rides `Positions`+`AreaSizes`+`SpawnCount`. The graph
  animates over its own baked duration, matched to the fixed authored windup.
- **AreaSize** for the telegraph = the AOE's `AreaSize` (same value used for spawn/hit), so the
  telegraph scales with the AOE footprint.

## Acceptance criteria
- Compiles. Authored `initialDelaySeconds > 0` + a `telegraphEffect` ⇒ one trigger-4 VFX emitted at
  spawn per AOE, in addition to the spawn VFX.
- Unauthored telegraph (null) or `initialDelaySeconds == 0` ⇒ no trigger-4 emit; behavior unchanged.
- Existing spawn/hit/expire/pulse graphs and their contract validation are untouched.

## Scope / complexity
Medium. Broad but mechanical authoring plumbing (mirrors an existing effect end-to-end) + one register
line + two expansion emit sites.

## Dependencies
002 (`InitialDelaySeconds` on the command/child). Independent of 001/003 for the VFX emit, but the
gameplay windup (001-003) should land first so the telegraph corresponds to a real windup.

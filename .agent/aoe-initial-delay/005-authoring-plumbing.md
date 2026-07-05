# 005 — Authoring surface + data plumbing

## Goal
Expose `initialDelaySeconds` and a `DelayEffect` VFX asset on AOE authoring, carry
them through every path that builds an `AoeSpawnCommand`, and register the
telegraph asset under trigger `4`.

## Authoring fields
### `Assets/Scripts/Skills/SkillDefinition.cs`
- `AoeDefinitionBase`: add `[Min(0f)] public float initialDelaySeconds = 0f;` and
  `public abstract VisualEffectAsset DelayEffect { get; }`.
- `AoeDefinition`: `public override VisualEffectAsset DelayEffect => prefab != null ?
  prefab.DelayEffect : null;`
- `LingeringAoeDefinition`: same, from its `prefab.DelayEffect`.

### `Assets/Scripts/Skills/Validator/BasicAoePrefab.cs` and `LingeringAoePrefab.cs`
- Add `[SerializeField] private VisualEffectAsset delayEffect;` + `public
  VisualEffectAsset DelayEffect => delayEffect;` + a `delayEffect` param in
  `Configure(...)` (default `null`).

### `Assets/Scripts/System/Aoe/AoeConfig.cs` + `AoeTypeRegistry.cs` (`AoeTypeDefinition`)
- `AoeConfig`: add `[SerializeField, Min(0f)] private float initialDelaySeconds;`
  and pass through. Prefer sourcing `delayEffect` from `basicPrefab` (add a
  `DelayEffect` accessor on `BasicAoePrefab`, already above) in
  `CreateTypeDefinition`.
- `AoeTypeDefinition`: add `[SerializeField] private VisualEffectAsset delayEffect;`
  + `public VisualEffectAsset DelayEffect => delayEffect;` + `delayEffect` param in
  `Configure(...)`. (No `initialDelay` needed on the *visual* type definition — it
  only carries VFX assets; the delay scalar lives on the command template.)

### `Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs`
- Add `public float InitialDelaySeconds { get; set; }` and `public VisualEffectAsset
  DelayEffect { get; set; }`.
- `CreateTypeDefinition()`: pass `DelayEffect` into `def.Configure(...)`.

### `Assets/Scripts/Skills/SkillSetCompiler.cs`
- In the `RuntimeAoeDefinition { ... }` initializer (~line 249): add
  `InitialDelaySeconds = Mathf.Max(0f, a.initialDelaySeconds),` and `DelayEffect =
  a.DelayEffect,`.

## Command builders (single source of truth = `AoeSpawnCommand.InitialDelaySeconds`)
### `Assets/Scripts/Skills/PlayerSkillDriver.cs`
- `BuildAoeTemplate(...)` (~line 695): set `InitialDelaySeconds =
  child.InitialDelaySeconds` in the returned `AoeSpawnCommand`. This covers the
  registered-template path used by direct casts, timed children, on-hit spawns, and
  stack detonations.
- VFX registration (~line 578): add
  `vfxRoot.Register(aoeDef.TypeId, 4, definition.DelayEffect, requireAreaSizeContract: true);`

### `Assets/Scripts/System/Common/CombatRoot.cs`
- `AoeCommandFor(request, aoeId)` (~line 460): set `InitialDelaySeconds =
  request.InitialDelaySeconds`.

### `Assets/Scripts/System/Aoe/AoeRuntimeEvents.cs` (`AoeSpawnRequest`)
- Add ctor param `float initialDelaySeconds = 0f` + `public float
  InitialDelaySeconds { get; }` (`Mathf.Max(0f, initialDelaySeconds)`), threaded
  from callers that build requests (config-driven `Spawn(...)` path). Default keeps
  every existing caller at `0`.

## Editor-only registration paths
Check `Assets/Editor/BareMinimumPrototypeBuilder.cs` and any test fixtures that call
`Configure(...)` / `Register(typeId, trigger, ...)` for AOEs; extend for `delayEffect`
/ trigger `4` only where they already register 0..3 (optional assets → passing
`null` is a no-op).

## Acceptance criteria
- All AOE authoring assets can set an initial delay + telegraph asset.
- Every `AoeSpawnCommand` construction site compiles with the new field; unset =>
  `0` => no windup.
- Trigger `4` registered from `DelayEffect` for player AOE types; graph missing the
  area-size contract fails registration with the existing clear error.

## Dependencies
001 (command field). Pairs with 004 for the asset to have an effect.

## Scope
Medium (wide but mechanical fan-out).

# 005 — Author VfxDataType per effect + registration wiring

## Goal
Let content declare which contract an effect uses, and have registration build the matching
dispatcher resource kind AND populate the `DataTypeByKey` lookup — the single source of truth for
the contract choice.

## Authoring
Keep existing `VisualEffectAsset` slots untouched (do not migrate). Add the contract type only
where the new path applies, minimally:
- **`Assets/Scripts/Skills/Validator/LingeringAoePrefab.cs`** — the only prefab that can host an
  Area-Timed self-pulsing spawn. Add `[SerializeField] private VfxDataType spawnEffectDataType =
  VfxDataType.Area;` + `public VfxDataType SpawnEffectDataType => spawnEffectDataType;`.
  (Default `Area` => existing prefabs unchanged in behavior.)
- Flow it through the authoring chain that already carries `SpawnEffect`:
  `RuntimeAoeDefinition` (add `VfxDataType SpawnEffectDataType { get; set; } = Area;`),
  `AoeTypeDefinition`/`AoeConfig.cs` `Configure(...)` (add the field alongside `SpawnEffect`).
  Basic (impact) AOEs stay `Area` only — no new field needed there.

## Registration: `Assets/Scripts/Skills/SkillDriver.cs` `RegisterAoeVfx` (~line 627-637)
- For the Spawn slot, switch on the authored type:
  ```csharp
  if (definition.SpawnEffectDataType == VfxDataType.AreaTimed)
      vfxRoot.RegisterAreaTimed(aoeDef.TypeId, AoeVfxTrigger.Spawn, definition.SpawnEffect);
  else
      vfxRoot.Register(aoeDef.TypeId, AoeVfxTrigger.Spawn, definition.SpawnEffect, requireAreaSizeContract: true);
  ```
- Regardless of asset presence, also record the mapping so producers can branch:
  call the singleton setter (task 001) `SetEffectDataType(aoeDef.TypeId, AoeVfxTrigger.Spawn,
  definition.SpawnEffectDataType)`. Reach the singleton from managed code on the main thread via the
  dispatch system (`World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<...>()` or the
  established access used elsewhere for one-time setup) — this is registration/setup, not a hot path.
- Other slots (Hit/Expire/Pulse/Arming) keep their current `Register(..., requireAreaSizeContract:
  true)` calls unchanged.

## Constraints
- Registration is cross-object setup -> keep it in the existing setup call path (not `Awake`),
  per `Docs/coding-standards.md:45-58`.
- Type authored via Inspector (`Docs/coding-standards.md:27` prefer Inspector DI).

## Acceptance criteria
- A LingeringAoePrefab with `spawnEffectDataType = AreaTimed` registers an Area-Timed graph and sets
  `DataTypeByKey[(TypeId, Spawn)] = AreaTimed`; producer branch (004) then fires the timed path.
- All existing prefabs default to `Area`; their registration and visuals are unchanged.

## Dependencies
- 001 (setter + lookup), 003 (`RegisterAreaTimed`).

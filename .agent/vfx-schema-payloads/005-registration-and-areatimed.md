# 005 — Authored VfxDataType per effect + unified registration

## Goal
Each VFX effect authors an explicit, type-safe `VfxDataType` (ECS-owned), chosen per effect and
independent of whether a projectile or AOE triggers it. Registration is one uniform path that
reads `(asset, dataType)` bindings; no baked "projectiles=Point / AOEs=Area" rule.

## Authoring binding (type-safe pairing)
Introduce a serializable value type pairing an asset with its authored data type:
```csharp
[Serializable]
public struct VfxEffectBinding
{
    public VisualEffectAsset Asset;
    public VfxDataType DataType;   // ECS-owned enum, authored per effect
}
```
Replace each standalone `VisualEffectAsset xEffect` authoring/runtime field with a
`VfxEffectBinding xEffect`, threaded from the prefab serialized fields → runtime/type definitions
→ the `vfxRoot.Register` call. This makes it impossible to assign an effect asset without also
declaring its GPU data type.

Pattern applies across (representative — same edit each):
- Authoring prefabs: `Skills/Validator/BasicAttackPrefab.cs`, `BasicAoePrefab.cs`,
  `LingeringAoePrefab.cs`; `System/Aoes/AoeTypeRegistry.cs`.
- Definitions that carry the effects through: `Skills/Runtime/RuntimeProjectileDefinition.cs`,
  `RuntimeAoeDefinition.cs`, and `AoeTypeDefinition.Configure(...)` (the AOE path SkillDriver
  reads from).
- `Skills/SkillDefinition.cs` abstract `SpawnEffect/HitEffect/...` getters → return bindings.

## SkillDriver registration (unified) — writes BOTH sides
Collapse the data-type baking. For each authored binding, register the managed graph AND publish
the authored type into the ECS lookup so producers can switch on it (task 004):
```csharp
void RegisterOne(int typeId, VfxTrigger trigger, VfxEffectBinding b)
{
    if (b.Asset == null) return;                 // None slot stays None → producers skip
    vfxRoot.Register(typeId, trigger, b.Asset, b.DataType);      // managed graph + validation
    dispatchSystem.SetEffectDataType(typeId, trigger, b.DataType); // ECS lookup (task 002)
}
```
`RegisterProjectileVfx`/`RegisterAoeVfx` each just loop their definition's bindings calling
`RegisterOne`. Trigger slots are now `Spawn/Hit/Expire/Arming` for both — the old AOE-only `Pulse`
slot is removed (task 006), so drop `PulseEffect` from the authoring chain (`RuntimeAoeDefinition`,
`AoeTypeRegistry`, `LingeringAoePrefab`, `SkillDefinition`, `AoeTypeDefinition.Configure`).
`dispatchSystem` is `world.GetExistingSystemManaged<CombatVfxDispatchSystem>()` (SkillDriver already
drives the combat world for type registration — confirm the handle path).

> Invariant: the authored `DataType` passed to `vfxRoot.Register` (validated against the graph)
> and the one written to `DataTypeByKey` are the same value → the producer's switch and the graph's
> schema always agree for a given key.

## AreaTimed lives on the lingering AOE's Spawn effect (no Pulse trigger)
- There is no `Pulse` trigger. A **lingering AOE authors its `Spawn` effect as `VfxDataType.AreaTimed`**
  (the graph self-pulses over `Duration` at `TickRate`); impact-AOE and projectile Spawn effects
  author `Area`/`Point`. Registration + `DataTypeByKey[(typeId, Spawn)] = AreaTimed` is the same
  flow as any other effect.
- Effect slots to register are now just `Spawn/Hit/Expire/Arming` (Pulse removed).
- The runtime behavior change (Spawn carries Duration+TickRate; delete `AoePulseVfxSystem`/
  `AoePulseVfxComponent`; arming unchanged) is **task 006**.

## Type safety / migration
Task-003 registration does **exact-match** validation: the authored `VfxDataType`'s schema
buffers must equal the graph asset's exposed buffers. Any existing prefab whose effect needs
`AreaSizes`/`Durations`/`TickRates` but is left at the default `Point` fails loudly at
registration, naming the effect — self-guiding migration. New serialized `VfxDataType` fields
default to `Point`; set `Area`/`AreaTimed` where the graph needs them.

## Acceptance
- Every effect registers with an authored `VfxDataType`; no code path derives it from
  projectile/AOE. No `Pulse` slot remains.
- A lingering AOE's `Spawn` graph (authored `AreaTimed`) receives populated `Durations`/`TickRates`;
  a mismatched authored type is rejected.

## Depends on
- 001–004.

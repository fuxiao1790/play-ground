# 001 — VfxDataType enum + flat DataTypeByKey lookup

## Goal
Give producers a Burst-readable, per-`(TypeId, Trigger)` way to pick a contract, stored as a flat
side table (no per-entity component, no archetype growth).

## Changes
- **`Assets/Scripts/System/Vfx/AoeVfxEcsComponents.cs`**
  - Add `public enum VfxDataType : byte { Area = 0, AreaTimed = 1 }`. `Area = 0` is deliberate:
    default-zero == the existing contract, so any unauthored key resolves to today's behavior.
- **`Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`** (`CombatAoeVfxDispatchSingleton`)
  - Add `public NativeList<VfxDataType> DataTypeByKey;` with an `// ECS Lifecycle:` comment
    (created in `OnCreate`, grown at registration, disposed in `OnDestroy`).
  - Define `const int TriggerCount = 5;` (mirrors `AoeVfxTrigger` value count) and a static
    `int FlatIndex(int typeId, AoeVfxTrigger t) => typeId * TriggerCount + (byte)t;`.
  - `OnCreate`: allocate `DataTypeByKey` (`Allocator.Persistent`, initial capacity small).
  - `OnDestroy`: dispose `DataTypeByKey` if created.
  - Add a main-thread method `public void SetEffectDataType(int typeId, AoeVfxTrigger trigger,
    VfxDataType type)` that grows `DataTypeByKey` (resize + fill new slots with `Area`) up to
    `FlatIndex(typeId,trigger)+1` and writes the value. Called from managed registration (task 005).

## Notes / constraints
- Growth happens only at registration (setup), never per frame — honors the hot-path alloc rule.
- Lookup is read by producer jobs off the singleton (RO): `dt = DataTypeByKey[FlatIndex(id, trig)]`
  with a bounds guard (`index < Length ? ... : Area`).

## Acceptance criteria
- `VfxDataType` compiles and is ECS-owned (usable from Burst jobs and managed registration).
- Singleton owns `DataTypeByKey`, created/disposed correctly, default slot value `Area`.
- `SetEffectDataType` grows and writes without per-frame allocation; out-of-range reads fall back
  to `Area`.

## Scope
Small. Foundation for 004 (producer read) and 005 (registration write).

# 001 — Data-shape core types, id encoding & per-shape containers

## Goal
Introduce the ECS-owned data-shape vocabulary, the `VfxId` encoding that carries a
graph's shape, and the per-shape native containers. No producers/consumers wired
yet. This is the single source of truth every later task references.

## Naming
Shapes are named by their **data**, not by AOE use-case: `Basic` (no timing) vs
`Timed` (carries `Duration` + `TickInterval`). Only the `Timed` request/shape has the
timing fields; `Basic` does not.

## Changes

### New: `Assets/Scripts/System/Vfx/VfxDataShapes.cs`
- `public enum VfxDataShape : byte { Basic = 0, Timed = 1 }`
- Per-shape **request** structs (queued payloads; hard-coded, exact):
  ```csharp
  // Basic: mirrors buffers Positions(float2), AreaSizes(float)
  public struct VfxSpawnRequest { int VfxId; float2 Position; float AreaSize; }

  // Timed: adds Durations(float), TickIntervals(float)
  public struct TimedVfxSpawnRequest
  { int VfxId; float2 Position; float AreaSize; float Duration; float TickInterval; }
  ```
  `VfxSpawnRequest` is the **rename** of today's `AoeVfxSpawnRequest` (fields
  unchanged); update all references in the rename.
- `VfxDataShapeTable` — static descriptor registry; the one place a shape's buffer
  contract is declared. Per shape, an ordered set of expected exposed **buffer**
  properties `(string name, System.Type type)` (`SpawnCount:int` + `OnSpawn` event
  are common and validated separately):
  ```csharp
  Basic -> { ("Positions", GraphicsBuffer), ("AreaSizes", GraphicsBuffer) }
  Timed -> { ("Positions", GraphicsBuffer), ("AreaSizes", GraphicsBuffer),
             ("Durations", GraphicsBuffer), ("TickIntervals", GraphicsBuffer) }
  ```
  Helpers: `BufferNamesFor(shape)`, `BufferCountFor(shape)`, property-name consts.
- **`VfxId` encoding** (one graph = one shape ⇒ shape lives in the id):
  ```csharp
  // int VfxId; bit 31 stays 0 (must be positive). High bits = shape ordinal,
  // low bits = per-shape 1-based local index. 0 = no-VFX sentinel.
  const int ShapeShift = 27;                    // 8 shapes (bits 27..30), ~134M ids/shape
  const int LocalMask  = (1 << ShapeShift) - 1;
  static int  EncodeId(VfxDataShape shape, int localIndex1Based)  // asserts index in range
  static VfxDataShape DecodeShape(int id)       // (VfxDataShape)((id >> ShapeShift) & 0x7)
  static int  DecodeLocalIndex(int id)          // id & LocalMask   (1-based)
  ```
  `DecodeShape`/`DecodeLocalIndex` must be Burst-callable (plain static math).

### `Assets/Scripts/System/Vfx/AoeVfxEcsComponents.cs`
- **Keep `AoeVfxIds` as 5 `int`s** (`Spawn/Hit/Expire/Pulse/Arming`); no slot struct.
- Add the timing carrier (generic name, not AOE-specific):
  ```csharp
  // Authored per-instance values consumed by Timed-shaped emits.
  public struct VfxTimingData : IComponentData { public float Duration; public float TickInterval; }
  ```

### `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs` — singleton only
- Rename the existing impact fields to the `Basic` set and add the `Timed` set, all
  `Allocator.Persistent`, grow-only:
  ```csharp
  // Basic (rename of PendingAoeSpawns / SortedPositions / SortedAreaSizes / BucketOffsets)
  NativeQueue<VfxSpawnRequest> PendingBasicSpawns;
  NativeList<float2> BasicSortedPositions;
  NativeList<float>  BasicSortedAreaSizes;
  NativeList<int>    BasicBucketOffsets;
  // Timed
  NativeQueue<TimedVfxSpawnRequest> PendingTimedSpawns;
  NativeList<float2> TimedSortedPositions;
  NativeList<float>  TimedSortedAreaSizes;
  NativeList<float>  TimedSortedDurations;
  NativeList<float>  TimedSortedTickIntervals;
  NativeList<int>    TimedBucketOffsets;
  ```
  Allocate in `OnCreate`, dispose in `OnDestroy` (guard `IsCreated`). Keep the single
  existing `ProducerHandle`. (The dispatcher/system/singleton/resources class names
  keep their existing `Aoe` naming; only the data-shape/request/timing/queue
  identifiers go generic.)

## Acceptance criteria
- Compiles (no `AoeVfxIds` field renames ⇒ existing `vfxIds.*Id` reads still build;
  the `AoeVfxSpawnRequest → VfxSpawnRequest` rename is applied everywhere).
- `VfxDataShapeTable` is the sole declaration of each shape's buffer set.
- `EncodeId`/`DecodeShape`/`DecodeLocalIndex` round-trip for both shapes and keep ids
  positive/nonzero for `localIndex >= 1`.
- New per-shape containers allocated/disposed symmetrically.

## Dependencies
None (first task).

## Scope
Small–medium.

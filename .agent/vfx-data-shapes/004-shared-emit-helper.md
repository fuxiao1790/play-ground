# 004 — Shared shape-aware emit helper & emit-site updates

## Goal
One Burst-safe helper turns a raw `VfxId` into the correct request on the correct
queue by **decoding the shape from the id**, so every emit site is shape-agnostic and
all AOEs stay in one combined flow. Populate `VfxTimingData` at spawn.

## Changes

### New: `Assets/Scripts/System/Vfx/VfxEmit.cs`
- Static, Burst-compatible; takes a plain `int` id (from `AoeVfxIds`):
  ```csharp
  public static void Enqueue(
      int vfxId, float2 position, float areaSize, in VfxTimingData timing,
      in NativeQueue<VfxSpawnRequest>.ParallelWriter basic, bool hasBasic,
      in NativeQueue<TimedVfxSpawnRequest>.ParallelWriter timed, bool hasTimed)
  {
      if (vfxId <= 0) return;
      switch (VfxDataShapeTable.DecodeShape(vfxId))
      {
          case VfxDataShape.Basic:
              if (hasBasic) basic.Enqueue(new VfxSpawnRequest {
                  VfxId = vfxId, Position = position, AreaSize = areaSize });
              break;
          case VfxDataShape.Timed:
              if (hasTimed) timed.Enqueue(new TimedVfxSpawnRequest {
                  VfxId = vfxId, Position = position, AreaSize = areaSize,
                  Duration = timing.Duration, TickInterval = timing.TickInterval });
              break;
      }
  }
  ```

### Populate `VfxTimingData` at spawn — `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- Add `VfxTimingData` to the AOE archetype; set
  `{ Duration = cmd.Lifetime, TickInterval = cmd.TickIntervalSeconds }` from the spawn
  command (authored `LingeringAoeDefinition.lifetimeSeconds` / `tickIntervalSeconds`;
  confirm both reach `AoeSpawnCommand` — add fields + the `SkillDriver` mapping in
  task 005 if missing). Reset on reuse. Basic-only AOEs get `VfxTimingData` too
  (zeros; unused unless a slot's id decodes to `Timed`).
- Leave `AoePulseVfxComponent`/`PulseVfxFor` untouched (pulse kept).

### Update emit sites to call `VfxEmit.Enqueue`
Each site already fetches the current VFX queue; add both queue writers + flags from
the singleton, read `VfxTimingData`, and replace inline
`if (vfxIds.XId > 0) enqueue(...)` with `VfxEmit.Enqueue(vfxIds.XId, pos, area, timing, ...)`.
Combine the job handle into the same `ProducerHandle` (already done today).
- `Assets/Scripts/System/Lifetime/CombatArmingSystem.cs` — `SpawnId` at arm-complete.
- `Assets/Scripts/System/Aoes/AoeSpawnExpansionSystem.cs` — `SpawnId`/`ArmingId` at go-live.
- `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs` — `ExpireId` at expire
  (extend/replace the VFX-emitting `CombatDeathUtility.Kill(...)` overload to take the
  id + both writers + `VfxTimingData`, or emit in the job and keep `Kill` VFX-free).
- `Assets/Scripts/System/Aoes/AoeCollisionCore.cs` (`EmitHit`) — `HitId`; both
  collision systems pass both writers + the entity's `VfxTimingData`. Add
  `VfxTimingData` to both collision queries and thread it through
  `RunCollision`/`EmitHit`; keep the once-per-pass `hitVfxEmitted` guard.

## Acceptance criteria
- Compiles; content using only `Basic`-shaped graphs behaves exactly as before.
- An id that decodes `Timed` enqueues one `TimedVfxSpawnRequest` carrying authored
  `Duration`/`TickInterval` at its emit moment, no per-frame re-emission from that path.
- No emit site branches on `LingeringAoeTag`; all route by `DecodeShape(id)`.
- No emitter reads `CombatLifetimeComponent.Remaining` for `Duration`.

## Dependencies
001, 002, 003. Pairs with 005 (registration must encode `Timed` ids for the timed
path to ever be selected).

## Scope
Large — most systems touched. Split per emit-site if review prefers.

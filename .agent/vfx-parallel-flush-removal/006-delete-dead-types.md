# 006 — Delete dead VFX types

**Files:** `Assets/Scripts/System/Vfx/VfxFlushJob.cs` (delete),
`Assets/Scripts/System/Vfx/VfxEcsComponents.cs` (edit)
**Depends on:** 003, 004, 005 (all producers migrated) and 001/002 (consumer
migrated) — only delete once nothing references these types.
**Scope:** small

## Change

- Delete `Assets/Scripts/System/Vfx/VfxFlushJob.cs` entirely (both `VfxFlushJob`
  and `VfxStreamFlushJob`). Remove its `.meta` too.
- In `VfxEcsComponents.cs`, delete `VfxSingleton` and `VfxSpawnRequestElement`.
  Keep `VfxPendingSpawn` as the single payload type; update its `ECS Lifecycle`
  comment to: queued by simulation jobs into the shared
  `NativeQueue<VfxPendingSpawn>` owned by `CombatVfxDispatchSystem`, drained in
  presentation. The file may no longer need `using Unity.Entities;` if nothing
  else there uses it — keep only the usings actually referenced (`Unity.Mathematics`
  for `float2`).

## Verification gate (run before/with this task)

`grep -rn "VfxFlushJob\|VfxStreamFlushJob\|VfxSingleton\|VfxSpawnRequestElement"
Assets/Scripts Assets/Tests` must return nothing after 007. (Docs handled in 008.)

## Acceptance criteria

- Project compiles with no references to the four deleted types in `Assets/`.
- `VfxPendingSpawn` remains and compiles with a minimal using set.

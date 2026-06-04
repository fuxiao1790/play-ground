# VFX System Implementation Plan

## Goal
Wire VFX Graph particle effects into the ECS simulation. Authors place VisualEffectAssets on prefabs/configs; roots bake them at startup; ECS systems enqueue events; roots upload positions to GraphicsBuffer each LateUpdate and fire VisualEffect events.

**Target:** ~1000s of VFX events/frame at 120fps with no per-event CPU expansion.

---

## Architecture Summary

- One `VisualEffect` scene instance per `(typeId, trigger)` pair.
- VFX Graph asset carries effect definition — pre-resident on GPU after first frame.
- Per-frame: CPU stages positions into `float2[]`, uploads once to `GraphicsBuffer`, calls `SetGraphicsBuffer("Positions", buf)` + `SetInt("SpawnCount", count)` + `SendEvent(...)`.
- ECS writes `VfxSpawnRequestElement` scope buffer entries (one per event, 12 bytes each).
- Roots drain the buffer each LateUpdate, group by `(typeId, trigger)`, dispatch.

### Trigger Values
| Value | Name    | When fired                          |
|-------|---------|-------------------------------------|
| 0     | spawn   | projectile/AOE materialized         |
| 1     | hit     | projectile hits target / AOE hits target |
| 2     | expire  | projectile lifetime ends / lingering AOE lifetime ends |
| 3     | pulse   | lingering AOE tick (regardless of hits) |

---

## New Files

### `Assets/Scripts/System/Vfx/VfxEcsComponents.cs`
```csharp
// ECS Lifecycle: transient native payload; not added to entities; queued by simulation jobs, drained by VfxFlushJob.
public struct VfxPendingSpawn
{
    public int TypeId;
    public byte Trigger;   // 0=spawn 1=hit 2=expire 3=pulse
    public float2 Position;
}
```
No scope buffer component needed — flush jobs write directly into per-kind `NativeList<float2>` on the dispatcher.

### `Assets/Scripts/System/Vfx/VfxFlushJob.cs`
`[BurstCompile] IJob`. Drains `NativeQueue<VfxPendingSpawn>` directly into per-kind staging lists on the dispatcher.
```csharp
[BurstCompile]
public struct VfxFlushJob : IJob
{
    public NativeQueue<VfxPendingSpawn> Pending;
    // One NativeList<float2> per registered (typeId, trigger) key; passed from dispatcher at schedule time.
    // Use NativeHashMap<int, int> KeyToIndex + flat NativeArray<NativeList<float2>> or pass individual lists.
    public NativeList<float2> Trigger0; // spawn
    public NativeList<float2> Trigger1; // hit
    public NativeList<float2> Trigger2; // expire
    public NativeList<float2> Trigger3; // pulse
    public int MaxPerFrame;

    public void Execute()
    {
        while (Pending.TryDequeue(out VfxPendingSpawn p))
        {
            NativeList<float2> list = p.Trigger switch
            {
                0 => Trigger0, 1 => Trigger1, 2 => Trigger2, 3 => Trigger3,
                _ => default
            };
            if (!list.IsCreated || list.Length >= MaxPerFrame) continue;
            list.Add(p.Position);
        }
    }
}
```
Note: each system that emits VFX events allocates its own `NativeQueue<VfxPendingSpawn>` and schedules one `VfxFlushJob`. VFX flush jobs for different systems are chained serially (each waits on prior) to avoid concurrent writes to the same `NativeList<float2>`. If this serialization ever shows up in profiling, switch to per-system per-kind lists + a combine job before `Dispatch`.

### `Assets/Scripts/System/Vfx/CombatVfxDispatcher.cs`
Managed class; one instance owned by each root (ProjectileRoot, AoeRoot).

```csharp
public sealed class VfxTypeResources : IDisposable
{
    public VisualEffect Instance;           // instantiated in scene
    public GraphicsBuffer PositionBuffer;   // GPU buffer: maxPerFrame * 8 bytes (float2)
    public NativeList<float2> Staging;      // Burst-writable; cleared at frame start; written by flush jobs
    public void Dispose() { PositionBuffer?.Release(); Staging.Dispose(); if (Instance != null) Object.Destroy(Instance.gameObject); }
}

public sealed class CombatVfxDispatcher : IDisposable
{
    // key = typeId * 256 + trigger
    private readonly Dictionary<int, VfxTypeResources> resources = new();
    private readonly Transform parent;

    public CombatVfxDispatcher(Transform parent) { this.parent = parent; }

    // Call once per (typeId, trigger) at root startup. asset=null -> registered but silent.
    public void Register(int typeId, byte trigger, VisualEffectAsset asset, int maxPerFrame)

    // Returns NativeList<float2> for the given key, or an invalid list if key unregistered.
    // Flush jobs call this to get the write target for each event kind.
    public NativeList<float2> GetStaging(int typeId, byte trigger)

    // Called in LateUpdate after all flush jobs complete. Main thread work: O(registered resources).
    // For each resource: SetData(staging) -> SetGraphicsBuffer -> SetInt -> SendEvent -> staging.Clear()
    public void Dispatch()

    public void Dispose()
}
```

**Write path (Burst flush job):**
Each system's `VfxFlushJob` (draining `NativeQueue<VfxPendingSpawn>`) writes positions directly into the per-kind `NativeList<float2>`:
```
while queue.TryDequeue(out p):
    list = stagingByKey[p.TypeId * 256 + p.Trigger]  // NativeList<float2>
    if list.IsCreated && list.Length < maxPerFrame:
        list.Add(p.Position)
```
Flush jobs for different systems are chained serially on the VFX dependency (same NativeLists, no concurrent writes needed). If contention becomes a problem, each system gets its own per-kind `NativeList<float2>`, and a combine job appends them into the shared list before `Dispatch`.

**Dispatch (main thread, zero compute):**
```
foreach (key, res) in resources:
    if res.Staging.Length == 0: continue
    res.PositionBuffer.SetData(res.Staging.AsArray(), 0, 0, res.Staging.Length)
    res.Instance.SetGraphicsBuffer("Positions", res.PositionBuffer)
    res.Instance.SetInt("SpawnCount", res.Staging.Length)
    res.Instance.SendEvent("OnSpawn")
    res.Staging.Clear()
```
No event iteration on main thread. Upload cost only.

---

## Modified Files

### `Assets/Scripts/Attack/BasicAttackPrefab.cs`
Add three optional VFX fields (no changes to existing fields or validation):
```csharp
[SerializeField] private VisualEffectAsset spawnEffect;
[SerializeField] private VisualEffectAsset hitEffect;
[SerializeField] private VisualEffectAsset expireEffect;

public VisualEffectAsset SpawnEffect => spawnEffect;
public VisualEffectAsset HitEffect => hitEffect;
public VisualEffectAsset ExpireEffect => expireEffect;
```
All nullable; no validation required (missing effect = no VFX for that trigger).

### `Assets/Scripts/System/Aoe/AoeTypeRegistry.cs` - `AoeTypeDefinition`
Add four VFX fields (no changes to existing shape/visual bake):
```csharp
[SerializeField] private VisualEffectAsset spawnEffect;
[SerializeField] private VisualEffectAsset hitEffect;
[SerializeField] private VisualEffectAsset expireEffect;
[SerializeField] private VisualEffectAsset pulseEffect;

public VisualEffectAsset SpawnEffect => spawnEffect;
public VisualEffectAsset HitEffect => hitEffect;
public VisualEffectAsset ExpireEffect => expireEffect;
public VisualEffectAsset PulseEffect => pulseEffect;
```
`Configure(...)` signature extended with optional VFX params (default null).

### `Assets/Scripts/System/Aoe/AoeEcsComponents.cs`
New component added only to lingering AOE entities (`IsPulse == 0`):
```csharp
// ECS Lifecycle: optional lingering AOE component; added at spawn materialization for IsPulse==0 AOEs; reset on reuse; kept until root teardown.
public struct AoePulseVfxComponent : IComponentData
{
    public float RemainingInterval;
    public float Interval;
}
```

### `Assets/Scripts/System/Projectile/ProjectileLifetimeSystem.cs`
Add `NativeQueue<VfxPendingSpawn> vfxPending` per frame. On lifetime expire enqueue `VfxPendingSpawn { TypeId, Trigger=2, Position }`. Requires `CombatKinematicsComponent` added to `Execute` signature.

`ProjectileLifetimeJob` additions:
- Field: `NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending`
- On expire: `VfxPending.Enqueue(new VfxPendingSpawn { TypeId=identity.TypeId, Trigger=2, Position=kinematics.Position })`

Dependency graph:
```
lifetimeJob -> vfxFlushHandle (writes to dispatcher staging lists; chained after prior VFX flush)
lifetimeJob -> recycleFlushHandle
CombineDependencies(vfxFlushHandle, recycleFlushHandle) -> vfxPending.Dispose -> recycled.Dispose
```

### `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`
Add `NativeQueue<VfxPendingSpawn> vfxPending` per frame.

`ProjectileCollisionJob` additions:
- On each confirmed hit: enqueue `VfxPendingSpawn { ..., Trigger=1, Position=kinematics.Position }`
- On Deactivate path (pierce consumed / scope null / lifetime zero): enqueue `{ ..., Trigger=2 }`

Note: Trigger=2 can fire from both LifetimeSystem and CollisionSystem. Each deactivation path fires exactly once per entity — no dedup needed.

`VfxFlushJob` scheduled after `collisionHandle`, chained after any prior system's `VfxFlushJob` dependency (serial VFX chain). Dispose `vfxPending` after flush.

### `Assets/Scripts/System/Aoe/AoeCollisionSystem.cs`
Add `NativeQueue<VfxPendingSpawn> vfxPending` per frame.

`AoeCollisionJob` additions:
- On each confirmed hit: enqueue `VfxPendingSpawn { TypeId=identity.TypeId, Trigger=1, Position=aoe_center_position }`

`VfxFlushJob` scheduled after collision job, chained into serial VFX dependency. Dispose `vfxPending` after flush.

### `Assets/Scripts/System/Projectile/ProjectileRoot.cs`
`RegisterTemplate`: after building `CombatSpriteRenderResources`, also call:
```csharp
vfxDispatcher.Register(typeId, 0, prefab.SpawnEffect, maxVfxPerFrame);
vfxDispatcher.Register(typeId, 1, prefab.HitEffect, maxVfxPerFrame);
vfxDispatcher.Register(typeId, 2, prefab.ExpireEffect, maxVfxPerFrame);
```
No scope buffer changes needed in `BindWorld`.

`LateUpdate` - after `SubmitProjectiles`, before `DrainHits`:
```csharp
entityManager.CompleteDependencyBeforeRO<ProjectileActiveTag>(); // ensure all VFX jobs complete
vfxDispatcher.Dispatch(); // O(registered resources) GPU calls only
```

`Awake`: `vfxDispatcher = new CombatVfxDispatcher(transform)`.
`OnDestroy`: `vfxDispatcher?.Dispose()`.

### `Assets/Scripts/System/Aoe/AoeRoot.cs`
`RegisterConfig` / `RegisterType`: after `TryBuildRenderResource`, also call:
```csharp
vfxDispatcher.Register(typeId, 0, definition.SpawnEffect, maxVfxPerFrame);
vfxDispatcher.Register(typeId, 1, definition.HitEffect, maxVfxPerFrame);
vfxDispatcher.Register(typeId, 2, definition.ExpireEffect, maxVfxPerFrame);
vfxDispatcher.Register(typeId, 3, definition.PulseEffect, maxVfxPerFrame);
```
No scope buffer changes needed in `BindWorld`.

`LateUpdate` - in `DrainEvents`, after dispatching hit events:
```csharp
vfxDispatcher.Dispatch(); // O(registered resources) GPU calls only
```

`Awake`: `vfxDispatcher = new CombatVfxDispatcher(transform)`.
`OnDestroy`: `vfxDispatcher?.Dispose()`.

---

## Deferred: `AoeLifetimeSystem` (new file)

**Blocker:** Lingering AOEs (`IsPulse==0`) have no lifetime system yet. `AoeLifetimeComponent.RemainingLifetime` is set on spawn but never ticked.

**New file:** `Assets/Scripts/System/Aoe/AoeLifetimeSystem.cs`

```
[UpdateInGroup(SimulationSystemGroup)]
[UpdateAfter(AoeSpawnSystem)]
[UpdateBefore(AoeCollisionSystem)]
```

Responsibilities:
1. **Lifetime countdown** - tick `RemainingLifetime -= dt` on all active AOEs with `RemainingLifetime > 0`. On expire: `AoeActiveTag = false`, enqueue `VfxPendingSpawn{Trigger=2}`, enqueue `AoePendingRecycle`.
2. **Pulse tick** - on entities with `AoePulseVfxComponent`: tick `RemainingInterval -= dt`. When `<= 0`: enqueue `VfxPendingSpawn{Trigger=3}`, reset `RemainingInterval = Interval`.

Two `IJobEntity` jobs, both `ScheduleParallel`, sharing one `NativeQueue<VfxPendingSpawn>` and one `NativeQueue<AoePendingRecycle>`:
- `AoeLifetimeJob`: `[WithAll(AoeTag, AoeActiveTag)]`, reads `AoeLifetimeComponent`, `AoeIdentityComponent`, `CombatKinematicsComponent`.
- `AoePulseVfxJob`: `[WithAll(AoeTag, AoeActiveTag, AoePulseVfxComponent)]`, reads same + `ref AoePulseVfxComponent`.

Flush jobs after both: `VfxFlushJob` + `AoeRecycleFlushJob` scheduled after `CombineDependencies(lifetimeHandle, pulseHandle)`.

**Also required for deferred work:**

`Assets/Scripts/System/Aoe/AoeSpawnSystem.cs` - when materializing a lingering AOE (`request.Lifetime > 0 && IsPulse==0`), add `AoePulseVfxComponent`:
```csharp
entityManager.AddComponentData(entity, new AoePulseVfxComponent
{
    Interval = request.RepeatHitCooldownSeconds,
    RemainingInterval = request.RepeatHitCooldownSeconds
});
```
On reuse (recycle path), reset both fields. Guard: skip if `Interval <= 0`.

---

## VFX Graph Authoring Contract

Each `VisualEffectAsset` assigned to a trigger slot must expose:
- `GraphicsBuffer` property named `"Positions"` - `float2` (x, y world position per spawn event)
- `int` property named `"SpawnCount"`
- Event named `"OnSpawn"`

All three are set/called by `CombatVfxDispatcher.Dispatch`. Effect definition (lifetime, color, size, etc.) lives entirely in the graph - no per-frame CPU authoring needed.

---

## Implementation Order

1. `VfxEcsComponents.cs` + `VfxFlushJob.cs` - foundation, no dependencies
2. `CombatVfxDispatcher.cs` - standalone managed class
3. `BasicAttackPrefab.cs` + `AoeTypeDefinition` - authoring fields (no runtime yet)
4. `ProjectileRoot.cs` + `AoeRoot.cs` - register + drain (gates all downstream events)
5. `ProjectileLifetimeSystem.cs` - Trigger=2 on projectile expire
6. `ProjectileCollisionSystem.cs` - Trigger=1 on projectile hit
7. `AoeCollisionSystem.cs` - Trigger=1 on AOE hit
8. `AoeEcsComponents.cs` (add `AoePulseVfxComponent`) + `AoeSpawnSystem.cs` (materialize it) - lingering AOE prep
9. `AoeLifetimeSystem.cs` - Trigger=2 on AOE expire, Trigger=3 on AOE pulse (deferred - requires lingering AOE lifetime wired)

Steps 1-7 independently shippable. Step 9 depends on 8.

---

## Notes

- `maxVfxPerFrame` constant per root - start at 2048 per (typeId, trigger) pair; tune after profiling. `GraphicsBuffer` size is `maxVfxPerFrame * 8` bytes.
- Trigger=2 (expire) can fire from both `ProjectileLifetimeSystem` and `ProjectileCollisionSystem` (deactivate path). Intentional - each deactivation path fires exactly once per entity per lifetime.
- `AoePulseVfxComponent.Interval <= 0` skipped in `AoeLifetimeSystem` (guard against zero-interval configs).
- Pulse VFX fires at AOE center position (`CombatKinematicsComponent.Position`), not at target position.
- Spawn VFX (Trigger=0) deferred until projectile/AOE spawn systems are touched.

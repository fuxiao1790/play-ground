# Plan: Bucket and Batch Hit Event Dispatch

## Context

Hit events from `ProjectileCollisionSystem` and `AoeCollisionSystem` land in the scope's `CombatHitElement` buffer in collision order — random target interleaving. `CombatHitReplay.ReplayAndClear()` iterates them one-by-one: one dict lookup, one crit roll, one `HitHandler.Invoke`, one `target.ReceiveHit` **per hit**. For N hits to the same mob that is N redundant dict lookups and N virtual dispatches. The request is to group hits by target first, then dispatch per-mob: one event invocation and one `ReceiveHits` call per mob regardless of how many hits it took.

### Bucketing strategy

The multimap `NativeParallelMultiHashMap<CombatHitBucketKey, CombatPendingHit>` is a **set of per-target buffers** — one bucket per `(scope, targetId)`. Each parallel collision worker writes to its target's bucket via `ParallelWriter.Add`; no coordination between workers hitting different targets. The flush job reads each bucket exactly once and writes its hits contiguously to the scope entity buffer → `ReplayAndClear` dispatches O(unique target count) batches.

Capacity is pre-sized from `totalTargetCount`, which both collision systems already compute before scheduling.

`HitEffect` (spawn-on-hit) stays per-hit — each hit carries a distinct world position.

`Hit` event currently has **no external subscribers** — renaming the delegate is safe.

---

## Files to change

| File | Change |
|------|--------|
| `Assets/Scripts/System/Common/CombatHitElement.cs` | Add `CombatHitBucketKey` struct |
| `Assets/Scripts/System/Common/CombatHitFlushJob.cs` | Input: `NativeParallelMultiHashMap<CombatHitBucketKey, CombatPendingHit>`; iterate unique keys → write grouped hits to scope buffers |
| `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs` | Replace `NativeQueue` with `NativeParallelMultiHashMap`; `Enqueue` → `ParallelWriter.Add` with bucket key |
| `Assets/Scripts/System/Aoe/AoeCollisionSystem.cs` | Same |
| `Assets/Scripts/System/Common/ICombatTarget.cs` | New `CombatHitBatchHandler` delegate; `ReceiveHits` default impl |
| `Assets/Scripts/System/Common/CombatTargetSync.cs` | `ICombatHitReplayAdapter` gets `ReplayEffect` + `ReplayBatch`; `ReplayAndClear` scan-while group loop; scratch list |
| `Assets/Scripts/System/Projectile/ProjectileRoot.cs` | `Hit` event type → `CombatHitBatchHandler`; adapter implements new interface |
| `Assets/Scripts/System/Aoe/AoeRoot.cs` | Same |

`ProjectileCollisionJob` / `AoeCollisionJob` stay as `IJobEntity` — `ParallelWriter.Add` is thread-safe across workers. No `IJobChunk` conversion needed. `MobRoot` / `PlayerRoot` require no changes.

---

## Step-by-step

### 1. `CombatHitElement.cs`

Add bucket key after `CombatPendingHit`:
```csharp
public struct CombatHitBucketKey : IEquatable<CombatHitBucketKey>
{
    public Entity Scope;
    public int TargetId;

    public CombatHitBucketKey(Entity scope, int targetId)
    {
        Scope = scope;
        TargetId = targetId;
    }

    public bool Equals(CombatHitBucketKey other) =>
        Scope == other.Scope && TargetId == other.TargetId;

    public override int GetHashCode() =>
        unchecked((Scope.GetHashCode() * 397) ^ TargetId);
}
```

---

### 2. `ProjectileCollisionSystem.cs`

Replace `NativeQueue`:
```csharp
// totalTargetCount already computed — use it for capacity.
// MaxHitsPerTarget is a tunable safety margin; 16 covers dense multi-pierce bursts.
const int MaxHitsPerTarget = 16;
var pendingHits = new NativeParallelMultiHashMap<CombatHitBucketKey, CombatPendingHit>(
    math.max(64, totalTargetCount * MaxHitsPerTarget), Allocator.TempJob);
```

Change job field:
```csharp
// Old:
public NativeQueue<CombatPendingHit>.ParallelWriter PendingHits;
// New:
public NativeParallelMultiHashMap<CombatHitBucketKey, CombatPendingHit>.ParallelWriter PendingHits;
```

Pass writer:
```csharp
var job = new ProjectileCollisionJob { ..., PendingHits = pendingHits.AsParallelWriter() };
```

In `Execute`, replace:
```csharp
PendingHits.Enqueue(new CombatPendingHit { ... });
// →
PendingHits.Add(
    new CombatHitBucketKey(identity.Scope, target.TargetId),
    new CombatPendingHit { ... });
```

Dispose:
```csharp
JobHandle disposeHitsHandle = pendingHits.Dispose(flushHandle);
```

---

### 3. `AoeCollisionSystem.cs`

Identical changes. `totalTargetCount` is computed during the target-cell-capacity pass; reuse it for the map capacity.

---

### 4. `CombatHitFlushJob.cs`

Change input type and replace the dequeue loop with a unique-key walk:

```csharp
public NativeParallelMultiHashMap<CombatHitBucketKey, CombatPendingHit> PendingHits;

public void Execute()
{
    // GetKeyArray returns all keys including duplicates.
    // Walk with a seen-set to visit each unique (scope, targetId) once — O(n), no sort.
    NativeArray<CombatHitBucketKey> allKeys = PendingHits.GetKeyArray(Allocator.Temp);
    var seen = new NativeHashSet<CombatHitBucketKey>(allKeys.Length, Allocator.Temp);

    for (int k = 0; k < allKeys.Length; k++)
    {
        CombatHitBucketKey key = allKeys[k];
        if (!seen.Add(key)) continue;
        if (key.Scope == Entity.Null || !Hits.HasBuffer(key.Scope)) continue;

        if (!PendingHits.TryGetFirstValue(key, out CombatPendingHit pending, out var it)) continue;
        do { WriteHit(key.Scope, pending); }
        while (PendingHits.TryGetNextValue(out pending, ref it));
    }

    seen.Dispose();
    allKeys.Dispose();
}
```

`WriteHit` extracts the existing payload/effect-index logic + `Hits[scope].Add(...)`.

All hits for a given key are written contiguously → `ReplayAndClear`'s scan-while loop fires one batch per unique target.

---

### 5. `ICombatTarget.cs`

Add batch delegate below `CombatHitHandler`:
```csharp
public delegate void CombatHitBatchHandler(ICombatTarget target, IReadOnlyList<CombatHitData> hits);
```

Add `ReceiveHits` default interface impl:
```csharp
void ReceiveHits(IReadOnlyList<CombatHitData> hits)
{
    for (int i = 0; i < hits.Count; i++)
    {
        var h = hits[i];
        ReceiveHit(in h);
    }
}
```

---

### 6. `CombatTargetSync.cs`

**`ICombatHitReplayAdapter<TTarget>`** — replace `Replay` with:
```csharp
void ReplayEffect(
    in CombatHitElement hit,
    in CombatHitEffectElement effect,
    TTarget target,
    in DamageSnapshot damage);

void ReplayBatch(IReadOnlyList<CombatHitData> data, TTarget target);
```

**`CombatHitReplay`** — one scratch list:
```csharp
private static readonly List<CombatHitData> hitDataScratch = new();
```

**`ReplayAndClear`** — scan-while group loop (hits grouped by flush job — one batch per unique target):
```csharp
int i = 0;
while (i < hitCount)
{
    int groupTargetId = hitBuffer[i].TargetId;
    TryGetLiveTarget(targetsById, groupTargetId, out TTarget target);
    hitDataScratch.Clear();

    while (i < hitCount && hitBuffer[i].TargetId == groupTargetId)
    {
        CombatHitElement hit = hitBuffer[i++];
        DamageSnapshot damage = adapter.RollDamage(in hit);
        CombatHitPayloadElement payload = hit.PayloadIndex >= 0
            ? payloadBuffer[hit.PayloadIndex] : default;

        if (hit.EffectIndex >= 0)
        {
            CombatHitEffectElement effect = effectBuffer[hit.EffectIndex];
            adapter.ReplayEffect(in hit, in effect, target, in damage);
        }

        hitDataScratch.Add(new CombatHitData(
            hit.Kind, damage,
            new Vector2(hit.Position.x, hit.Position.y),
            hit.DirectDamageEnabled, payload.StackEffect));
    }

    adapter.ReplayBatch(hitDataScratch, target);
}
```

---

### 7. `ProjectileRoot.cs`

- `public event CombatHitHandler Hit` → `public event CombatHitBatchHandler Hit`
- Replace `Replay` with `ReplayEffect` + `ReplayBatch`:

```csharp
public void ReplayEffect(
    in CombatHitElement hit,
    in CombatHitEffectElement effect,
    TTarget target,
    in DamageSnapshot damage)
{
    var pos = new Vector2(hit.Position.x, hit.Position.y);
    var ctx = new CombatHitContext(hit.Kind, hit.SourceId, hit.TypeId, hit.TargetId,
        pos, damage, target as ICombatTarget, hit.SourceNodeId);
    EffectHandler?.Invoke(in ctx, in effect);
}

public void ReplayBatch(IReadOnlyList<CombatHitData> data, TTarget target)
{
    HitHandler?.Invoke(target as ICombatTarget, data);
    if (CombatHitReplay.IsTargetUsable(target))
        target.ReceiveHits(data);
}
```

---

### 8. `AoeRoot.cs`

Identical changes to `Hit` event type and `AoeHitReplayAdapter`.

---

## Verification

- Existing PlayMode tests for projectile and AOE hit registration should pass — damage totals unchanged since crit rolling and payload application are order-invariant within each target group.
- In-editor: fire a multi-pierce projectile into a mob cluster; confirm each mob takes correct cumulative damage.
- Profile `CombatHitReplay.Events` counter and `CombatHitReplay.Replay` marker at high hit counts to confirm reduced per-mob overhead.

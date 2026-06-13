# Plan: Bucket and Batch Hit Event Dispatch

## Context

Hit events from `ProjectileCollisionSystem` and `AoeCollisionSystem` land in the scope's `CombatHitElement` buffer in collision order — random target interleaving. `CombatHitReplay.ReplayAndClear()` iterates them one-by-one: one dict lookup, one crit roll, one `HitHandler.Invoke`, one `target.ReceiveHit` **per hit**. For N hits to the same mob that is N redundant dict lookups and N virtual dispatches. The request is to sort hits by target first, then dispatch per-mob: one event invocation and one `ReceiveHits` call per mob regardless of how many hits it took.

`HitEffect` (spawn-on-hit) stays per-hit — each hit carries a distinct world position.

`Hit` event currently has **no external subscribers** — renaming the delegate is safe.

---

## Files to change

| File | Change |
|------|--------|
| `Assets/Scripts/System/Common/ICombatTarget.cs` | New `CombatHitBatchHandler` delegate; add `ReceiveHits` default impl to `ICombatTarget` |
| `Assets/Scripts/System/Common/CombatTargetSync.cs` | `ICombatHitReplayAdapter` gets `ReplayEffect` + `ReplayBatch` in place of `Replay`; `CombatHitReplay.ReplayAndClear` sorts + groups; add `TargetIdComparer` and scratch lists |
| `Assets/Scripts/System/Projectile/ProjectileRoot.cs` | `Hit` event type → `CombatHitBatchHandler`; `ProjectileHitReplayAdapter` implements new interface |
| `Assets/Scripts/System/Aoe/AoeRoot.cs` | Same as ProjectileRoot |

`MobRoot` / `PlayerRoot` require **no changes** — the default `ReceiveHits` impl satisfies the interface by looping `ReceiveHit`.

---

## Step-by-step

### 1. `ICombatTarget.cs`

Add batch delegate below `CombatHitHandler`:
```csharp
public delegate void CombatHitBatchHandler(ICombatTarget target, IReadOnlyList<CombatHitContext> hits);
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

`ReceiveHit` stays unchanged — existing `MobRoot`/`PlayerRoot` impls still satisfy the interface.

---

### 2. `CombatTargetSync.cs`

**`ICombatHitReplayAdapter<TTarget>`** — replace `Replay` with:
```csharp
// Called per-hit only when EffectIndex >= 0 (positional spawn-on-hit)
void ReplayEffect(
    in CombatHitElement hit,
    in CombatHitEffectElement effect,
    TTarget target,
    in DamageSnapshot damage);

// Called once per target group after all contexts for that target are built
void ReplayBatch(
    IReadOnlyList<CombatHitContext> contexts,
    IReadOnlyList<CombatHitData> data,
    TTarget target);
```

**`CombatHitReplay`** — add:
```csharp
private static readonly List<CombatHitContext> hitContextScratch = new();
private static readonly List<CombatHitData> hitDataScratch = new();

private struct TargetIdComparer : IComparer<CombatHitElement>
{
    public int Compare(CombatHitElement a, CombatHitElement b)
        => a.TargetId.CompareTo(b.TargetId);
}
```

**`ReplayAndClear`** — replace the per-hit loop:
```csharp
hitBuffer.AsNativeArray().Sort(new TargetIdComparer());

int i = 0;
while (i < hitCount)
{
    int groupTargetId = hitBuffer[i].TargetId;
    TryGetLiveTarget(targetsById, groupTargetId, out TTarget target);
    hitContextScratch.Clear();
    hitDataScratch.Clear();

    while (i < hitCount && hitBuffer[i].TargetId == groupTargetId)
    {
        CombatHitElement hit = hitBuffer[i++];
        DamageSnapshot damage = adapter.RollDamage(in hit);
        CombatHitEffectElement effect = hit.EffectIndex >= 0
            ? effectBuffer[hit.EffectIndex] : default;
        CombatHitPayloadElement payload = hit.PayloadIndex >= 0
            ? payloadBuffer[hit.PayloadIndex] : default;

        if (hit.EffectIndex >= 0)
            adapter.ReplayEffect(in hit, in effect, target, in damage);

        var pos = new Vector2(hit.Position.x, hit.Position.y);
        hitContextScratch.Add(new CombatHitContext(
            hit.Kind, hit.SourceId, hit.TypeId, hit.TargetId,
            pos, damage, target as ICombatTarget, hit.SourceNodeId));
        hitDataScratch.Add(new CombatHitData(
            hit.Kind, damage, pos, hit.DirectDamageEnabled, payload.StackEffect));
    }

    adapter.ReplayBatch(hitContextScratch, hitDataScratch, target);
}
```

---

### 3. `ProjectileRoot.cs`

- `public event CombatHitHandler Hit` → `public event CombatHitBatchHandler Hit`
- `ProjectileHitReplayAdapter<TTarget>` — replace `Replay` with:

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

public void ReplayBatch(
    IReadOnlyList<CombatHitContext> contexts,
    IReadOnlyList<CombatHitData> data,
    TTarget target)
{
    HitHandler?.Invoke(target as ICombatTarget, contexts);
    if (CombatHitReplay.IsTargetUsable(target))
        target.ReceiveHits(data);
}
```

---

### 4. `AoeRoot.cs`

Identical changes to `Hit` event type and `AoeHitReplayAdapter`, using `CombatHitKind.Aoe`.

---

## Verification

- Existing PlayMode tests for projectile and AOE hit registration should pass — damage totals unchanged since crit rolling and payload application are order-preserved within each target group.
- In-editor: fire a multi-pierce projectile into a mob cluster; confirm each mob takes correct cumulative damage.
- Profile `CombatHitReplay.Events` counter and `CombatHitReplay.Replay` marker at high hit counts to confirm reduced per-mob overhead.

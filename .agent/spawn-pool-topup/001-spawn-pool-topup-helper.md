# 001 — Shared SpawnPoolTopUp helper

**New file:** `Assets/Scripts/System/Spawning/SpawnPoolTopUp.cs`
(namespace `PlayGround.System.Combat.Spawning`)

## Change

A single static helper the three lanes call to guarantee enough disabled slots
before their reuse fill:

```csharp
internal static class SpawnPoolTopUp
{
    /// Ensures at least `demand` disabled-Active slots exist in `archetype`.
    /// Returns the number of slots created (the deficit). MUST be called before
    /// the caller fetches chunk arrays / component type-handles for the fill,
    /// because CreateEntity is a structural change that invalidates them.
    public static int EnsureDisabledSlots(
        EntityManager em,
        EntityArchetype archetype,
        EntityQuery disabledSlotQuery,
        int demand)
    {
        int have = disabledSlotQuery.CalculateEntityCount(); // enableable-aware
        int deficit = demand - have;
        if (deficit <= 0)
        {
            return 0;
        }

        NativeArray<Entity> created =
            em.CreateEntity(archetype, deficit, Allocator.Temp); // 1 structural change
        for (int i = 0; i < created.Length; i++)
        {
            // Fresh entities default to Active ENABLED; make them pool slots so
            // the reuse fill discovers and claims them. Only Active gates
            // discovery; other gate bits are overwritten by the fill.
            em.SetComponentEnabled<Active>(created[i], false);
        }
        created.Dispose();
        return deficit;
    }
}
```

Notes:
- `CalculateEntityCount()` (not `...WithoutFiltering`) so disabled-count reflects
  enable state.
- `Allocator.Temp` array is fine — used and disposed on the main thread within
  the call, not passed to a job.
- The disable loop is non-structural (enable-bit flips), bounded by `deficit`,
  and only runs during pool growth.

## Counter convention

Each lane already exposes `*.Reuse` / `*.Cold` `ProfilerCounterValue`s. After
the top-up refactor:
- `*.Reuse` == command count (always fully reused).
- `*.Cold` → repurpose/rename to `*.TopUp`, set to the deficit returned here
  (slots newly created this tick). Keeps a signal for "pool grew" and preserves
  the `Reuse + created == command count` reading. Update the counter names in
  each lane subtask; note the rename in [profiling.md](../../Docs/profiling.md)
  "Spawn Apply Counters".

## Acceptance criteria

- Compiles; no job references (pure main-thread helper).
- Returns 0 when `have >= demand` (no `CreateEntity`, no structural change).
- Returns `demand - have` and creates exactly that many `Active`-disabled
  entities otherwise.

## Dependencies

None. Prerequisite for 002–004.

## Scope

Small — one new ~30-line file.

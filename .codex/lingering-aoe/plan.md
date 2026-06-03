# Lingering AOE Implementation Plan

## Context

AOE collision system currently supports only pulse AOEs. Lingering AOE data structs already exist
(`AoeLifetimeComponent`, `AoeHitGateComponent`, `AoeContactGateElement`) but are unused.

Design constraints:

- Per-target tick cooldown lives on the AOE as `AoeContactGateElement` entries — not on the target —
  because a target cannot know the tick rate of each AOE hitting it.
- Each entry tracks its own `CooldownRemaining` independently — different entry times mean different
  offsets.
- Cooldown continues ticking after a target exits; re-entry checks the gate and skips if still active.
  No special exit handling. Mirrors `ProjectileContactGateSystem` exactly.

Already done:

- `AoeContactGateElement.TouchedThisStep` removed — was written but never read.
  Struct is now `{ int TargetId; float CooldownRemaining; }`.

## Files To Change

### 1. `Assets/Scripts/System/Aoe/AoeSpawnSystem.cs`

Fix `IsPulse` initialization in `LifetimeFor` (line ~284, currently hardcoded to `1`):

```csharp
IsPulse = request.Lifetime <= 0f ? 1 : 0
```

### 2. `Assets/Scripts/System/Aoe/AoeSimulationSystem.cs`

Add lifetime tick. System already runs `OrderFirst = true` — already before spawn and collision.

New parallel job `AoeLifetimeTickJob`:

- `[WithAll(typeof(AoeTag), typeof(AoeActiveTag))]`
- Skip if `AoeLifetimeComponent.IsPulse == 1`
- `RemainingLifetime -= deltaTime`
- When `<= 0`: disable `AoeActiveTag` + `CombatRenderActiveTag`, enqueue `AoePendingRecycle`

New serial flush job after the tick job:

- Same `AoeRecycleFlushJob` pattern as in `AoeCollisionSystem` — drains queue into scope
  `AoeRecycleElement` buffers. Duplicate minimally; do not share the collision system's job instance.

Keep existing hit buffer clear (unchanged).

### 3. New `Assets/Scripts/System/Aoe/AoeContactGateSystem.cs`

Mirrors `ProjectileContactGateSystem` exactly.
Order: `[UpdateAfter(AoeSpawnSystem)][UpdateBefore(AoeCollisionSystem)]`.

```csharp
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AoeSpawnSystem))]
[UpdateBefore(typeof(AoeCollisionSystem))]
public partial struct AoeContactGateSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var job = new AoeContactGateJob { DeltaTime = SystemAPI.Time.DeltaTime };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(AoeTag), typeof(AoeActiveTag))]
    private partial struct AoeContactGateJob : IJobEntity
    {
        public float DeltaTime;

        private void Execute(DynamicBuffer<AoeContactGateElement> contactGates)
        {
            // Pass 1: subtract all (no branches — Burst can vectorize).
            for (int i = 0; i < contactGates.Length; i++)
                contactGates.ElementAt(i).CooldownRemaining -= DeltaTime;

            // Pass 2: compact expired.
            for (int i = contactGates.Length - 1; i >= 0; i--)
                if (contactGates[i].CooldownRemaining <= 0f)
                    contactGates.RemoveAt(i);
        }
    }
}
```

### 4. `Assets/Scripts/System/Aoe/AoeCollisionSystem.cs`

Add `AoeLifetimeComponent` and `AoeHitGateComponent` as `ReadOnly` params to `Execute`.

Branch on `AoeLifetimeComponent.IsPulse`:

**Pulse path** — unchanged (hits once, calls `Deactivate`).

**Lingering path**:

```
For each overlapping target:
  a. Find gate entry by TargetId (IndexOfGate helper already exists)
  b. No entry (first contact OR cooldown expired):
     → emit hit, add { TargetId, CooldownRemaining = hitGate.RepeatHitCooldownSeconds }
  c. Entry found (still in cooldown): skip
// Gate system owns all removal — never remove gates on target exit
// Do NOT call Deactivate — lifetime system owns that
```

## Memory Cost

Contact gate requires O(m × n) state where m = targets, n = AOEs. Unavoidable: one side must
track per-pair tick offset. AOE-side is the right choice — AOE count is budget-capped, not unbounded.

Practical bound: gate entries only exist for currently-overlapping pairs. Peak allocation ≈
`aoe_budget × max_targets_per_aoe`, not `aoe_cap × mob_cap`. Document both as the memory ceiling.

`DynamicBuffer` handles sparse allocation per entity. No pre-sizing needed unless profiling shows
fragmentation.

## Reused Patterns

- `AoePendingRecycle` / recycle flush: already in `AoeCollisionSystem` — duplicate minimally into
  `AoeSimulationSystem`
- `AoeContactGateSystem` mirrors `ProjectileContactGateSystem` — same structure, same two-pass job
- `IndexOfGate` helper already in `AoeCollisionSystem` — reuse as-is for lingering path
- `SpatialHashIntersects` early-out already guards both paths

## Verification

1. Set `lifetimeSeconds > 0` and `tickIntervalSeconds > 0` on an `AoeConfig` asset
2. Place a mob in the AOE area — confirm hit fires immediately on overlap
3. Mob stays in AOE — confirm repeat hit fires after tick interval, not every frame
4. Mob exits and re-enters before cooldown expires — confirm no hit on re-entry; hit fires only after
   cooldown expires
5. Let AOE expire — confirm it despawns (recycle event fires, entity goes inactive)
6. Pulse AOE (`lifetimeSeconds = 0`) — confirm unchanged: hits once, despawns immediately
7. Port PlayMode tests from `AoePlayModeTests.cs` for lingering cases listed in
   `Docs/aoe-system.md` § Tests To Port

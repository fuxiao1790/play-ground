# 001 — Single source of truth for AOE spawn activation state

## Goal
Introduce `AoeSpawnApplyUtility.SpawnState` + `SpawnStateFor(cmd, isLingering)` and route all four
materialization sites through it. Behavior-preserving: the applied enable-states must be identical
to today for every archetype × path.

## The one decision (must reproduce exactly)
```csharp
internal readonly struct SpawnState
{
    public readonly bool Active;
    public readonly bool Collision;
    public readonly bool Render;
    public readonly bool Timed;
    public SpawnState(bool active, bool collision, bool render, bool timed)
    { Active = active; Collision = collision; Render = render; Timed = timed; }
}

public static SpawnState SpawnStateFor(in AoeSpawnCommand cmd, bool isLingering)
{
    bool collision = NeedsCollision(cmd);
    return isLingering
        ? new SpawnState(active: true, collision: collision, render: true, timed: HasTimedSpawner(cmd))
        : new SpawnState(active: collision, collision: collision, render: collision, timed: false);
}
```
Place next to `NeedsCollision`/`HasTimedSpawner` in `AoeSpawnApplyUtility`
([AoeSpawnApplySystem.cs:541](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L541)).

## Apply at the four sites

1. **Impact reuse** ([:210-213](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L210)):
   ```csharp
   var state = AoeSpawnApplyUtility.SpawnStateFor(cfg, isLingering: false);
   activeMask[i]          = state.Active;
   collisionActiveMask[i] = state.Collision;
   renderActiveMask[i]    = state.Render;
   ```

2. **Lingering reuse** ([:444-454](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L444)):
   compute `var state = SpawnStateFor(cfg, isLingering: true);` and use `state.Timed` for the timed
   **data** writes (`timedSpawns[i]`, `timedSpawnStates[i]`, `timedSpawnMask[i]`) — identical to
   today's `hasTimedSpawner` — and `state.Active/Collision/Render` for the three masks.

3. **`RecordImpactReset`** ([:481-484](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L481)):
   ```csharp
   var state = SpawnStateFor(cmd, isLingering: false);
   ecb.SetComponentEnabled<Active>(entity, state.Active);
   ecb.SetComponentEnabled<AoeCollisionActiveTag>(entity, state.Collision);
   ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, state.Render);
   ```

4. **`RecordLingeringReset`** ([:502-512](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L502)):
   `var state = SpawnStateFor(cmd, isLingering: true);` — use `state.Timed` for the `TimedSpawnComponent`
   data + enable, and `state.Active/Collision/Render` for the three enable calls.

## Constraints
- `SpawnState` blittable bools; `SpawnStateFor` static/pure (Burst-safe — used inside the reuse `IJob`s).
- Do not touch data-component writes other than gating the timed ones on `state.Timed` (== today's
  `hasTimedSpawner`).
- Do not change archetypes, queries, systems, or ordering.

## Acceptance criteria
- Compiles (user-side; Burst accepts `SpawnState`/`SpawnStateFor` in the reuse jobs).
- Grep: the four sites no longer compute enable bits independently — each reads a `SpawnStateFor` result.
- **User runs `AoeSimulationTests` (PlayMode): all green**, unchanged — impact one-shot, lingering
  repeat+expire, visual-only impact born inert, pool reuse both archetypes.

## Scope / complexity
Low. One struct + one function + four call-site rewrites. No behavior change.

## Dependencies
None. Pairs with 002 (regression guard).

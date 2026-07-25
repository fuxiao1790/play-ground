# 007 — ECS resource regen system

## Goal
ECS owns the regen tick: each unit's resource `Current` climbs toward `Max` at its
`RegenPerSecond`, clamped, deterministic, and bounded.

## Design
A single `ResourceRegenSystem : ISystem` in `SimulationSystemGroup`, ordered so it
does not race the hit-apply write to `Health.Current`. Apply damage/spend first,
then regen, so a unit that takes a hit and regens in the same frame nets correctly.
Suggested: `[UpdateAfter(typeof(CombatApplyFinalizeSingleSystem))]` (confirm the
finalize system's group/placement when implementing).

Two tiny queries (one per resource component), or one job per component:
```csharp
foreach (var h in SystemAPI.Query<RefRW<Health>>())
    h.ValueRW.Current = math.min(h.ValueRO.Max,
        h.ValueRO.Current + h.ValueRO.RegenPerSecond * dt);
// same for Mana
```
- `RegenPerSecond <= 0` is a no-op (health defaults to 0 → no regen unless authored).
- Do not regen past `Max`; do not resurrect a depleted unit if depletion means
  death — regen should only apply while alive. Gate on the same aliveness signal
  the apply path uses (e.g. skip when `Current <= 0` for health, or exclude dead
  proxies). Confirm the death/despawn ordering so regen can't revive a corpse.

## Acceptance Criteria
- Mana `Current` rises by `RegenPerSecond * dt` per frame up to `Max`, verified in
  a PlayMode/ECS test.
- Health with `RegenPerSecond = 0` never changes from regen.
- Regen runs after damage apply in the same frame (net = -damage + regen), verified
  by a test.
- Depleted/dead units are not regenerated back to life.

## Dependencies
Depends on 004 (`RegenPerSecond` field on the neutral components).

## Scope
Small–medium (one system + ordering + tests).

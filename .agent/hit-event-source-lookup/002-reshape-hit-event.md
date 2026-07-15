# 002 — Reshape CombatHitEvent to {Source, Target}

## Goal
Shrink the per-hit event to two entity references; drop every field now reachable
from the source entity.

## Change
[CombatHitEvent.cs](../../Assets/Scripts/System/Application/CombatHitEvent.cs):

```csharp
public struct CombatHitEvent
{
    public Entity Source;   // projectile / AOE entity that produced the hit
    public Entity Target;   // target proxy entity that was hit
}
```

Remove: `TargetProxy` (renamed to `Target`), `Kind`, `DamageAmount`, `CritChance`,
`CritMultiplier`, `DirectDamageEnabled`, `HitPosition`, `SourceNodeId`, `SourceId`,
`TypeId`, `StackEffect`.

## Rationale
All removed fields are reachable from `Source`:
- damage/crit/stack/`SourceNodeId` → `CombatHitPayload` component (001)
- projectile-vs-AOE (`Kind`) → which archetype `Source` is (not needed: same
  payload component on both)
- `SourceId`/`TypeId` → `*IdentityComponent`; `HitPosition` → `CombatKinematicsComponent`

Finalize (004) is the only consumer; it used only target + damage + stack, all
still available.

## Acceptance
- `CombatHitEvent` has exactly `Source` and `Target`.
- Rename `TargetProxy`→`Target` propagated to producers (003) and finalize (004).

## Dependencies
Pairs with 003 (producers) and 004 (finalize). Scope: trivial.

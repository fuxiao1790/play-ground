# 001 — Continuous Archetype Types

**Depends on:** none
**Scope:** small (new declarations only, no behavior change)

## Goal

Declare the two types the continuous lane is built from, plus one constant.

Continuous collision is **authored**, so there is no routing predicate and no threshold math. The swept box
always spans the full previous→current segment, so there is no distance cap either. There is
no config singleton in this feature.

## Changes

### `Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs`

```csharp
// ECS Lifecycle: continuous projectile archetype discriminator; added at entity creation by
// ProjectileContinuousSpawnApplySystem; never added or removed at runtime; present only on the
// continuous archetype and absent from the discrete one. Mutually exclusive with
// ProjectileTrackingComponent, which the continuous archetype does not carry at all.
public struct ProjectileContinuousTag : IComponentData
{
}

// ECS Lifecycle: swept-archetype-only component; added at entity creation; seeded to the
// spawn position on reuse and overwritten every frame by ProjectileContinuousOriginSystem
// before movement integrates; kept until root teardown.
public struct ProjectileContinuousStepComponent : IComponentData
{
    // World position the projectile occupied at the start of the current frame's step.
    // The swept collision test sweeps from here to CombatKinematicsComponent.Position.
    // Because continuous projectiles never track, this segment is the exact path travelled,
    // not an approximation of a curve.
    public float2 Origin;
}
```

`ProjectileContinuousTag` is a plain (non-enableable) tag — it is the archetype discriminator,
exactly like `LingeringAoeTag`. Do **not** make it `IEnableableComponent`.

### `Assets/Scripts/System/Api/Collision/CollisionConstants.cs`

```csharp
// Max time-of-impact candidates a continuous projectile resolves in one frame. Mirrors
// MaxProjectileGateCapacity: a safety bound on the per-entity stack array, not a
// gameplay knob. When the cap binds, the nearest candidates are kept.
public const int MaxContinuousHitsPerFrame = 16;
```

### No sweep config singleton

An earlier draft had a `ProjectileSweepConfig` holding `MaxSweepDistance`, which clamped the
tested segment during a frame hitch. **Dropped** — the swept box always covers the full
previous→current span.

Clamping would have reintroduced tunneling in exactly the situation the feature exists to
prevent: a hitch produces the longest step, which is when a projectile is *most* likely to
skip a target, and that is precisely when the clamp would have stopped testing. A collision
feature whose coverage degrades under load is not a collision feature.

The cost concern the cap addressed is real but belongs to the broadphase, not to coverage.
The remedy is the DDA segment walk (`O(n)` cells instead of the AABB rect-walk's `O(n²)`) —
see task 007. Per `Docs/coding-standards.md` (Performance Budget Rule) the feature still has
a bounded fallback: `MaxContinuousHitsPerFrame` caps per-entity work without dropping any part of
the continuous path.

## Acceptance Criteria

- Both components declared with `ECS Lifecycle:` comments per `Docs/coding-standards.md`.
- `ProjectileContinuousTag` is **not** `IEnableableComponent`.
- No config singleton is introduced; no distance clamp exists anywhere in the continuous path.
- Project compiles; nothing references the new types yet; no behavior change.

## Reference — geometry inputs for the compile-time check

These feed the tunneling warning in [003](003-authoring-flag-and-exclusivity.md) and the
authoring guidance in the docs. **None of it is reachable from simulation code** — no ECS
system computes a threshold or reads a target radius.

Measured from actual assets:

| Quantity | Value | Source |
|---|---|---|
| Standard projectile collider | `BoxCollider2D` `0.1 × 0.15` → bounding radius `0.090` | `Prefabs/Skills/Projectile/MagicBolt.prefab:209` (12 of 14 projectile prefabs) |
| Star projectile collider | `CircleCollider2D` `m_Radius: 0.07` | `Star.prefab:209` |
| Smallest target | Bat, `m_Radius: 0.35` | `Prefabs/Mobs/Bat.prefab:193` |

The fastest authored projectile today is `MagicBolt` at `speed: 30`, which does cross the
tunneling threshold at 30 fps. **Existing skills are not retrofitted** — that is a known,
accepted, authored state. Sweep will be authored deliberately on new content.

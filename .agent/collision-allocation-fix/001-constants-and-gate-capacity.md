# Task 01 — Hard-coded constant + gate buffer capacity

**Depends on:** none. **Blocks:** 02, 03.

## Goal

Add the hard-coded AOE hit cap and set explicit `[InternalBufferCapacity]` on both
contact-gate buffers so gate growth stops allocating within the cap.

## Changes

### 1. New constant file

Create `Assets/Scripts/System/Common/CollisionConstants.cs`:

```csharp
namespace PlayGround.System.Common
{
    public static class CollisionConstants
    {
        // Hard-coded per-tick hit cap for AOE collision. Also the in-chunk
        // capacity of AoeContactGateElement, so AOE gate growth never allocates
        // within a tick. 32 is far above any real AOE overlap; the clamp is a
        // safety bound, not a gameplay knob. There is intentionally no per-skill
        // override and no config entity.
        public const int MaxAoeTargetsPerTick = 32;

        // In-chunk capacity for ProjectileContactGateElement. Pierce counts are
        // authored small; a projectile that pierces more distinct targets than
        // this over its lifetime pays a one-time heap growth (accepted).
        public const int MaxProjectileGateCapacity = 16;
    }
}
```

### 2. AOE gate buffer capacity

In [Assets/Scripts/System/Aoe/AoeEcsComponents.cs](../../Assets/Scripts/System/Aoe/AoeEcsComponents.cs)
on `AoeContactGateElement` (line 46):

```csharp
[InternalBufferCapacity(CollisionConstants.MaxAoeTargetsPerTick)] // = 32
public struct AoeContactGateElement : IBufferElementData
```

Add `using PlayGround.System.Common;` if not present. Element is 8 bytes
(`int` + `float`); 32 entries = 256 bytes in-chunk (was the default 16 / 128 B).

### 3. Projectile gate buffer capacity

In [Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs](../../Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs)
on `ProjectileContactGateElement` (line 50):

```csharp
[InternalBufferCapacity(CollisionConstants.MaxProjectileGateCapacity)] // = 16
public struct ProjectileContactGateElement : IBufferElementData
```

Make the capacity explicit (was relying on the default 16) so it is intentional.

## Acceptance

- Project compiles.
- No behavior change yet (projectile capacity unchanged at 16; AOE 16→32 only
  enlarges in-chunk room).

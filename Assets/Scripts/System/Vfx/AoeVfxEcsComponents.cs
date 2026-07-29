using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Vfx
{
    public struct AoeVfxIds : IComponentData
    {
        public int SpawnId;
        public int HitId;
        public int ExpireId;
        public int PulseId;
        public int ArmingId;
    }

    // ECS Lifecycle: authored per-instance VFX timing; added to AOE entities at
    // creation, reset on reuse, and consumed only by TimedCircular VFX emits.
    public struct VfxTimingData : IComponentData
    {
        public float Duration;
        public float TickInterval;
    }
}

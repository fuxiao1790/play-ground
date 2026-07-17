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

    // ECS Lifecycle: transient native payload; not added to entities; queued by
    // simulation jobs into the shared NativeQueue<AoeVfxSpawnRequest> owned by
    // CombatAoeVfxDispatchSystem, drained in presentation.
    public struct AoeVfxSpawnRequest
    {
        public int VfxId;
        public float2 Position;
        public float AreaSize;
    }
}

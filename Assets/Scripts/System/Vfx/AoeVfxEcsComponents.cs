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
using Unity.Mathematics;

namespace PlayGround.System.Combat.Vfx
{
    public enum AoeVfxTrigger : byte
    {
        Spawn = 0,
        Hit = 1,
        Expire = 2,
        Pulse = 3,
        Arming = 4
    }

    // ECS Lifecycle: transient native payload; not added to entities; queued by
    // simulation jobs into the shared NativeQueue<AoeVfxSpawnRequest> owned by
    // CombatAoeVfxDispatchSystem, drained in presentation.
    public struct AoeVfxSpawnRequest
    {
        public int TypeId;
        public AoeVfxTrigger Trigger;
        public float2 Position;
        public float AreaSize;
    }
}

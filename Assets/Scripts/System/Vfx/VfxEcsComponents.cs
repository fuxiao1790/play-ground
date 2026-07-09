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
    // ECS Lifecycle: transient native payload; not added to entities; queued by
    // simulation jobs into the shared NativeQueue<VfxPendingSpawn> owned by
    // CombatVfxDispatchSystem, drained in presentation.
    public struct VfxPendingSpawn
    {
        public int TypeId;
        public byte Trigger;   // 0=spawn 1=hit 2=expire 3=pulse 4=arming
        public float2 Position;
        public float AreaSize;
    }
}

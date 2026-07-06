using Unity.Mathematics;

namespace PlayGround.System.Vfx
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

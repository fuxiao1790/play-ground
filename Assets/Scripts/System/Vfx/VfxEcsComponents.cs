using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Vfx
{
    // ECS Lifecycle: transient native payload; not added to entities; queued by simulation jobs, drained by VfxFlushJob.
    public struct VfxPendingSpawn
    {
        public Entity Scope;
        public int TypeId;
        public byte Trigger;   // 0=spawn 1=hit 2=expire 3=pulse
        public float2 Position;
    }

    // ECS Lifecycle: scope buffer; added at root setup; kept until root teardown; drained by roots in LateUpdate before VFX dispatch.
    public struct VfxSpawnRequestElement : IBufferElementData
    {
        public int TypeId;
        public int Trigger;    // 0=spawn 1=hit 2=expire 3=pulse
        public float2 Position;
    }
}

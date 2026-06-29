using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Vfx
{
    // ECS Lifecycle: VFX staging singleton tag; created by CombatVfxDispatchSystem.OnCreate
    // (or by test setup); hosts the DynamicBuffer<VfxSpawnRequestElement> drained each frame.
    public struct VfxSingleton : IComponentData
    {
    }

    // ECS Lifecycle: transient native payload; not added to entities; queued or streamed by simulation jobs, drained by VfxFlushJob variants.
    public struct VfxPendingSpawn
    {
        public int TypeId;
        public byte Trigger;   // 0=spawn 1=hit 2=expire 3=pulse
        public float2 Position;
        public float AreaSize;
    }

    // ECS Lifecycle: VFX staging buffer; owned by the VfxSingleton entity; drained by CombatVfxDispatchSystem in PresentationSystemGroup.
    public struct VfxSpawnRequestElement : IBufferElementData
    {
        public int TypeId;
        public int Trigger;    // 0=spawn 1=hit 2=expire 3=pulse
        public float2 Position;
        public float AreaSize;
    }
}

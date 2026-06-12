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
        public float AreaSize;
    }

    // ECS Lifecycle: scope buffer; added at root setup; kept until root teardown; drained by CombatVfxDispatchSystem in PresentationSystemGroup.
    public struct VfxSpawnRequestElement : IBufferElementData
    {
        public int TypeId;
        public int Trigger;    // 0=spawn 1=hit 2=expire 3=pulse
        public float2 Position;
        public float AreaSize;
    }

    // ECS Lifecycle: unmanaged component; added to scope entities via CombatVfxRoot.Bind; stores only an opaque int key used by CombatVfxDispatchSystem to look up the owning CombatVfxRoot from its static registry.
    internal struct CombatScopeVfxCatalog : IComponentData
    {
        public int VfxRootId;
    }
}

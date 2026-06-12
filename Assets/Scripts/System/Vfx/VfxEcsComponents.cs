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

    // ECS Lifecycle: managed component; added to scope entities at root setup; references CombatVfxRoot so the dispatch system can drain and forward GPU work without MonoBehaviour callbacks.
    internal sealed class CombatScopeVfxCatalog : IComponentData
    {
        public CombatVfxRoot VfxRoot;
    }
}

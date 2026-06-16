using PlayGround.System.Common;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Vfx
{
    // ECS Lifecycle: transient native payload; not added to entities; queued or streamed by simulation jobs, drained by VfxFlushJob variants.
    public struct VfxPendingSpawn
    {
        public CombatFaction Faction;
        public int TypeId;
        public byte Trigger;   // 0=spawn 1=hit 2=expire 3=pulse
        public float2 Position;
        public float AreaSize;
    }

    // ECS Lifecycle: scope buffer; added at root setup; kept until root teardown; drained by CombatVfxDispatchSystem in PresentationSystemGroup.
    public struct VfxSpawnRequestElement : IBufferElementData
    {
        public CombatFaction Faction;
        public int TypeId;
        public int Trigger;    // 0=spawn 1=hit 2=expire 3=pulse
        public float2 Position;
        public float AreaSize;
    }
}

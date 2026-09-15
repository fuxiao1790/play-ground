using AgentVFX.Editor;
using Unity.Pipeline.Commands;

namespace AgentVFX.Editor
{
    // Read-only CLI surface. VFX authoring must remain in Unity editor until a
    // separately reviewed mutation API exists.
    public static class AgentVfxCommands
    {
        [CliCommand("vfx_ping", "Verify the agent can reach the VFX Graph editor model.", MainThreadRequired = true)]
        public static string Ping() => AgentVfxApi.Ping();

        [CliCommand("vfx_graph_read", "Read a VFX Graph's full node/connection topology.", MainThreadRequired = true)]
        public static AgentVfxGraphDto GraphRead(string assetPath) => AgentVfxApi.ReadGraph(assetPath);

        [CliCommand("vfx_types_list", "List every context/block/operator type Unity's VFX Graph currently exposes.", MainThreadRequired = true)]
        public static AgentVfxTypeDto[] TypesList() => AgentVfxApi.ListNodeTypes();

        [CliCommand("vfx_type_describe", "Describe a node type's settings/inputs/outputs by id from vfx_types_list.", MainThreadRequired = true)]
        public static AgentVfxTypeDetailDto TypeDescribe(string typeId) => AgentVfxApi.DescribeNodeType(typeId);

        [CliCommand("vfx_slot_read", "Read one slot's name/type/value/link state.", MainThreadRequired = true)]
        public static AgentVfxSlotDto SlotRead(string slotId) => AgentVfxApi.ReadSlot(slotId);

        [CliCommand("vfx_blackboard_read", "List a graph's exposed blackboard properties.", MainThreadRequired = true)]
        public static AgentVfxBlackboardPropertyDto[] BlackboardRead(string assetPath) => AgentVfxApi.ReadBlackboard(assetPath);

    }
}

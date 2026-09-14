using AgentVFX.Editor;
using Unity.Pipeline.Commands;

namespace AgentVFX.Editor
{
    // Unverified against real Unity.Pipeline source (guide §10, §11) --
    // com.unity.pipeline is not installed in this project as of writing
    // (.agent/vfx-graph-agent-bridge/006-cli-commands.md). Isolated into its own
    // assembly precisely so a compile failure here (missing package, wrong
    // [CliCommand] signature/namespace) cannot break AgentVFX.Editor, which is
    // independently useful and already compile-clean.
    //
    // Once com.unity.pipeline is installed: `unity command` should list every
    // command below; fix whatever the compiler flags in this file only.
    public static class AgentVfxCommands
    {
        [CliCommand("vfx_ping", "Verify the agent can reach the VFX Graph editor model.", MainThreadRequired = true)]
        public static string Ping() => AgentVfxApi.Ping();

        [CliCommand("vfx_graph_create", "Create a new, empty VFX Graph asset.", MainThreadRequired = true)]
        public static string GraphCreate(string assetPath) => AgentVfxApi.CreateGraph(assetPath);

        [CliCommand("vfx_graph_read", "Read a VFX Graph's full node/connection topology.", MainThreadRequired = true)]
        public static AgentVfxGraphDto GraphRead(string assetPath) => AgentVfxApi.ReadGraph(assetPath);

        [CliCommand("vfx_graph_save", "Save a VFX Graph and let Unity serialize the .vfx asset.", MainThreadRequired = true)]
        public static string GraphSave(string assetPath) => AgentVfxApi.SaveGraph(assetPath);

        [CliCommand("vfx_types_list", "List every context/block/operator type Unity's VFX Graph currently exposes.", MainThreadRequired = true)]
        public static AgentVfxTypeDto[] TypesList() => AgentVfxApi.ListNodeTypes();

        [CliCommand("vfx_type_describe", "Describe a node type's settings/inputs/outputs by id from vfx_types_list.", MainThreadRequired = true)]
        public static AgentVfxTypeDetailDto TypeDescribe(string typeId) => AgentVfxApi.DescribeNodeType(typeId);

        [CliCommand("vfx_node_create", "Create a node of the given type inside a graph or context.", MainThreadRequired = true)]
        public static string NodeCreate(string assetPath, string typeId, string parentId, float x, float y) =>
            AgentVfxApi.CreateNode(assetPath, typeId, parentId, x, y);

        [CliCommand("vfx_node_delete", "Delete a node by id.", MainThreadRequired = true)]
        public static void NodeDelete(string nodeId) => AgentVfxApi.DeleteNode(nodeId);

        [CliCommand("vfx_node_move", "Move a node's canvas position.", MainThreadRequired = true)]
        public static void NodeMove(string nodeId, float x, float y) => AgentVfxApi.MoveNode(nodeId, x, y);

        [CliCommand("vfx_node_configure", "Set a node setting (not a linkable slot) by name.", MainThreadRequired = true)]
        public static void NodeConfigure(string nodeId, string settingName, string valueJson) =>
            AgentVfxApi.ConfigureNode(nodeId, settingName, valueJson);

        [CliCommand("vfx_slot_read", "Read one slot's name/type/value/link state.", MainThreadRequired = true)]
        public static AgentVfxSlotDto SlotRead(string slotId) => AgentVfxApi.ReadSlot(slotId);

        [CliCommand("vfx_slot_set", "Set a slot's direct value.", MainThreadRequired = true)]
        public static void SlotSet(string slotId, string valueJson) => AgentVfxApi.SetSlotValue(slotId, valueJson);

        [CliCommand("vfx_slot_connect", "Link an output slot to an input slot.", MainThreadRequired = true)]
        public static AgentVfxConnectResultDto SlotConnect(string fromSlotId, string toSlotId) =>
            AgentVfxApi.ConnectSlots(fromSlotId, toSlotId);

        [CliCommand("vfx_slot_disconnect", "Unlink two connected slots.", MainThreadRequired = true)]
        public static void SlotDisconnect(string fromSlotId, string toSlotId) =>
            AgentVfxApi.DisconnectSlots(fromSlotId, toSlotId);

        [CliCommand("vfx_blackboard_read", "List a graph's exposed blackboard properties.", MainThreadRequired = true)]
        public static AgentVfxBlackboardPropertyDto[] BlackboardRead(string assetPath) => AgentVfxApi.ReadBlackboard(assetPath);

        [CliCommand("vfx_blackboard_add", "Add an exposed blackboard property.", MainThreadRequired = true)]
        public static string BlackboardAdd(string assetPath, string name, string typeName) =>
            AgentVfxApi.AddBlackboardProperty(assetPath, name, typeName);

        [CliCommand("vfx_blackboard_remove", "Remove a blackboard property by id.", MainThreadRequired = true)]
        public static void BlackboardRemove(string propertyId) => AgentVfxApi.RemoveBlackboardProperty(propertyId);

        [CliCommand("vfx_compile", "Compile the graph and return success + structured diagnostics.", MainThreadRequired = true)]
        public static AgentVfxCompileResultDto Compile(string assetPath) => AgentVfxApi.Compile(assetPath);

        [CliCommand("vfx_errors", "Read the graph's current diagnostics without forcing a recompile.", MainThreadRequired = true)]
        public static AgentVfxCompileResultDto Errors(string assetPath) => AgentVfxApi.GetErrors(assetPath);
    }
}

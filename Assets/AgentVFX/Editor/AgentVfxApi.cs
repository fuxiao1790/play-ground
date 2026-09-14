using AgentVFX.Internal;
using UnityEngine;

namespace AgentVFX.Editor
{
    // Stable, DTO-only agent-facing API. This is what the (future) [CliCommand]
    // layer in Assets/AgentVFX/Commands/ calls -- it never touches
    // AgentVfxInternalBridge or UnityEditor.VFX directly.
    public static class AgentVfxApi
    {
        public static string Ping() => AgentVfxInternalBridge.Ping();

        public static string CreateGraph(string assetPath) => AgentVfxInternalBridge.CreateGraph(assetPath);

        public static string SaveGraph(string assetPath) => AgentVfxInternalBridge.SaveGraph(assetPath);

        public static AgentVfxGraphDto ReadGraph(string assetPath) =>
            JsonUtility.FromJson<AgentVfxGraphDto>(AgentVfxInternalBridge.ReadGraph(assetPath));

        public static AgentVfxTypeDto[] ListNodeTypes() =>
            JsonUtility.FromJson<AgentVfxTypeListDto>(AgentVfxInternalBridge.ListNodeTypes()).types;

        public static AgentVfxTypeDetailDto DescribeNodeType(string typeId) =>
            JsonUtility.FromJson<AgentVfxTypeDetailDto>(AgentVfxInternalBridge.DescribeNodeType(typeId));

        public static string CreateNode(string assetPath, string typeId, string parentId, float x, float y) =>
            AgentVfxInternalBridge.CreateNode(assetPath, typeId, parentId, x, y);

        public static void DeleteNode(string nodeId) => AgentVfxInternalBridge.DeleteNode(nodeId);

        public static void MoveNode(string nodeId, float x, float y) => AgentVfxInternalBridge.MoveNode(nodeId, x, y);

        public static void ConfigureNode(string nodeId, string settingName, string valueJson) =>
            AgentVfxInternalBridge.ConfigureNode(nodeId, settingName, valueJson);

        public static AgentVfxSlotDto ReadSlot(string slotId) =>
            JsonUtility.FromJson<AgentVfxSlotDto>(AgentVfxInternalBridge.ReadSlot(slotId));

        public static void SetSlotValue(string slotId, string valueJson) =>
            AgentVfxInternalBridge.SetSlotValue(slotId, valueJson);

        public static AgentVfxConnectResultDto ConnectSlots(string fromSlotId, string toSlotId) =>
            JsonUtility.FromJson<AgentVfxConnectResultDto>(AgentVfxInternalBridge.ConnectSlots(fromSlotId, toSlotId));

        public static void DisconnectSlots(string fromSlotId, string toSlotId) =>
            AgentVfxInternalBridge.DisconnectSlots(fromSlotId, toSlotId);

        public static AgentVfxBlackboardPropertyDto[] ReadBlackboard(string assetPath) =>
            JsonUtility.FromJson<AgentVfxBlackboardListDto>(AgentVfxInternalBridge.ReadBlackboard(assetPath)).properties;

        public static string AddBlackboardProperty(string assetPath, string name, string typeName) =>
            AgentVfxInternalBridge.AddBlackboardProperty(assetPath, name, typeName);

        public static void RemoveBlackboardProperty(string propertyId) =>
            AgentVfxInternalBridge.RemoveBlackboardProperty(propertyId);

        public static AgentVfxCompileResultDto Compile(string assetPath) =>
            JsonUtility.FromJson<AgentVfxCompileResultDto>(AgentVfxInternalBridge.Compile(assetPath));

        public static AgentVfxCompileResultDto GetErrors(string assetPath) =>
            JsonUtility.FromJson<AgentVfxCompileResultDto>(AgentVfxInternalBridge.GetErrors(assetPath));
    }
}

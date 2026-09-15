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

        public static AgentVfxGraphDto ReadGraph(string assetPath) =>
            JsonUtility.FromJson<AgentVfxGraphDto>(AgentVfxInternalBridge.ReadGraph(assetPath));

        public static AgentVfxTypeDto[] ListNodeTypes() =>
            JsonUtility.FromJson<AgentVfxTypeListDto>(AgentVfxInternalBridge.ListNodeTypes()).types;

        public static AgentVfxTypeDetailDto DescribeNodeType(string typeId) =>
            JsonUtility.FromJson<AgentVfxTypeDetailDto>(AgentVfxInternalBridge.DescribeNodeType(typeId));

        public static AgentVfxSlotDto ReadSlot(string slotId) =>
            JsonUtility.FromJson<AgentVfxSlotDto>(AgentVfxInternalBridge.ReadSlot(slotId));

        public static AgentVfxBlackboardPropertyDto[] ReadBlackboard(string assetPath) =>
            JsonUtility.FromJson<AgentVfxBlackboardListDto>(AgentVfxInternalBridge.ReadBlackboard(assetPath)).properties;

    }
}

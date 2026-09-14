using System;

namespace AgentVFX.Editor
{
    // Agent-facing DTOs. No UnityEditor.VFX type may appear here (guide §14) --
    // enforced by this assembly only referencing Unity.VisualEffectGraph.Editor
    // for AgentVfxInternalBridge's public (primitive/JSON-only) surface, which
    // cannot itself expose an internal VFX type across the assembly boundary.
    //
    // Field names below are the wire contract with
    // Assets/AgentVFX/InternalAccess/AgentVfxSnapshots.cs -- keep them in sync;
    // JsonUtility matches by field name only.

    [Serializable]
    public sealed class AgentVfxNodeDto
    {
        public string id;
        public string typeId;
        public string displayName;
        public string kind;
        public string parentId;
        public float x;
        public float y;
        public string[] inputSlotIds;
        public string[] outputSlotIds;
    }

    [Serializable]
    public sealed class AgentVfxSlotDto
    {
        public string id;
        public string nodeId;
        public string name;
        public string typeName;
        public bool isOutput;
        public bool linkable;
        public bool hasLink;
        public string valueJson;
    }

    [Serializable]
    public sealed class AgentVfxConnectionDto
    {
        public string fromNode;
        public string fromSlot;
        public string toNode;
        public string toSlot;
    }

    [Serializable]
    public sealed class AgentVfxGraphDto
    {
        public string asset;
        public AgentVfxNodeDto[] nodes;
        public AgentVfxConnectionDto[] connections;
    }

    [Serializable]
    public sealed class AgentVfxTypeDto
    {
        public string id;
        public string displayName;
        public string kind;
        public string category;
    }

    [Serializable]
    public sealed class AgentVfxTypeDetailDto
    {
        public string id;
        public string displayName;
        public string kind;
        public string category;
        public string[] settingNames;
        public string[] inputNames;
        public string[] outputNames;
        public string[] validContexts;
    }

    [Serializable]
    public sealed class AgentVfxErrorDto
    {
        public string nodeId;
        public string severity;
        public string code;
        public string message;
    }

    [Serializable]
    public sealed class AgentVfxCompileResultDto
    {
        public bool success;
        public AgentVfxErrorDto[] errors;
    }

    [Serializable]
    public sealed class AgentVfxBlackboardPropertyDto
    {
        public string id;
        public string name;
        public string typeName;
        public string valueJson;
    }

    [Serializable]
    public sealed class AgentVfxConnectResultDto
    {
        public bool success;
        public string message;
    }

    [Serializable]
    public sealed class AgentVfxTypeListDto
    {
        public AgentVfxTypeDto[] types;
    }

    [Serializable]
    public sealed class AgentVfxBlackboardListDto
    {
        public AgentVfxBlackboardPropertyDto[] properties;
    }
}

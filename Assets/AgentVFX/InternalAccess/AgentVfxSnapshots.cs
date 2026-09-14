using System;

namespace AgentVFX.Internal
{
    // Plain, JsonUtility-serializable data carried across the AgentVFX.Editor
    // boundary (guide §15: JSON at the agent boundary). Field names here are the
    // wire contract -- Assets/AgentVFX/Editor/AgentVfxDtos.cs mirrors them.
    // No UnityEditor.VFX type may appear on any of these.

    [Serializable]
    public sealed class NodeSnapshot
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
    public sealed class SlotSnapshot
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
    public sealed class ConnectionSnapshot
    {
        public string fromNode;
        public string fromSlot;
        public string toNode;
        public string toSlot;
    }

    [Serializable]
    public sealed class GraphSnapshot
    {
        public string asset;
        public NodeSnapshot[] nodes;
        public ConnectionSnapshot[] connections;
    }

    [Serializable]
    public sealed class TypeSnapshot
    {
        public string id;
        public string displayName;
        public string kind;
        public string category;
    }

    [Serializable]
    public sealed class TypeDetailSnapshot
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
    public sealed class ErrorSnapshot
    {
        public string nodeId;
        public string severity;
        public string code;
        public string message;
    }

    [Serializable]
    public sealed class CompileResultSnapshot
    {
        public bool success;
        public ErrorSnapshot[] errors;
    }

    [Serializable]
    public sealed class BlackboardPropertySnapshot
    {
        public string id;
        public string name;
        public string typeName;
        public string valueJson;
    }
}

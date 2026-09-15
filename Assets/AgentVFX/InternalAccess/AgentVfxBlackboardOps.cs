using System;
using System.Linq;
using UnityEditor.VFX;
using UnityEngine;

namespace AgentVFX.Internal
{
    // Read-only blackboard inspection. Exposed entries are VFXParameter models
    // that are children of the graph itself.
    public static partial class AgentVfxInternalBridge
    {
        public static string ReadBlackboard(string assetPath)
        {
            var graph = OpenGraph(assetPath);
            var properties = graph.children
                .OfType<VFXParameter>()
                .Where(p => p.exposed)
                .Select(BuildParameterSnapshot)
                .ToArray();

            return JsonUtility.ToJson(new BlackboardListSnapshot { properties = properties });
        }

        internal static BlackboardPropertySnapshot BuildParameterSnapshot(VFXParameter parameter)
        {
            var outputSlot = parameter.outputSlots.FirstOrDefault();
            return new BlackboardPropertySnapshot
            {
                id = AgentVfxIdMap.GetOrCreateId(parameter),
                name = parameter.exposedName,
                typeName = parameter.type?.Name,
                valueJson = outputSlot != null ? AgentVfxJson.ToJson(outputSlot.value) : "null",
            };
        }

        [Serializable]
        private sealed class BlackboardListSnapshot
        {
            public BlackboardPropertySnapshot[] properties;
        }
    }
}

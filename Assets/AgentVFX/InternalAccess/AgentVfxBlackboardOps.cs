using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.VFX;
using UnityEngine;

namespace AgentVFX.Internal
{
    // vfx_blackboard_read/add/remove (guide §11 Phase 4). Blackboard entries are
    // VFXParameter models that are children of the graph itself.
    public static partial class AgentVfxInternalBridge
    {
        private static readonly Dictionary<string, Type> SupportedParameterTypes = new()
        {
            ["float"] = typeof(float),
            ["int"] = typeof(int),
            ["bool"] = typeof(bool),
            ["Vector2"] = typeof(Vector2),
            ["Vector3"] = typeof(Vector3),
            ["Vector4"] = typeof(Vector4),
            ["Color"] = typeof(Color),
            ["Texture2D"] = typeof(Texture2D),
            ["Gradient"] = typeof(Gradient),
        };

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

        public static string AddBlackboardProperty(string assetPath, string name, string typeName)
        {
            if (!SupportedParameterTypes.TryGetValue(typeName, out var type))
                throw new NotSupportedException(
                    $"AgentVFX: unsupported blackboard type '{typeName}'. Supported: {string.Join(", ", SupportedParameterTypes.Keys)}.");

            var graph = OpenGraph(assetPath);
            var parameter = ScriptableObject.CreateInstance<VFXParameter>();
            parameter.Init(type);
            parameter.SetSettingValue("m_ExposedName", name);
            parameter.SetSettingValue("m_Exposed", true);
            graph.AddChild(parameter);

            return AgentVfxIdMap.GetOrCreateId(parameter);
        }

        public static void RemoveBlackboardProperty(string propertyId)
        {
            var parameter = AgentVfxIdMap.Resolve<VFXParameter>(propertyId);
            var parent = parameter.GetParent();
            parent?.RemoveChild(parameter);
            AgentVfxIdMap.Forget(parameter);
            UnityEngine.Object.DestroyImmediate(parameter, true);
        }

        private static BlackboardPropertySnapshot BuildParameterSnapshot(VFXParameter parameter)
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

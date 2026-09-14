using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.VFX;
using UnityEngine;

namespace AgentVFX.Internal
{
    // vfx_types_list, vfx_type_describe, vfx_node_create/delete/move/configure
    // (guide §11-12, §16). No effect-specific knowledge: every type comes from
    // Unity's own VFXLibrary catalogue, never a hard-coded list.
    public static partial class AgentVfxInternalBridge
    {
        public static string ListNodeTypes()
        {
            var types = EnumerateDescriptors()
                .Select(entry => new TypeSnapshot
                {
                    id = entry.id,
                    displayName = entry.descriptor.name,
                    kind = entry.kind,
                    category = entry.descriptor.category,
                })
                .ToArray();

            return JsonUtility.ToJson(new TypeListSnapshot { types = types });
        }

        public static string DescribeNodeType(string typeId)
        {
            var entry = EnumerateDescriptors().FirstOrDefault(e => e.id == typeId);
            if (entry.descriptor == null)
                throw new InvalidOperationException($"AgentVFX: unknown node type id '{typeId}'.");

            // VFX Graph does not expose a static per-type slot/setting schema
            // separate from an instance, so describe creates a throwaway instance,
            // reads it, and discards it without adding it to any graph.
            var instance = entry.descriptor.CreateInstance();
            try
            {
                var settingNames = GetSettingNames(instance);
                var inputNames = instance is IVFXSlotContainer sc1 ? sc1.inputSlots.Select(s => s.name).ToArray() : Array.Empty<string>();
                var outputNames = instance is IVFXSlotContainer sc2 ? sc2.outputSlots.Select(s => s.name).ToArray() : Array.Empty<string>();
                var validContexts = instance is VFXBlock block ? DecomposeContextFlags(block.compatibleContexts) : Array.Empty<string>();

                return JsonUtility.ToJson(new TypeDetailSnapshot
                {
                    id = entry.id,
                    displayName = entry.descriptor.name,
                    kind = entry.kind,
                    category = entry.descriptor.category,
                    settingNames = settingNames,
                    inputNames = inputNames,
                    outputNames = outputNames,
                    validContexts = validContexts,
                });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance, true);
            }
        }

        public static string CreateNode(string assetPath, string typeId, string parentId, float x, float y)
        {
            var graph = OpenGraph(assetPath);
            var entry = EnumerateDescriptors().FirstOrDefault(e => e.id == typeId);
            if (entry.descriptor == null)
                throw new InvalidOperationException($"AgentVFX: unknown node type id '{typeId}'.");

            VFXModel parent = string.IsNullOrEmpty(parentId) ? graph : AgentVfxIdMap.Resolve(parentId);
            var model = entry.descriptor.CreateInstance();

            // AddChild throws a raw ArgumentException for an incompatible
            // block/context pairing (e.g. a particle-update-only block under a
            // Spawn context) -- surface it as a clear, structured error the agent
            // can act on instead (check vfx_type_describe's validContexts first).
            if (model is VFXBlock block && parent is VFXContext context &&
                (block.compatibleContexts & context.contextType) == 0)
            {
                UnityEngine.Object.DestroyImmediate(model, true);
                throw new InvalidOperationException(
                    $"AgentVFX: block '{entry.descriptor.name}' is not valid inside a '{context.contextType}' context. " +
                    $"Valid contexts: {string.Join(", ", DecomposeContextFlags(block.compatibleContexts))}.");
            }

            model.position = new Vector2(x, y);
            parent.AddChild(model);

            return AgentVfxIdMap.GetOrCreateId(model);
        }

        public static void DeleteNode(string nodeId)
        {
            var model = AgentVfxIdMap.Resolve(nodeId);
            var parent = model.GetParent();
            parent?.RemoveChild(model);
            AgentVfxIdMap.Forget(model);
            UnityEngine.Object.DestroyImmediate(model, true);
        }

        public static void MoveNode(string nodeId, float x, float y)
        {
            var model = AgentVfxIdMap.Resolve(nodeId);
            model.position = new Vector2(x, y);
        }

        public static void ConfigureNode(string nodeId, string settingName, string valueJson)
        {
            var model = AgentVfxIdMap.Resolve(nodeId);
            var field = FindSettingField(model, settingName);
            if (field == null)
                throw new InvalidOperationException($"AgentVFX: node '{nodeId}' has no setting '{settingName}'.");

            var value = AgentVfxJson.FromJson(valueJson, field.FieldType);
            model.SetSettingValue(settingName, value);
        }

        internal static NodeSnapshot BuildNodeSnapshot(VFXModel model, VFXModel parent)
        {
            var kind = ClassifyKind(model);
            var inputIds = Array.Empty<string>();
            var outputIds = Array.Empty<string>();

            if (model is IVFXSlotContainer container)
            {
                inputIds = container.inputSlots.Select(s => AgentVfxIdMap.GetOrCreateId(s)).ToArray();
                outputIds = container.outputSlots.Select(s => AgentVfxIdMap.GetOrCreateId(s)).ToArray();
            }

            return new NodeSnapshot
            {
                id = AgentVfxIdMap.GetOrCreateId(model),
                // Coarser than a catalogue id: an existing instance only knows its
                // runtime type, not which VFXLibrary variant created it (Unity does
                // not retain that provenance once settings have been applied).
                typeId = $"{kind}:{model.GetType().FullName}",
                displayName = model.name,
                kind = kind,
                parentId = parent is VFXGraph ? null : AgentVfxIdMap.GetOrCreateId(parent),
                x = model.position.x,
                y = model.position.y,
                inputSlotIds = inputIds,
                outputSlotIds = outputIds,
            };
        }

        private static string ClassifyKind(VFXModel model) => model switch
        {
            VFXContext => "Context",
            VFXBlock => "Block",
            VFXOperator => "Operator",
            VFXParameter => "Parameter",
            _ => "Unknown",
        };

        private static IEnumerable<(string id, string kind, IVFXModelDescriptor descriptor)> EnumerateDescriptors()
        {
            var seen = new Dictionary<string, int>();

            IEnumerable<(string, string, IVFXModelDescriptor)> Build(IEnumerable<IVFXModelDescriptor> descriptors, string kind)
            {
                foreach (var descriptor in descriptors)
                {
                    var baseId = $"{kind}:{descriptor.modelType.FullName}:{SanitizeForId(descriptor.name)}";
                    var count = seen.TryGetValue(baseId, out var n) ? n : 0;
                    seen[baseId] = count + 1;
                    yield return ($"{baseId}#{count}", kind, descriptor);
                }
            }

            foreach (var e in Build(VFXLibrary.GetContexts().Cast<IVFXModelDescriptor>(), "Context")) yield return e;
            foreach (var e in Build(VFXLibrary.GetBlocks().Cast<IVFXModelDescriptor>(), "Block")) yield return e;
            foreach (var e in Build(VFXLibrary.GetOperators().Cast<IVFXModelDescriptor>(), "Operator")) yield return e;
        }

        private static string SanitizeForId(string value)
        {
            return string.IsNullOrEmpty(value) ? "unnamed" : value.Replace(' ', '_').Replace('/', '_');
        }

        private static System.Reflection.FieldInfo FindSettingField(VFXModel model, string settingName)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            for (var type = model.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(settingName, flags);
                if (field != null)
                    return field;
            }

            return null;
        }

        // Decomposes a [Flags] VFXContextType into its individual single-bit flag
        // names, ignoring Unity's own combo aliases (InitAndUpdate, All, ...) so
        // the agent gets one array entry per real context kind, not a mix.
        private static string[] DecomposeContextFlags(VFXContextType flags)
        {
            var names = new List<string>();
            foreach (VFXContextType value in Enum.GetValues(typeof(VFXContextType)))
            {
                var bits = (int)value;
                if (bits == 0 || (bits & (bits - 1)) != 0)
                    continue; // skip None and multi-bit combo aliases

                if ((flags & value) == value)
                    names.Add(value.ToString());
            }

            return names.ToArray();
        }

        private static string[] GetSettingNames(VFXModel model)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly;

            var names = new List<string>();
            for (var type = model.GetType(); type != null && type != typeof(VFXModel); type = type.BaseType)
            {
                names.AddRange(type
                    .GetFields(flags)
                    .Where(f => f.GetCustomAttributes(typeof(VFXSettingAttribute), true).Length > 0)
                    .Select(f => f.Name));
            }

            return names.ToArray();
        }

        [Serializable]
        private sealed class TypeListSnapshot
        {
            public TypeSnapshot[] types;
        }
    }
}

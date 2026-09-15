using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.VFX;
using UnityEngine;

namespace AgentVFX.Internal
{
    // All direct dependencies on unstable UnityEditor.VFX internals live under
    // Assets/AgentVFX/InternalAccess/ (this folder compiles into
    // Unity.VisualEffectGraph.Editor via the .asmref next to this file). Nothing
    // outside this folder may reference UnityEditor.VFX types.
    //
    // Every public method on this partial class accepts and returns only
    // strings/primitives/JSON (guide §14-15) -- never a VFXModel/VFXGraph/VFXSlot.
    // AgentVFX.Editor references this assembly by name but cannot see its
    // internal types, so the compiler itself enforces the boundary as long as no
    // public signature here leaks one.
    //
    // See .agent/vfx-graph-agent-bridge/index.md for the full design record.
    public static partial class AgentVfxInternalBridge
    {
        public static string Ping()
        {
            return typeof(VFXGraph).FullName;
        }

        public static string ReadGraph(string assetPath)
        {
            var graph = OpenGraph(assetPath);
            var snapshot = BuildGraphSnapshot(assetPath, graph);
            return JsonUtility.ToJson(snapshot);
        }

        // Internal to this assembly only (not public) -- shared by every partial
        // bridge file that needs a live VFXGraph for a path already validated by
        // AgentVfxPathGuard.
        internal static VFXGraph OpenGraph(string assetPath)
        {
            AgentVfxPathGuard.EnsureAllowed(assetPath);

            var resource = VisualEffectResource.GetResourceAtPath(assetPath);
            if (resource == null)
                throw new InvalidOperationException($"AgentVFX: no VFX asset found at '{assetPath}'.");

            var graph = resource.graph as VFXGraph;
            if (graph == null)
                throw new InvalidOperationException($"AgentVFX: '{assetPath}' has no graph.");

            return graph;
        }

        private static GraphSnapshot BuildGraphSnapshot(string assetPath, VFXGraph graph)
        {
            var nodes = new List<NodeSnapshot>();
            var slots = new List<SlotSnapshot>();
            var collectedSlots = new HashSet<VFXSlot>();
            var connections = new List<ConnectionSnapshot>();
            var flowConnections = new List<FlowConnectionSnapshot>();

            foreach (var model in graph.children)
                CollectNodeRecursive(model, graph, nodes, slots, collectedSlots, connections, flowConnections);

            var blackboard = graph.children
                .OfType<VFXParameter>()
                .Where(parameter => parameter.exposed)
                .Select(BuildParameterSnapshot)
                .ToArray();

            return new GraphSnapshot
            {
                asset = assetPath,
                nodes = nodes.ToArray(),
                slots = slots.ToArray(),
                connections = connections.ToArray(),
                flowConnections = flowConnections.ToArray(),
                blackboard = blackboard,
            };
        }

        private static void CollectNodeRecursive(
            VFXModel model,
            VFXModel parent,
            List<NodeSnapshot> nodes,
            List<SlotSnapshot> slots,
            HashSet<VFXSlot> collectedSlots,
            List<ConnectionSnapshot> connections,
            List<FlowConnectionSnapshot> flowConnections)
        {
            nodes.Add(BuildNodeSnapshot(model, parent));

            if (model is IVFXSlotContainer container)
            {
                foreach (var slot in container.inputSlots)
                    CollectSlotRecursive(slot, slots, collectedSlots, connections);
                foreach (var slot in container.outputSlots)
                    CollectSlotRecursive(slot, slots, collectedSlots, connections);
            }

            if (model is VFXContext context)
            {
                for (var outputIndex = 0; outputIndex < context.outputFlowSlot.Length; outputIndex++)
                {
                    foreach (var link in context.outputFlowSlot[outputIndex].link)
                    {
                        flowConnections.Add(new FlowConnectionSnapshot
                        {
                            fromNode = AgentVfxIdMap.GetOrCreateId(context),
                            fromIndex = outputIndex,
                            toNode = AgentVfxIdMap.GetOrCreateId(link.context),
                            toIndex = link.slotIndex,
                        });
                    }
                }

                foreach (var child in context.children)
                    CollectNodeRecursive(child, context, nodes, slots, collectedSlots, connections, flowConnections);
            }
        }
    }
}

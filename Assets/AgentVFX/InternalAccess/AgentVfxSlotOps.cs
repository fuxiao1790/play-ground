using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.VFX;
using UnityEngine;

namespace AgentVFX.Internal
{
    // Read-only slot inspection.
    public static partial class AgentVfxInternalBridge
    {
        public static string ReadSlot(string slotId)
        {
            var slot = AgentVfxIdMap.Resolve<VFXSlot>(slotId);
            return JsonUtility.ToJson(BuildSlotSnapshot(slot));
        }

        internal static void CollectSlotRecursive(
            VFXSlot slot,
            List<SlotSnapshot> slots,
            HashSet<VFXSlot> collectedSlots,
            List<ConnectionSnapshot> connections)
        {
            AddSlotWithAncestors(slot, slots, collectedSlots, connections);

            foreach (var child in slot.children.Cast<VFXSlot>())
                CollectSlotRecursive(child, slots, collectedSlots, connections);
        }

        private static void AddSlotWithAncestors(
            VFXSlot slot,
            List<SlotSnapshot> slots,
            HashSet<VFXSlot> collectedSlots,
            List<ConnectionSnapshot> connections)
        {
            if (collectedSlots.Contains(slot))
                return;

            var parentSlot = slot.GetParent();
            if (parentSlot != null)
                AddSlotWithAncestors(parentSlot, slots, collectedSlots, connections);

            if (!collectedSlots.Add(slot))
                return;

            slots.Add(BuildSlotSnapshot(slot, parentSlot));

            if (slot.HasLink(recursive: false) && slot.refSlot != slot)
            {
                var source = slot.refSlot;
                connections.Add(new ConnectionSnapshot
                {
                    fromNode = AgentVfxIdMap.GetOrCreateId((VFXModel)source.owner),
                    fromSlot = AgentVfxIdMap.GetOrCreateId(source),
                    toNode = AgentVfxIdMap.GetOrCreateId((VFXModel)slot.owner),
                    toSlot = AgentVfxIdMap.GetOrCreateId(slot),
                });
            }

        }

        private static SlotSnapshot BuildSlotSnapshot(VFXSlot slot, VFXSlot parentSlot = null)
        {
            var container = (VFXModel)slot.owner;
            var isOutput = container is IVFXSlotContainer c && c.outputSlots.Contains(slot);

            return new SlotSnapshot
            {
                id = AgentVfxIdMap.GetOrCreateId(slot),
                nodeId = AgentVfxIdMap.GetOrCreateId(container),
                parentSlotId = parentSlot == null ? null : AgentVfxIdMap.GetOrCreateId(parentSlot),
                name = slot.name,
                typeName = slot.property.type?.Name,
                isOutput = isOutput,
                linkable = true,
                hasLink = slot.HasLink(recursive: true),
                valueJson = AgentVfxJson.ToJson(slot.value),
            };
        }

    }
}

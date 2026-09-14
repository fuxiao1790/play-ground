using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.VFX;
using UnityEngine;

namespace AgentVFX.Internal
{
    // vfx_slot_read, vfx_slot_set, vfx_slot_connect, vfx_slot_disconnect (guide §17).
    public static partial class AgentVfxInternalBridge
    {
        public static string ReadSlot(string slotId)
        {
            var slot = AgentVfxIdMap.Resolve<VFXSlot>(slotId);
            return JsonUtility.ToJson(BuildSlotSnapshot(slot));
        }

        public static void SetSlotValue(string slotId, string valueJson)
        {
            var slot = AgentVfxIdMap.Resolve<VFXSlot>(slotId);
            slot.value = AgentVfxJson.FromJson(valueJson, slot.property.type);
        }

        public static string ConnectSlots(string fromSlotId, string toSlotId)
        {
            var from = AgentVfxIdMap.Resolve<VFXSlot>(fromSlotId);
            var to = AgentVfxIdMap.Resolve<VFXSlot>(toSlotId);

            if (!to.CanLink(from))
                return JsonUtility.ToJson(new ConnectResultSnapshot
                {
                    success = false,
                    message = $"AgentVFX: slot '{toSlotId}' cannot link to '{fromSlotId}' (incompatible types or roles).",
                });

            var linked = to.Link(from);
            return JsonUtility.ToJson(new ConnectResultSnapshot { success = linked, message = linked ? null : "Link() returned false." });
        }

        public static void DisconnectSlots(string fromSlotId, string toSlotId)
        {
            var from = AgentVfxIdMap.Resolve<VFXSlot>(fromSlotId);
            var to = AgentVfxIdMap.Resolve<VFXSlot>(toSlotId);
            to.Unlink(from);
        }

        internal static void CollectLinks(VFXSlot slot, List<ConnectionSnapshot> connections)
        {
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

            foreach (var child in slot.children.Cast<VFXSlot>())
                CollectLinks(child, connections);
        }

        private static SlotSnapshot BuildSlotSnapshot(VFXSlot slot)
        {
            var container = (VFXModel)slot.owner;
            var isOutput = container is IVFXSlotContainer c && c.outputSlots.Contains(slot);

            return new SlotSnapshot
            {
                id = AgentVfxIdMap.GetOrCreateId(slot),
                nodeId = AgentVfxIdMap.GetOrCreateId(container),
                name = slot.name,
                typeName = slot.property.type?.Name,
                isOutput = isOutput,
                linkable = true,
                hasLink = slot.HasLink(recursive: true),
                valueJson = AgentVfxJson.ToJson(slot.value),
            };
        }

        [Serializable]
        private sealed class ConnectResultSnapshot
        {
            public bool success;
            public string message;
        }
    }
}

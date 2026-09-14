using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor.VFX;

namespace AgentVFX.Internal
{
    // Stable-for-the-session agent-facing IDs for VFXModel instances (nodes AND
    // slots -- VFXSlot is itself a VFXModel). IDs are assigned once per object and
    // survive our own edits/saves because Unity does not recreate VFXModel
    // sub-assets when the graph is mutated or saved; they only reset on domain
    // reload, matching Unity's own in-memory graph state. Guide §13, strategy 3.
    internal static class AgentVfxIdMap
    {
        private sealed class IdBox
        {
            public string Id;
        }

        private static readonly ConditionalWeakTable<VFXModel, IdBox> IdByModel = new();
        private static readonly Dictionary<string, WeakReference<VFXModel>> ModelById = new();
        private static int _nextNodeId;
        private static int _nextSlotId;

        public static string GetOrCreateId(VFXModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (IdByModel.TryGetValue(model, out var box))
                return box.Id;

            var id = model is VFXSlot ? $"slot:{_nextSlotId++}" : $"node:{_nextNodeId++}";
            IdByModel.Add(model, new IdBox { Id = id });
            ModelById[id] = new WeakReference<VFXModel>(model);
            return id;
        }

        public static bool TryResolve(string id, out VFXModel model)
        {
            model = null;
            if (string.IsNullOrEmpty(id) || !ModelById.TryGetValue(id, out var weak))
                return false;

            if (!weak.TryGetTarget(out model) || model == null)
            {
                ModelById.Remove(id);
                return false;
            }

            return true;
        }

        public static VFXModel Resolve(string id)
        {
            if (!TryResolve(id, out var model))
                throw new InvalidOperationException($"AgentVFX: unknown or stale id '{id}'. Re-run vfx_graph_read.");

            return model;
        }

        public static T Resolve<T>(string id) where T : VFXModel
        {
            var model = Resolve(id);
            if (model is not T typed)
                throw new InvalidOperationException(
                    $"AgentVFX: id '{id}' refers to a {model.GetType().Name}, expected {typeof(T).Name}.");

            return typed;
        }

        // Forgets every id for a closed graph so stale ids from a previous open
        // fail fast (Resolve throws) instead of silently resolving to a destroyed
        // object.
        public static void Forget(VFXModel model)
        {
            if (model == null || !IdByModel.TryGetValue(model, out var box))
                return;

            ModelById.Remove(box.Id);
            IdByModel.Remove(model);
        }
    }
}

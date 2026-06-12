using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Vfx
{
    public sealed class CombatVfxRoot : MonoBehaviour
    {
        private static readonly Dictionary<int, CombatVfxRoot> Registry = new();
        private static int nextId;

        private CombatVfxDispatcher dispatcher;
        private int rootId;

        private void Awake()
        {
            rootId = ++nextId;
            Registry[rootId] = this;
            dispatcher = new CombatVfxDispatcher(transform);
        }

        private void OnDestroy()
        {
            Registry.Remove(rootId);
            dispatcher?.Dispose();
            dispatcher = null;
        }

        public void Register(int typeId, byte trigger, VisualEffectAsset asset, int maxPerFrame = 2048, bool requireAreaSizeContract = false)
        {
            dispatcher?.Register(typeId, trigger, asset, maxPerFrame, requireAreaSizeContract);
        }

        public void ResetDispatcher()
        {
            dispatcher?.Dispose();
            dispatcher = new CombatVfxDispatcher(transform);
        }

        public void Bind(Entity scopeEntity, EntityManager entityManager)
        {
            var catalog = new CombatScopeVfxCatalog { VfxRootId = rootId };
            if (entityManager.HasComponent<CombatScopeVfxCatalog>(scopeEntity))
                entityManager.SetComponentData(scopeEntity, catalog);
            else
                entityManager.AddComponentData(scopeEntity, catalog);
        }

        internal static bool TryGetRoot(int id, out CombatVfxRoot root) => Registry.TryGetValue(id, out root);

        internal void DrainAndDispatch(DynamicBuffer<VfxSpawnRequestElement> buffer)
        {
            if (dispatcher == null)
            {
                return;
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                VfxSpawnRequestElement e = buffer[i];
                dispatcher.StageSpawn(e.TypeId, e.Trigger, e.Position, e.AreaSize);
            }
            buffer.Clear();
            dispatcher.Dispatch();
        }
    }
}

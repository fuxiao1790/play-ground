using Unity.Entities;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Vfx
{
    public sealed class CombatVfxRoot : MonoBehaviour
    {
        private CombatVfxDispatcher dispatcher;

        private void Awake()
        {
            dispatcher = new CombatVfxDispatcher(transform);
        }

        private void OnDestroy()
        {
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
            entityManager.AddComponentObject(scopeEntity, new CombatScopeVfxCatalog { VfxRoot = this });
        }

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

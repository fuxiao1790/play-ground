using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Profiling;

namespace PlayGround.System.Common
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatHitDispatchSystem : SystemBase
    {
        private static readonly ProfilerMarker Marker = new("CombatHitDispatchSystem");

        private readonly List<ScopeHandler> handlers = new();

        internal void Register(Entity scopeEntity,
            Action<DynamicBuffer<CombatHitElement>,
                   DynamicBuffer<CombatHitPayloadElement>,
                   DynamicBuffer<CombatHitEffectElement>> drain)
        {
            handlers.Add(new ScopeHandler { ScopeEntity = scopeEntity, Drain = drain });
        }

        internal void Unregister(Entity scopeEntity)
        {
            for (int i = handlers.Count - 1; i >= 0; i--)
            {
                if (handlers[i].ScopeEntity == scopeEntity)
                    handlers.RemoveAt(i);
            }
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            using (Marker.Auto())
            {
                for (int i = 0; i < handlers.Count; i++)
                {
                    ScopeHandler h = handlers[i];
                    h.Drain(
                        EntityManager.GetBuffer<CombatHitElement>(h.ScopeEntity),
                        EntityManager.GetBuffer<CombatHitPayloadElement>(h.ScopeEntity),
                        EntityManager.GetBuffer<CombatHitEffectElement>(h.ScopeEntity));
                }
            }
        }

        private struct ScopeHandler
        {
            public Entity ScopeEntity;
            public Action<DynamicBuffer<CombatHitElement>,
                          DynamicBuffer<CombatHitPayloadElement>,
                          DynamicBuffer<CombatHitEffectElement>> Drain;
        }
    }
}

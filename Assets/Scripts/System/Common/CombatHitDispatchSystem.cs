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
        private static readonly ProfilerMarker<int> ReplayMarker =
            new("CombatHitReplay.Dispatch", "Combat Events");

        private readonly List<ScopeHandler> handlers = new();

        internal void Register(Entity scopeEntity,
            Action<DynamicBuffer<CombatDamageElement>,
                   DynamicBuffer<CombatSpawnElement>> drain)
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
                int totalEvents = 0;
                for (int i = 0; i < handlers.Count; i++)
                {
                    ScopeHandler handler = handlers[i];
                    totalEvents += EntityManager.GetBuffer<CombatDamageElement>(handler.ScopeEntity).Length;
                    totalEvents += EntityManager.GetBuffer<CombatSpawnElement>(handler.ScopeEntity).Length;
                }

                using (ReplayMarker.Auto(totalEvents))
                {
                    for (int i = 0; i < handlers.Count; i++)
                    {
                        ScopeHandler h = handlers[i];
                        h.Drain(
                            EntityManager.GetBuffer<CombatDamageElement>(h.ScopeEntity),
                            EntityManager.GetBuffer<CombatSpawnElement>(h.ScopeEntity));
                    }
                }
            }
        }

        private struct ScopeHandler
        {
            public Entity ScopeEntity;
            public Action<DynamicBuffer<CombatDamageElement>,
                          DynamicBuffer<CombatSpawnElement>> Drain;
        }
    }
}

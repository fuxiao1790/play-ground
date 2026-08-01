using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using System.Collections.Generic;
using Unity.Entities;

namespace PlayGround.System.Combat.Targets
{
    public sealed class CombatTargetRegistry<T> where T : ICombatTarget
    {
        private readonly List<T> targets = new();
        private EntityManager entityManager;
        private bool proxyBindingReady;

        public IReadOnlyList<T> Targets => targets;

        public void ConfigureProxyBinding(EntityManager manager)
        {
            entityManager = manager;
            proxyBindingReady = manager != default;

            for (int i = 0; i < targets.Count; i++)
            {
                TryCreateProxy(targets[i]);
            }
        }

        public void ClearProxyBinding()
        {
            entityManager = default;
            proxyBindingReady = false;
        }

        public void Register(T target)
        {
            if (target == null || targets.Contains(target))
            {
                return;
            }

            targets.Add(target);
            TryCreateProxy(target);
        }

        public void Unregister(T target)
        {
            targets.Remove(target);
        }

        private void TryCreateProxy(T target)
        {
            if (!proxyBindingReady
                || target == null
                || !target.IsCombatTargetActive)
            {
                return;
            }

            CombatTargetProxy.Create(entityManager, target, target.CombatFaction);
        }
    }
}

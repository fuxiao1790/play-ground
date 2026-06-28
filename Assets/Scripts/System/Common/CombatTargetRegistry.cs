using System.Collections.Generic;
using Unity.Entities;

namespace PlayGround.System.Common
{
    public sealed class CombatTargetRegistry<T> where T : ICombatTarget
    {
        private readonly List<T> targets = new();
        private readonly Dictionary<int, T> targetsById = new();
        private readonly Dictionary<T, int> proxyKeyByTarget = new();
        private EntityManager entityManager;
        private bool proxyBindingReady;

        public IReadOnlyList<T> Targets => targets;
        public IReadOnlyDictionary<int, T> TargetsById => targetsById;

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
            targetsById.Clear();
            proxyKeyByTarget.Clear();
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
            RemoveTargetLookup(target);
        }

        private void TryCreateProxy(T target)
        {
            if (!proxyBindingReady
                || target == null
                || !target.IsCombatTargetActive)
            {
                return;
            }

            Entity proxy = CombatTargetProxy.Create(entityManager, target, target.CombatFaction);
            if (proxy == Entity.Null)
            {
                return;
            }

            int key = CombatTargetProxy.TargetKey(proxy);
            targetsById[key] = target;
            proxyKeyByTarget[target] = key;
        }

        private void RemoveTargetLookup(T target)
        {
            if (target == null)
            {
                return;
            }

            if (!proxyKeyByTarget.TryGetValue(target, out int key))
            {
                return;
            }

            targetsById.Remove(key);
            proxyKeyByTarget.Remove(target);
        }
    }
}

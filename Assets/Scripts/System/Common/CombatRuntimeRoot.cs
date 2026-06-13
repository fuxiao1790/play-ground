using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.System.Common
{
    public sealed class CombatRuntimeRoot : MonoBehaviour
    {
        [SerializeField] private CombatTargetSetBinding[] targetSets = Array.Empty<CombatTargetSetBinding>();
        [SerializeField] private CombatScopeBinding[] scopes = Array.Empty<CombatScopeBinding>();

        private readonly Dictionary<string, CombatTargetSet> targetSetsByKey = new();
        private readonly List<RuntimeScopeBinding> scopeBindings = new();

        private void Awake()
        {
            RegisterSerializedTargetSets();
            RegisterSerializedScopes();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < scopeBindings.Count; i++)
            {
                scopeBindings[i].Endpoint?.SetCombatRuntimeManaged(false);
            }

            scopeBindings.Clear();
            targetSetsByKey.Clear();
        }

        private void Update()
        {
            foreach (CombatTargetSet targetSet in targetSetsByKey.Values)
            {
                targetSet.BuildSnapshot();
            }

            for (int i = scopeBindings.Count - 1; i >= 0; i--)
            {
                RuntimeScopeBinding binding = scopeBindings[i];
                if (binding.IsDestroyed)
                {
                    scopeBindings.RemoveAt(i);
                    continue;
                }

                if (!targetSetsByKey.TryGetValue(binding.TargetSetKey, out CombatTargetSet targetSet)
                    || !binding.Endpoint.EnsureRuntimeAvailable())
                {
                    continue;
                }

                binding.Endpoint.WriteTargets(targetSet.Snapshots, targetSet.TargetsById);
            }
        }

        public CombatTargetSet GetOrCreateTargetSet(string targetSetKey)
        {
            string key = NormalizeKey(targetSetKey);
            if (!targetSetsByKey.TryGetValue(key, out CombatTargetSet targetSet))
            {
                targetSet = new CombatTargetSet();
                targetSetsByKey.Add(key, targetSet);
            }

            return targetSet;
        }

        public void RegisterTarget(string targetSetKey, ICombatTarget target)
        {
            GetOrCreateTargetSet(targetSetKey).Register(target);
        }

        public void UnregisterTarget(string targetSetKey, ICombatTarget target)
        {
            if (targetSetsByKey.TryGetValue(NormalizeKey(targetSetKey), out CombatTargetSet targetSet))
            {
                targetSet.Unregister(target);
            }
        }

        public void BindScope(Component endpoint, string targetSetKey)
        {
            if (endpoint == null)
            {
                return;
            }

            if (endpoint is not ICombatScopeEndpoint combatEndpoint)
            {
                throw new InvalidOperationException(
                    $"{endpoint.GetType().Name} on {endpoint.name} is not a combat scope endpoint.");
            }

            BindScope(endpoint, combatEndpoint, targetSetKey);
        }

        private void BindScope(Component component, ICombatScopeEndpoint endpoint, string targetSetKey)
        {
            string key = NormalizeKey(targetSetKey);
            GetOrCreateTargetSet(key);

            for (int i = 0; i < scopeBindings.Count; i++)
            {
                RuntimeScopeBinding existing = scopeBindings[i];
                if (existing.Component == component)
                {
                    existing.TargetSetKey = key;
                    scopeBindings[i] = existing;
                    endpoint.SetCombatRuntimeManaged(true);
                    return;
                }
            }

            endpoint.SetCombatRuntimeManaged(true);
            scopeBindings.Add(new RuntimeScopeBinding(component, endpoint, key));
        }

        private void RegisterSerializedTargetSets()
        {
            if (targetSets == null)
            {
                return;
            }

            for (int i = 0; i < targetSets.Length; i++)
            {
                string key = targetSets[i]?.Key;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    GetOrCreateTargetSet(key);
                }
            }
        }

        private void RegisterSerializedScopes()
        {
            if (scopes == null)
            {
                return;
            }

            for (int i = 0; i < scopes.Length; i++)
            {
                CombatScopeBinding binding = scopes[i];
                if (binding?.Endpoint != null)
                {
                    BindScope(binding.Endpoint, binding.TargetSetKey);
                }
            }
        }

        private static string NormalizeKey(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? "Default" : key.Trim();
        }

        [Serializable]
        private sealed class CombatTargetSetBinding
        {
            [SerializeField] private string key = "Default";

            public string Key => key;
        }

        [Serializable]
        private sealed class CombatScopeBinding
        {
            [SerializeField] private Component endpoint;
            [SerializeField] private string targetSetKey = "Default";

            public Component Endpoint => endpoint;
            public string TargetSetKey => targetSetKey;
        }

        private struct RuntimeScopeBinding
        {
            public RuntimeScopeBinding(Component component, ICombatScopeEndpoint endpoint, string targetSetKey)
            {
                Component = component;
                Endpoint = endpoint;
                TargetSetKey = targetSetKey;
            }

            public Component Component;
            public ICombatScopeEndpoint Endpoint;
            public string TargetSetKey;
            public readonly bool IsDestroyed => Component == null;
        }
    }
}

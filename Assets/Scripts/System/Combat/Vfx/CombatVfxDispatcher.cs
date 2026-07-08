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
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Combat.Vfx
{
    public sealed class VfxTypeResources : global::System.IDisposable
    {
        public VisualEffect Instance;
        public GraphicsBuffer PositionBuffer;
        public GraphicsBuffer AreaSizeBuffer;
        public NativeList<float2> Staging;
        public NativeList<float> AreaSizeStaging;
        public int MaxPerFrame;

        public void Dispose()
        {
            // Dispose native memory first so it's freed even if GPU/scene teardown throws.
            if (Staging.IsCreated)
            {
                Staging.Dispose();
            }
            if (AreaSizeStaging.IsCreated)
            {
                AreaSizeStaging.Dispose();
            }
            PositionBuffer?.Release();
            PositionBuffer = null;
            AreaSizeBuffer?.Release();
            AreaSizeBuffer = null;
            if (Instance != null)
            {
                Object.Destroy(Instance.gameObject);
                Instance = null;
            }
        }
    }

    public sealed class CombatVfxDispatcher : global::System.IDisposable
    {
        private const string PositionsPropertyName = "Positions";
        private const string AreaSizePropertyName = "AreaSizes";
        private const string SpawnCountPropertyName = "SpawnCount";
        private const string SpawnEventName = "OnSpawn";

        private static readonly List<VfxTypeResources> LiveResources = new();

        // key = typeId * 256 + trigger
        private readonly Dictionary<int, VfxTypeResources> resources = new();
        private readonly Transform parent;

        public CombatVfxDispatcher(Transform parent)
        {
            this.parent = parent;
        }

        // asset == null means no visual for this trigger; nothing is registered and no memory is allocated.
        public void Register(
            int typeId,
            byte trigger,
            VisualEffectAsset asset,
            int maxPerFrame,
            bool requireAreaSizeContract = false)
        {
            if (asset == null)
            {
                return;
            }

            int key = typeId * 256 + trigger;
            if (resources.ContainsKey(key))
            {
                return;
            }

            GameObject go = null;
            VfxTypeResources res = null;
            try
            {
                go = new GameObject($"Vfx_{typeId}_{trigger}");
                go.layer = parent != null ? parent.gameObject.layer : 0;
                go.transform.SetParent(parent, false);
                VisualEffect vfx = go.AddComponent<VisualEffect>();
                vfx.visualEffectAsset = asset;
                if (!ValidateGraphContract(asset, requireAreaSizeContract, out string reason))
                {
                    Debug.LogError(reason);
                    Object.Destroy(go);
                    return;
                }

                res = new VfxTypeResources
                {
                    MaxPerFrame = maxPerFrame,
                    Instance = vfx,
                };
                res.PositionBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    maxPerFrame,
                    sizeof(float) * 2);
                res.Staging = new NativeList<float2>(maxPerFrame, Allocator.Persistent);
                res.AreaSizeBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    maxPerFrame,
                    sizeof(float));
                res.AreaSizeStaging = new NativeList<float>(maxPerFrame, Allocator.Persistent);

                resources[key] = res;
                LiveResources.Add(res);
            }
            catch (global::System.Exception ex)
            {
                res?.Dispose();
                if (res == null && go != null)
                {
                    Object.Destroy(go);
                }

                Debug.LogError(
                    $"{nameof(CombatVfxDispatcher)} failed to register VFX asset '{asset.name}' "
                    + $"for type {typeId}, trigger {trigger}: {ex.Message}");
            }
        }

        public static int AliveParticleCount(bool visibleOnly = true)
        {
            int count = 0;
            for (int i = LiveResources.Count - 1; i >= 0; i--)
            {
                VfxTypeResources res = LiveResources[i];
                if (res == null || res.Instance == null)
                {
                    LiveResources.RemoveAt(i);
                    continue;
                }

                if (visibleOnly && res.Instance.culled)
                {
                    continue;
                }

                count += Mathf.Max(0, res.Instance.aliveParticleCount);
            }

            return count;
        }

        public bool StageSpawn(int typeId, int trigger, float2 position, float areaSize = 1f)
        {
            int key = typeId * 256 + trigger;
            if (!resources.TryGetValue(key, out VfxTypeResources res))
            {
                return false;
            }

            if (res.Staging.Length >= res.MaxPerFrame)
            {
                return false;
            }

            res.Staging.Add(position);
            res.AreaSizeStaging.Add(math.max(0.01f, areaSize));
            return true;
        }

        public void Dispatch()
        {
            foreach (KeyValuePair<int, VfxTypeResources> pair in resources)
            {
                VfxTypeResources res = pair.Value;
                if (res.Staging.Length == 0)
                {
                    continue;
                }

                Vector3 worldPosition = new(0f, 0f, res.Instance.transform.position.z);
                res.Instance.transform.position = worldPosition;
                res.PositionBuffer.SetData(res.Staging.AsArray(), 0, 0, res.Staging.Length);
                res.Instance.SetGraphicsBuffer(PositionsPropertyName, res.PositionBuffer);
                res.AreaSizeBuffer.SetData(res.AreaSizeStaging.AsArray(), 0, 0, res.AreaSizeStaging.Length);
                res.Instance.SetGraphicsBuffer(AreaSizePropertyName, res.AreaSizeBuffer);
                res.Instance.SetInt(SpawnCountPropertyName, res.Staging.Length);
                res.Instance.SendEvent(SpawnEventName);

                res.Staging.Clear();
                res.AreaSizeStaging.Clear();
            }
        }

        private static bool ValidateGraphContract(
            VisualEffectAsset asset,
            bool requireAreaSizeContract,
            out string reason)
        {
            var props = new List<VFXExposedProperty>();
            asset.GetExposedProperties(props);

            bool hasPositions = false;
            bool hasSpawnCount = false;
            bool hasAreaSize = false;
            foreach (VFXExposedProperty p in props)
            {
                if (p.name == PositionsPropertyName) hasPositions = p.type == typeof(GraphicsBuffer);
                if (p.name == SpawnCountPropertyName) hasSpawnCount = p.type == typeof(int);
                if (p.name == AreaSizePropertyName) hasAreaSize = p.type == typeof(GraphicsBuffer);
            }

            if (!hasPositions || !hasSpawnCount)
            {
                reason = $"{nameof(CombatVfxDispatcher)} cannot register VFX asset '{asset.name}'. "
                    + $"Graph must expose GraphicsBuffer '{PositionsPropertyName}', int '{SpawnCountPropertyName}', "
                    + $"and event '{SpawnEventName}'.";
                return false;
            }

            if (requireAreaSizeContract && !hasAreaSize)
            {
                reason = $"{nameof(CombatVfxDispatcher)} cannot register AOE VFX asset '{asset.name}'. "
                    + $"Graph must expose GraphicsBuffer '{AreaSizePropertyName}'.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public void Dispose()
        {
            foreach (VfxTypeResources res in resources.Values)
            {
                LiveResources.Remove(res);
                res.Dispose();
            }
            resources.Clear();
        }
    }
}

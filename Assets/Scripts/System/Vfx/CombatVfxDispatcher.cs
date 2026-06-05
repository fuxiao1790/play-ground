using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Vfx
{
    public sealed class VfxTypeResources : global::System.IDisposable
    {
        public VisualEffect Instance;
        public GraphicsBuffer PositionBuffer;
        public NativeList<float2> Staging;
        public int MaxPerFrame;

        public void Dispose()
        {
            // Dispose native memory first so it's freed even if GPU/scene teardown throws.
            if (Staging.IsCreated)
            {
                Staging.Dispose();
            }
            PositionBuffer?.Release();
            PositionBuffer = null;
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
        public void Register(int typeId, byte trigger, VisualEffectAsset asset, int maxPerFrame)
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
                if (!ValidateGraphContract(vfx, asset, out string reason))
                {
                    Debug.LogError(reason);
                    Object.Destroy(go);
                    return;
                }

                res = new VfxTypeResources
                {
                    MaxPerFrame = maxPerFrame,
                    Instance = vfx
                };
                res.PositionBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    maxPerFrame,
                    sizeof(float) * 2);
                res.Staging = new NativeList<float2>(maxPerFrame, Allocator.Persistent);

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

        public void StageSpawn(int typeId, int trigger, float2 position)
        {
            int key = typeId * 256 + trigger;
            if (!resources.TryGetValue(key, out VfxTypeResources res))
            {
                return;
            }

            if (res.Staging.Length < res.MaxPerFrame)
            {
                res.Staging.Add(position);
            }
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
                res.Instance.SetInt(SpawnCountPropertyName, res.Staging.Length);
                res.Instance.SendEvent(SpawnEventName);

                res.Staging.Clear();
            }
        }

        private static bool ValidateGraphContract(VisualEffect vfx, VisualEffectAsset asset, out string reason)
        {
            if (!vfx.HasGraphicsBuffer(PositionsPropertyName) || !vfx.HasInt(SpawnCountPropertyName))
            {
                reason = $"{nameof(CombatVfxDispatcher)} cannot register VFX asset '{asset.name}'. "
                    + $"Graph must expose GraphicsBuffer '{PositionsPropertyName}', int '{SpawnCountPropertyName}', "
                    + $"and event '{SpawnEventName}'.";
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

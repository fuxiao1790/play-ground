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
    // Manager for every combat VFX resource: owns each AoeVfxTypeResources (GameObject,
    // VisualEffect, GraphicsBuffers, staging lists), the (typeId, trigger) table onto them, and
    // their teardown. CombatAoeVfxDispatcher owns none of that — it only knows the graph protocol.
    public sealed class CombatVfxRoot : MonoBehaviour
    {
        public static CombatVfxRoot Instance { get; private set; }

        private const int TriggerKeyStride = 256;

        private static readonly List<AoeVfxTypeResources> LiveResources = new();

        private readonly Dictionary<int, AoeVfxTypeResources> resources = new();
        private CombatAoeVfxDispatcher dispatcher;

        private void Awake()
        {
            dispatcher = new CombatAoeVfxDispatcher();
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            DisposeResources();
            dispatcher = null;
        }

        // asset == null means no visual for this trigger; nothing is registered and no memory is allocated.
        public void Register(
            int typeId,
            AoeVfxTrigger trigger,
            VisualEffectAsset asset,
            int maxPerFrame = 2048,
            bool requireAreaSizeContract = false)
        {
            if (asset == null || dispatcher == null)
            {
                return;
            }

            int key = KeyFor(typeId, trigger);
            if (resources.ContainsKey(key))
            {
                return;
            }

            GameObject go = null;
            AoeVfxTypeResources res = null;
            try
            {
                go = new GameObject($"Vfx_{typeId}_{trigger}");
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                VisualEffect vfx = go.AddComponent<VisualEffect>();
                vfx.visualEffectAsset = asset;
                if (!dispatcher.ValidateGraphContract(asset, requireAreaSizeContract, out string reason))
                {
                    Debug.LogError(reason);
                    Destroy(go);
                    return;
                }

                res = new AoeVfxTypeResources
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
                    Destroy(go);
                }

                Debug.LogError(
                    $"{nameof(CombatVfxRoot)} failed to register VFX asset '{asset.name}' "
                    + $"for type {typeId}, trigger {trigger}: {ex.Message}");
            }
        }

        public void ResetDispatcher()
        {
            DisposeResources();
            dispatcher = new CombatAoeVfxDispatcher();
        }

        internal int DrainAndDispatch(ref NativeQueue<AoeVfxSpawnRequest> queue)
        {
            if (dispatcher == null)
            {
                return 0;
            }

            int acceptedSpawnCount = 0;
            while (queue.TryDequeue(out AoeVfxSpawnRequest p))
            {
                if (StageAoeSpawn(p.TypeId, p.Trigger, p.Position, p.AreaSize))
                {
                    acceptedSpawnCount++;
                }
            }

            foreach (KeyValuePair<int, AoeVfxTypeResources> pair in resources)
            {
                AoeVfxTypeResources res = pair.Value;
                if (res.Staging.Length == 0)
                {
                    continue;
                }

                dispatcher.Dispatch(res);
                res.Staging.Clear();
                res.AreaSizeStaging.Clear();
            }

            return acceptedSpawnCount;
        }

        public static int AliveParticleCount(bool visibleOnly = true)
        {
            int count = 0;
            for (int i = LiveResources.Count - 1; i >= 0; i--)
            {
                AoeVfxTypeResources res = LiveResources[i];
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

        private bool StageAoeSpawn(int typeId, AoeVfxTrigger trigger, float2 position, float areaSize)
        {
            int key = KeyFor(typeId, trigger);
            if (!resources.TryGetValue(key, out AoeVfxTypeResources res))
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

        private static int KeyFor(int typeId, AoeVfxTrigger trigger)
        {
            return typeId * TriggerKeyStride + (byte)trigger;
        }

        private void DisposeResources()
        {
            foreach (AoeVfxTypeResources res in resources.Values)
            {
                LiveResources.Remove(res);
                res.Dispose();
            }
            resources.Clear();
        }
    }
}

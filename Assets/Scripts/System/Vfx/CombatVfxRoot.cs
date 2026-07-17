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
    // VisualEffect, GraphicsBuffers, staging lists), the effect-id table onto them, and
    // their teardown. CombatAoeVfxDispatcher owns none of that - it only knows the graph protocol.
    public sealed class CombatVfxRoot : MonoBehaviour
    {
        public static CombatVfxRoot Instance { get; private set; }

        // Sole owner and dispatch/teardown set. Append-only for the root lifetime; VfxId is
        // (index + 1) so 0 stays a safe no-VFX sentinel that can never address a resource.
        private readonly List<AoeVfxTypeResources> owners = new();
        private readonly Dictionary<VisualEffectAsset, int> idsByAsset = new();
        private CombatAoeVfxDispatcher dispatcher;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    $"{nameof(CombatVfxRoot)} on '{name}' rejected: '{Instance.name}' is already the "
                    + "active combat VFX root.");
                enabled = false;
                return;
            }

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

        // asset == null means no visual for this graph kind; nothing is registered and no memory
        // is allocated. Re-registering an already-known asset returns its existing id without
        // allocating; the first successful registration owns the graph's contract options.
        public int Register(
            VisualEffectAsset asset,
            bool requireAreaSizeContract = false)
        {
            if (asset == null || dispatcher == null)
            {
                return 0;
            }

            if (idsByAsset.TryGetValue(asset, out int existingId))
            {
                AoeVfxTypeResources existing = owners[existingId - 1];
                if (existing.RequireAreaSizeContract != requireAreaSizeContract)
                {
                    Debug.LogError(
                        $"{nameof(CombatVfxRoot)} re-registered VFX asset '{asset.name}' (id {existingId}) "
                        + "with a conflicting contract; the graph keeps its original contract.");
                }

                return existingId;
            }

            if (!dispatcher.ValidateGraphContract(asset, requireAreaSizeContract, out string reason))
            {
                Debug.LogError(reason);
                return 0;
            }

            GameObject go = null;
            AoeVfxTypeResources res = null;
            int newId = owners.Count + 1;
            try
            {
                go = new GameObject($"{asset.name}_{newId}");
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                VisualEffect vfx = go.AddComponent<VisualEffect>();
                vfx.visualEffectAsset = asset;

                res = new AoeVfxTypeResources
                {
                    BufferCapacity = AoeVfxTypeResources.InitialBufferCapacity,
                    RequireAreaSizeContract = requireAreaSizeContract,
                    Instance = vfx,
                };
                res.PositionBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    AoeVfxTypeResources.InitialBufferCapacity,
                    sizeof(float) * 2);
                res.Staging = new NativeList<float2>(
                    AoeVfxTypeResources.InitialBufferCapacity,
                    Allocator.Persistent);
                res.AreaSizeBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    AoeVfxTypeResources.InitialBufferCapacity,
                    sizeof(float));
                res.AreaSizeStaging = new NativeList<float>(
                    AoeVfxTypeResources.InitialBufferCapacity,
                    Allocator.Persistent);

                owners.Add(res);
                idsByAsset[asset] = newId;
                return newId;
            }
            catch (global::System.Exception ex)
            {
                res?.Dispose();
                if (res == null && go != null)
                {
                    Destroy(go);
                }

                Debug.LogError(
                    $"{nameof(CombatVfxRoot)} failed to register VFX asset '{asset.name}': {ex.Message}");
                return 0;
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
                if (StageAoeSpawn(p.VfxId, p.Position, p.AreaSize))
                {
                    acceptedSpawnCount++;
                }
            }

            foreach (AoeVfxTypeResources res in owners)
            {
                if (res.Staging.Length == 0)
                {
                    continue;
                }

                dispatcher.Dispatch(res);
                res.ClearStaging();
            }

            return acceptedSpawnCount;
        }

        public static int AliveParticleCount(bool visibleOnly = true)
        {
            int count = 0;
            if (Instance == null)
            {
                return count;
            }

            foreach (AoeVfxTypeResources res in Instance.owners)
            {
                if (res == null || res.Instance == null)
                {
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

        private bool StageAoeSpawn(int vfxId, float2 position, float areaSize)
        {
            if (vfxId <= 0 || vfxId > owners.Count)
            {
                return false;
            }

            AoeVfxTypeResources res = owners[vfxId - 1];
            if (res == null)
            {
                return false;
            }

            return res.TryStage(position, math.max(0.01f, areaSize));
        }

        private void DisposeResources()
        {
            foreach (AoeVfxTypeResources res in owners)
            {
                res.Dispose();
            }

            owners.Clear();
            idsByAsset.Clear();
        }
    }
}

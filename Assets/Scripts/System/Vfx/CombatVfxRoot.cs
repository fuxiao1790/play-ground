using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Combat.Vfx
{
    // Manager for every combat VFX resource: owns each AoeVfxTypeResources, the encoded
    // effect-id table onto them, and their teardown. CombatAoeVfxDispatcher owns none of that.
    public sealed class CombatVfxRoot : MonoBehaviour
    {
        public static CombatVfxRoot Instance { get; private set; }

        public int RegisteredVfxCount => RegisteredCountFor(VfxDataShape.Basic);

        private readonly List<AoeVfxTypeResources>[] ownersByShape =
        {
            new(),
            new()
        };
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

        public int Register(VisualEffectAsset asset, VfxDataShape shape = VfxDataShape.Basic)
        {
            if (asset == null || dispatcher == null)
            {
                return 0;
            }

            if (idsByAsset.TryGetValue(asset, out int existingId))
            {
                AoeVfxTypeResources existing = ResourceForId(existingId);
                if (existing != null && existing.Shape != shape)
                {
                    Debug.LogError(
                        $"{nameof(CombatVfxRoot)} re-registered VFX asset '{asset.name}' (id {existingId}) "
                        + "with a conflicting shape; the graph keeps its original shape.");
                }

                return existingId;
            }

            if (!dispatcher.ValidateGraphContract(asset, shape, out string reason))
            {
                Debug.LogError(reason);
                return 0;
            }

            GameObject go = null;
            AoeVfxTypeResources res = null;
            List<AoeVfxTypeResources> owners = OwnersFor(shape);
            int localIndex = owners.Count + 1;
            int newId = VfxDataShapeTable.EncodeId(shape, localIndex);
            try
            {
                go = new GameObject($"{asset.name}_{newId}");
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                VisualEffect vfx = go.AddComponent<VisualEffect>();
                vfx.visualEffectAsset = asset;

                res = new AoeVfxTypeResources
                {
                    Instance = vfx,
                    Shape = shape
                };
                res.EnsureBufferCapacity(AoeVfxTypeResources.InitialBufferCapacity);

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

        public int RegisteredCountFor(VfxDataShape shape) => OwnersFor(shape).Count;

        internal int DrainAndDispatch(
            NativeArray<float2> sortedPositions,
            NativeArray<float> sortedAreaSizes,
            NativeArray<int> bucketOffsets) =>
            DrainAndDispatchBasic(sortedPositions, sortedAreaSizes, bucketOffsets);

        internal int DrainAndDispatchBasic(
            NativeArray<float2> sortedPositions,
            NativeArray<float> sortedAreaSizes,
            NativeArray<int> bucketOffsets)
        {
            if (dispatcher == null)
            {
                return 0;
            }

            List<AoeVfxTypeResources> owners = OwnersFor(VfxDataShape.Basic);
            for (int localIndex = 1; localIndex <= owners.Count; localIndex++)
            {
                int start = bucketOffsets[localIndex];
                int count = bucketOffsets[localIndex + 1] - start;
                if (count == 0)
                {
                    continue;
                }

                AoeVfxTypeResources res = owners[localIndex - 1];
                if (res == null)
                {
                    continue;
                }

                dispatcher.DispatchBasic(
                    res,
                    sortedPositions.GetSubArray(start, count),
                    sortedAreaSizes.GetSubArray(start, count));
            }

            return sortedPositions.Length;
        }

        internal int DrainAndDispatchTimed(
            NativeArray<float2> sortedPositions,
            NativeArray<float> sortedAreaSizes,
            NativeArray<float> sortedDurations,
            NativeArray<float> sortedTickIntervals,
            NativeArray<int> bucketOffsets)
        {
            if (dispatcher == null)
            {
                return 0;
            }

            List<AoeVfxTypeResources> owners = OwnersFor(VfxDataShape.Timed);
            for (int localIndex = 1; localIndex <= owners.Count; localIndex++)
            {
                int start = bucketOffsets[localIndex];
                int count = bucketOffsets[localIndex + 1] - start;
                if (count == 0)
                {
                    continue;
                }

                AoeVfxTypeResources res = owners[localIndex - 1];
                if (res == null)
                {
                    continue;
                }

                dispatcher.DispatchTimed(
                    res,
                    sortedPositions.GetSubArray(start, count),
                    sortedAreaSizes.GetSubArray(start, count),
                    sortedDurations.GetSubArray(start, count),
                    sortedTickIntervals.GetSubArray(start, count));
            }

            return sortedPositions.Length;
        }

        public static int AliveParticleCount(bool visibleOnly = true)
        {
            int count = 0;
            if (Instance == null)
            {
                return count;
            }

            foreach (List<AoeVfxTypeResources> owners in Instance.ownersByShape)
            {
                foreach (AoeVfxTypeResources res in owners)
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
            }

            return count;
        }

        private void DisposeResources()
        {
            foreach (List<AoeVfxTypeResources> owners in ownersByShape)
            {
                foreach (AoeVfxTypeResources res in owners)
                {
                    res.Dispose();
                }

                owners.Clear();
            }

            idsByAsset.Clear();
        }

        private List<AoeVfxTypeResources> OwnersFor(VfxDataShape shape) =>
            ownersByShape[(int)shape];

        private AoeVfxTypeResources ResourceForId(int id)
        {
            VfxDataShape shape = VfxDataShapeTable.DecodeShape(id);
            int localIndex = VfxDataShapeTable.DecodeLocalIndex(id);
            List<AoeVfxTypeResources> owners = OwnersFor(shape);
            int listIndex = localIndex - 1;
            return listIndex >= 0 && listIndex < owners.Count ? owners[listIndex] : null;
        }
    }
}

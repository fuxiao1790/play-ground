using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Combat.Vfx
{
    // Manager for every combat VFX resource: owns each AoeVfxResourcesBase, the encoded
    // effect-id table onto them, and their teardown. CombatAoeVfxDispatcher owns none of that.
    public sealed class CombatVfxRoot : MonoBehaviour
    {
        public static CombatVfxRoot Instance { get; private set; }

        public int RegisteredVfxCount => RegisteredCountFor(VfxDataShape.ImpactCircle);

        private readonly List<AoeVfxResourcesBase>[] ownersByShape =
        {
            new(),
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

        public int Register(VisualEffectAsset asset, VfxDataShape shape = VfxDataShape.ImpactCircle)
        {
            if (asset == null || dispatcher == null)
            {
                return 0;
            }

            if (idsByAsset.TryGetValue(asset, out int existingId))
            {
                AoeVfxResourcesBase existing = ResourceForId(existingId);
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
            AoeVfxResourcesBase res = null;
            List<AoeVfxResourcesBase> owners = OwnersFor(shape);
            int localIndex = owners.Count + 1;
            int newId = VfxDataShapeTable.EncodeId(shape, localIndex);
            try
            {
                go = new GameObject($"{asset.name}_{newId}");
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                VisualEffect vfx = go.AddComponent<VisualEffect>();
                vfx.visualEffectAsset = asset;

                res = CreateResources(shape, vfx);
                res.EnsureBufferCapacity(AoeVfxResourcesBase.InitialBufferCapacity);

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

        private static AoeVfxResourcesBase CreateResources(VfxDataShape shape, VisualEffect vfx) =>
            shape switch
            {
                VfxDataShape.ImpactCircle => new CircularVfxResources { Instance = vfx },
                VfxDataShape.LingeringCircle => new TimedCircularVfxResources { Instance = vfx },
                VfxDataShape.LineSegment => new LineSegmentVfxResources { Instance = vfx },
                _ => throw new global::System.ArgumentOutOfRangeException(
                    nameof(shape), shape, "Unknown VFX data shape.")
            };

        public int RegisteredCountFor(VfxDataShape shape) => OwnersFor(shape).Count;

        internal int DrainAndDispatch(
            NativeArray<float2> sortedPositions,
            NativeArray<float> sortedAreaSizes,
            NativeArray<int> bucketOffsets) =>
            DrainAndDispatchCircular(sortedPositions, sortedAreaSizes, bucketOffsets);

        internal int DrainAndDispatchCircular(
            NativeArray<float2> sortedPositions,
            NativeArray<float> sortedAreaSizes,
            NativeArray<int> bucketOffsets)
        {
            if (dispatcher == null)
            {
                return 0;
            }

            List<AoeVfxResourcesBase> owners = OwnersFor(VfxDataShape.ImpactCircle);
            for (int localIndex = 1; localIndex <= owners.Count; localIndex++)
            {
                int start = bucketOffsets[localIndex];
                int count = bucketOffsets[localIndex + 1] - start;
                if (count == 0)
                {
                    continue;
                }

                var res = owners[localIndex - 1] as CircularVfxResources;
                if (res == null)
                {
                    continue;
                }

                dispatcher.DispatchCircular(
                    res,
                    sortedPositions.GetSubArray(start, count),
                    sortedAreaSizes.GetSubArray(start, count));
            }

            return sortedPositions.Length;
        }

        internal int DrainAndDispatchTimedCircular(
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

            List<AoeVfxResourcesBase> owners = OwnersFor(VfxDataShape.LingeringCircle);
            for (int localIndex = 1; localIndex <= owners.Count; localIndex++)
            {
                int start = bucketOffsets[localIndex];
                int count = bucketOffsets[localIndex + 1] - start;
                if (count == 0)
                {
                    continue;
                }

                var res = owners[localIndex - 1] as TimedCircularVfxResources;
                if (res == null)
                {
                    continue;
                }

                dispatcher.DispatchTimedCircular(
                    res,
                    sortedPositions.GetSubArray(start, count),
                    sortedAreaSizes.GetSubArray(start, count),
                    sortedDurations.GetSubArray(start, count),
                    sortedTickIntervals.GetSubArray(start, count));
            }

            return sortedPositions.Length;
        }

        internal int DrainAndDispatchLineSegment(
            NativeArray<float2> sortedStartPositions,
            NativeArray<float2> sortedEndPositions,
            NativeArray<float> sortedWidths,
            NativeArray<int> bucketOffsets)
        {
            if (dispatcher == null)
            {
                return 0;
            }

            List<AoeVfxResourcesBase> owners = OwnersFor(VfxDataShape.LineSegment);
            for (int localIndex = 1; localIndex <= owners.Count; localIndex++)
            {
                int start = bucketOffsets[localIndex];
                int count = bucketOffsets[localIndex + 1] - start;
                if (count == 0)
                {
                    continue;
                }

                var res = owners[localIndex - 1] as LineSegmentVfxResources;
                if (res == null)
                {
                    continue;
                }

                dispatcher.DispatchLineSegment(
                    res,
                    sortedStartPositions.GetSubArray(start, count),
                    sortedEndPositions.GetSubArray(start, count),
                    sortedWidths.GetSubArray(start, count));
            }

            return sortedStartPositions.Length;
        }

        public static int AliveParticleCount(bool visibleOnly = true)
        {
            int count = 0;
            if (Instance == null)
            {
                return count;
            }

            foreach (List<AoeVfxResourcesBase> owners in Instance.ownersByShape)
            {
                foreach (AoeVfxResourcesBase res in owners)
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
            foreach (List<AoeVfxResourcesBase> owners in ownersByShape)
            {
                foreach (AoeVfxResourcesBase res in owners)
                {
                    res.Dispose();
                }

                owners.Clear();
            }

            idsByAsset.Clear();
        }

        private List<AoeVfxResourcesBase> OwnersFor(VfxDataShape shape) =>
            ownersByShape[(int)shape];

        private AoeVfxResourcesBase ResourceForId(int id)
        {
            VfxDataShape shape = VfxDataShapeTable.DecodeShape(id);
            int localIndex = VfxDataShapeTable.DecodeLocalIndex(id);
            List<AoeVfxResourcesBase> owners = OwnersFor(shape);
            int listIndex = localIndex - 1;
            return listIndex >= 0 && listIndex < owners.Count ? owners[listIndex] : null;
        }
    }
}

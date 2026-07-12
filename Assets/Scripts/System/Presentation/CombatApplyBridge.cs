using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Targets;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;

namespace PlayGround.System.Combat.Application
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(CombatBatchedRenderSystem))]
    public partial class CombatApplyBridge : SystemBase
    {
        private static readonly ProfilerMarker Marker = new("CombatApplyBridge");
        private static readonly ProfilerMarker<int> TickReplayMarker =
            new("CombatApplyBridge.TickReplay", "Combat Tick Results");

        private static readonly List<StatusStackSnapshot> statusScratch = new();

        protected override void OnUpdate()
        {
            CompleteDependency();
            if (!SystemAPI.TryGetSingletonRW<CombatApplyResultSingleton>(
                    out RefRW<CombatApplyResultSingleton> resultLane))
            {
                return;
            }

            ref CombatApplyResultSingleton lane = ref resultLane.ValueRW;
            lane.ProducerHandle.Complete();
            lane.ProducerHandle = default;

            if (!lane.Results.IsCreated)
            {
                return;
            }

            bool hasStatusSnapshots = HasAnyStatusRange(lane.Results);
            if (lane.Results.Length == 0 && !hasStatusSnapshots)
            {
                lane.Clear();
                return;
            }

            using (Marker.Auto())
            using (TickReplayMarker.Auto(lane.Results.Length))
            {
                ReplayCombat(
                    lane.Results,
                    lane.StatusSnapshots,
                    EntityManager);
            }

            lane.Clear();
        }

        private static void ReplayCombat(
            NativeList<CombatTickResult> results,
            NativeList<StatusStackSnapshot> statusSnapshots,
            EntityManager entityManager)
        {
            for (int resultIndex = 0; resultIndex < results.Length; resultIndex++)
            {
                CombatTickResult result = results[resultIndex];
                if (result.HitCount <= 0 && result.StatusCount <= 0)
                {
                    continue;
                }

                ICombatTarget target = ResolveTarget(entityManager, result.TargetProxy);
                if (!IsTargetUsable(target))
                {
                    continue;
                }

                statusScratch.Clear();
                for (int i = 0; i < result.StatusCount; i++)
                {
                    statusScratch.Add(statusSnapshots[result.StatusStart + i]);
                }

                target.ReceiveCombatTick(in result, statusScratch);
            }

            statusScratch.Clear();
        }

        private static bool HasAnyStatusRange(NativeList<CombatTickResult> results)
        {
            if (!results.IsCreated)
            {
                return false;
            }

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].StatusCount > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static ICombatTarget ResolveTarget(EntityManager entityManager, Entity targetProxy)
        {
            if (targetProxy == Entity.Null
                || !entityManager.Exists(targetProxy)
                || !entityManager.HasComponent<TargetCompanion>(targetProxy))
            {
                return null;
            }

            TargetCompanion companion = entityManager.GetComponentObject<TargetCompanion>(targetProxy);
            return companion?.Target;
        }

        private static bool IsTargetUsable(ICombatTarget target) =>
            target != null
            && (target is not UnityEngine.Object unityObject || unityObject != null)
            && target.IsCombatTargetActive;
    }
}

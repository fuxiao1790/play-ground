using System.Collections.Generic;
using PlayGround.Common;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;

namespace PlayGround.System.Common
{
    internal interface ICombatHitReplayAdapter<TTarget>
        where TTarget : class
    {
        DamageSnapshot RollDamage(in CombatDamageElement damage);
        void ReplaySpawn(in CombatSpawnElement spawn, TTarget target);
        void ReplayBatch(IReadOnlyList<CombatHitData> data, TTarget target);
    }

    internal static class CombatHitReplay
    {
        private static readonly ProfilerMarker<int> DamageReplayMarker =
            new("CombatHitReplay.Damage", "Damage Events");
        private static readonly ProfilerMarker<int> SpawnReplayMarker =
            new("CombatHitReplay.Spawn", "Spawn Events");
        private static readonly List<CombatHitData> hitDataScratch = new();

        public static void ReplayAndClear<TTarget, TAdapter>(
            DynamicBuffer<CombatDamageElement> damageBuffer,
            DynamicBuffer<CombatSpawnElement> spawnBuffer,
            IReadOnlyDictionary<int, TTarget> targetsById,
            ref TAdapter adapter)
            where TTarget : class
            where TAdapter : struct, ICombatHitReplayAdapter<TTarget>
        {
            using (DamageReplayMarker.Auto(damageBuffer.Length))
            {
                ReplayDamage(damageBuffer, targetsById, ref adapter);
            }

            using (SpawnReplayMarker.Auto(spawnBuffer.Length))
            {
                ReplaySpawns(spawnBuffer, targetsById, ref adapter);
            }

            damageBuffer.Clear();
            spawnBuffer.Clear();
        }

        public static bool IsTargetUsable<TTarget>(TTarget target)
            where TTarget : class
        {
            return target != null
                && (target is not UnityEngine.Object unityObject || unityObject != null)
                && target is ICombatTarget combatTarget
                && combatTarget.IsCombatTargetActive;
        }

        private static void ReplayDamage<TTarget, TAdapter>(
            DynamicBuffer<CombatDamageElement> damageBuffer,
            IReadOnlyDictionary<int, TTarget> targetsById,
            ref TAdapter adapter)
            where TTarget : class
            where TAdapter : struct, ICombatHitReplayAdapter<TTarget>
        {
            int hitCount = damageBuffer.Length;
            int i = 0;
            while (i < hitCount)
            {
                int groupTargetId = damageBuffer[i].TargetId;
                TryGetLiveTarget(targetsById, groupTargetId, out TTarget target);
                hitDataScratch.Clear();

                while (i < hitCount && damageBuffer[i].TargetId == groupTargetId)
                {
                    CombatDamageElement damageEvent = damageBuffer[i++];
                    DamageSnapshot damage = adapter.RollDamage(in damageEvent);
                    hitDataScratch.Add(new CombatHitData(
                        damageEvent.Kind, damage,
                        new Vector2(damageEvent.Position.x, damageEvent.Position.y),
                        damageEvent.DirectDamageEnabled, damageEvent.StackEffect,
                        damageEvent.SourceNodeId));
                }

                adapter.ReplayBatch(hitDataScratch, target);
            }
        }

        private static void ReplaySpawns<TTarget, TAdapter>(
            DynamicBuffer<CombatSpawnElement> spawnBuffer,
            IReadOnlyDictionary<int, TTarget> targetsById,
            ref TAdapter adapter)
            where TTarget : class
            where TAdapter : struct, ICombatHitReplayAdapter<TTarget>
        {
            for (int i = 0; i < spawnBuffer.Length; i++)
            {
                CombatSpawnElement spawn = spawnBuffer[i];
                adapter.ReplaySpawn(in spawn, null);
            }
        }

        private static bool TryGetLiveTarget<TTarget>(
            IReadOnlyDictionary<int, TTarget> targetsById,
            int targetId,
            out TTarget target)
            where TTarget : class
        {
            if (targetsById.TryGetValue(targetId, out target) && IsTargetUsable(target))
            {
                return true;
            }

            target = null;
            return false;
        }
    }
}

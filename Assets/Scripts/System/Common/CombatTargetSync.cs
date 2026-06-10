using System;
using System.Collections.Generic;
using PlayGround.Common;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Common
{
    public sealed class CombatTargetSync<T> where T : class, ICombatTarget
    {
        private readonly CombatTargetRegistry<T> registry;
        private readonly Dictionary<int, T> targetsById = new();

        public CombatTargetSync(CombatTargetRegistry<T> registry)
        {
            this.registry = registry;
        }

        public IReadOnlyDictionary<int, T> TargetsById => targetsById;

        public void SyncToBuffer(
            DynamicBuffer<CombatTargetElement> buffer,
            int maxCount = int.MaxValue,
            Func<T, bool> additionalFilter = null)
        {
            buffer.Clear();
            targetsById.Clear();
            IReadOnlyList<T> targets = registry.Targets;
            int count = 0;
            for (int i = 0; i < targets.Count && count < maxCount; i++)
            {
                T target = targets[i];
                if (target == null || !target.IsCombatTargetActive)
                {
                    continue;
                }

                if (additionalFilter != null && !additionalFilter(target))
                {
                    continue;
                }

                float2 pos = new(target.CombatTargetPosition.x, target.CombatTargetPosition.y);
                float2 halfExtents = new(target.CombatTargetHalfExtents.x, target.CombatTargetHalfExtents.y);
                CombatCollisionMath.ComputeWorldBounds(
                    pos,
                    target.CombatTargetRadius,
                    halfExtents,
                    target.CombatTargetRotationRadians,
                    target.CombatTargetShapeType,
                    out float2 boundsMin,
                    out float2 boundsMax);

                buffer.Add(new CombatTargetElement
                {
                    TargetId = target.TargetId,
                    TargetMask = target.CombatTargetMask,
                    Position = pos,
                    ShapeType = target.CombatTargetShapeType,
                    Radius = target.CombatTargetRadius,
                    HalfExtents = halfExtents,
                    RotationRadians = target.CombatTargetRotationRadians,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax
                });
                targetsById[target.TargetId] = target;
                count++;
            }
        }
    }

    internal interface ICombatHitReplayAdapter<THit, TTarget>
        where THit : unmanaged, IBufferElementData
        where TTarget : class
    {
        int TargetId(in THit hit);
        DamageSnapshot RollDamage(in THit hit);
        void Replay(in THit hit, TTarget target, in DamageSnapshot damage);
    }

    internal static class CombatHitReplay
    {
        private static readonly ProfilerMarker<int> ReplayMarker =
            new("CombatHitReplay.Replay", "Hit Events");
        private static readonly ProfilerCounterValue<int> ReplayEventCounter =
            new(ProfilerCategory.Scripts, "CombatHitReplay.Events", ProfilerMarkerDataUnit.Count);

        public static void ReplayAndClear<THit, TTarget, TAdapter>(
            DynamicBuffer<THit> hitBuffer,
            IReadOnlyDictionary<int, TTarget> targetsById,
            ref TAdapter adapter)
            where THit : unmanaged, IBufferElementData
            where TTarget : class
            where TAdapter : struct, ICombatHitReplayAdapter<THit, TTarget>
        {
            int hitCount = hitBuffer.Length;
            ReplayEventCounter.Value = hitCount;

            using (ReplayMarker.Auto(hitCount))
            {
                for (int i = 0; i < hitCount; i++)
                {
                    THit hit = hitBuffer[i];
                    targetsById.TryGetValue(adapter.TargetId(in hit), out TTarget target);
                    DamageSnapshot damage = adapter.RollDamage(in hit);
                    adapter.Replay(in hit, target, in damage);
                }
            }

            hitBuffer.Clear();
        }
    }
}

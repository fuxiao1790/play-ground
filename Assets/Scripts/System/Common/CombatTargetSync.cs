using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

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
}

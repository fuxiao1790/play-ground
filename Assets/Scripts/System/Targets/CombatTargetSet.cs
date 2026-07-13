using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Narrowphase;
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
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Combat.Targets
{
    public sealed class CombatTargetSet
    {
        private readonly List<ICombatTarget> targets = new();
        private readonly List<CombatTargetElement> snapshots = new();
        private readonly Dictionary<int, ICombatTarget> targetsById = new();

        public IReadOnlyList<CombatTargetElement> Snapshots => snapshots;
        public IReadOnlyDictionary<int, ICombatTarget> TargetsById => targetsById;
        public int SnapshotBuildCount { get; private set; }

        public void Register(ICombatTarget target)
        {
            if (target == null || targets.Contains(target))
            {
                return;
            }

            targets.Add(target);
        }

        public void Unregister(ICombatTarget target)
        {
            targets.Remove(target);
            targetsById.Remove(target?.TargetId ?? 0);
        }

        public void BuildSnapshot()
        {
            SnapshotBuildCount++;
            snapshots.Clear();
            targetsById.Clear();

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                ICombatTarget target = targets[i];
                if (IsDestroyed(target))
                {
                    targets.RemoveAt(i);
                    continue;
                }

                if (!target.IsCombatTargetActive)
                {
                    continue;
                }

                float2 position = new(target.CombatTargetPosition.x, target.CombatTargetPosition.y);
                float2 halfExtents = new(target.CombatTargetHalfExtents.x, target.CombatTargetHalfExtents.y);
                CombatCollisionMath.ComputeWorldBounds(
                    position,
                    target.CombatTargetRadius,
                    halfExtents,
                    target.CombatTargetRotationRadians,
                    target.CombatTargetShapeType,
                    out float2 boundsMin,
                    out float2 boundsMax);

                snapshots.Add(new CombatTargetElement
                {
                    TargetId = target.TargetId,
                    TargetMask = target.CombatTargetMask,
                    Position = position,
                    ShapeType = target.CombatTargetShapeType,
                    Radius = target.CombatTargetRadius,
                    HalfExtents = halfExtents,
                    RotationRadians = target.CombatTargetRotationRadians,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax
                });
                targetsById[target.TargetId] = target;
            }
        }

        private static bool IsDestroyed(ICombatTarget target)
        {
            return target == null
                || target is Object unityObject && unityObject == null;
        }
    }
}

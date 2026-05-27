using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public sealed class AoeTargetSync
    {
        private readonly AoeTargetRegistry registry;
        private readonly List<AoeTargetSnapshot> snapshots = new();
        private readonly Dictionary<int, IAoeTarget> targetsById = new();

        public AoeTargetSync(AoeTargetRegistry registry)
        {
            this.registry = registry;
        }

        public IReadOnlyDictionary<int, IAoeTarget> TargetsById => targetsById;

        public IReadOnlyList<AoeTargetSnapshot> Snapshot()
        {
            snapshots.Clear();
            targetsById.Clear();
            IReadOnlyList<IAoeTarget> targets = registry.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                IAoeTarget target = targets[i];
                if (target == null || !target.IsAoeTargetActive)
                {
                    continue;
                }

                snapshots.Add(new AoeTargetSnapshot(
                    target.TargetId,
                    target.AoeTargetMask,
                    target.AoeTargetPosition,
                    new AoeShape(
                        target.AoeTargetShapeType,
                        target.AoeTargetRadius,
                        target.AoeTargetHalfExtents,
                        target.AoeTargetRotationRadians)));
                targetsById[target.TargetId] = target;
            }

            return snapshots;
        }
    }
}

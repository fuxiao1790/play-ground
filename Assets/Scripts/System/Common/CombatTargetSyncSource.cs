using System;
using System.Collections.Generic;
using Unity.Entities;

namespace PlayGround.System.Common
{
    internal sealed class CombatTargetSyncSource : IComponentData
    {
        internal Action<DynamicBuffer<CombatTargetElement>> Sync;
    }

    // ECS Lifecycle: managed scope component object; added at root setup; kept until root teardown; updated when target snapshots are synced so common damage replay can resolve scene targets without projectile/AOE root callbacks.
    internal sealed class CombatDamageTargetSource : IComponentData
    {
        internal IReadOnlyDictionary<int, ICombatTarget> TargetsById;
    }
}

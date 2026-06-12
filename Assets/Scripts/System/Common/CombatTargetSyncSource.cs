using System;
using Unity.Entities;

namespace PlayGround.System.Common
{
    internal sealed class CombatTargetSyncSource : IComponentData
    {
        internal Action<DynamicBuffer<CombatTargetElement>> Sync;
    }
}

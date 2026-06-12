using System.Collections.Generic;
using Unity.Entities;

namespace PlayGround.System.Common
{
    internal interface ICombatScopeEndpoint
    {
        Entity ScopeEntity { get; }
        EntityManager EntityManager { get; }
        bool EnsureRuntimeAvailable();
        void SetCombatRuntimeManaged(bool managed);
        void WriteTargets(IReadOnlyList<CombatTargetElement> targets, IReadOnlyDictionary<int, ICombatTarget> targetsById);
        void PresentFromCombatRuntime();
    }
}

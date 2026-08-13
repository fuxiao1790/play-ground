using PlayGround.System.Combat.Targets;
using Unity.Entities;

namespace PlayGround.System.Combat.Presentation
{
    internal static class CombatTargetBridge
    {
        internal static ICombatTarget ResolveTarget(EntityManager entityManager, Entity targetProxy)
        {
            if (targetProxy == Entity.Null
                || !entityManager.Exists(targetProxy)
                || !entityManager.HasComponent<TargetCompanion>(targetProxy))
            {
                return null;
            }

            ICombatTarget target = entityManager
                .GetComponentObject<TargetCompanion>(targetProxy)
                ?.Target;
            return target is UnityEngine.Object unityObject && unityObject == null ? null : target;
        }
    }
}

using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Entities;

namespace PlayGround.System.Common
{
    // Writes CombatSpawnRouting onto scope entities, replacing the managed
    // CombatSpawnRouter faction wiring with ECS-visible routing. Public so both
    // GameRoot and tests can drive it (the underlying ICombatScopeEndpoint is
    // internal to the runtime assembly).
    public static class CombatSpawnRoutingBinder
    {
        // Pairs a projectile root with its sibling AOE root:
        //   projectile scope -> impact projectiles spawn into itself, impact AOEs
        //   into the AOE scope; AOE scope -> projectile bursts into the projectile scope.
        public static void Bind(ProjectileRoot projectileRoot, AoeRoot aoeRoot)
        {
            if (projectileRoot is not ICombatScopeEndpoint projectileScope
                || !projectileScope.EnsureRuntimeAvailable())
            {
                return;
            }

            Entity projectileEntity = projectileScope.ScopeEntity;
            EntityManager entityManager = projectileScope.EntityManager;

            Entity aoeEntity = Entity.Null;
            if (aoeRoot is ICombatScopeEndpoint aoeScope && aoeScope.EnsureRuntimeAvailable())
            {
                aoeEntity = aoeScope.ScopeEntity;
            }

            WriteRouting(entityManager, projectileEntity, projectileEntity, aoeEntity);

            if (aoeEntity != Entity.Null)
            {
                WriteRouting(entityManager, aoeEntity, projectileEntity, Entity.Null);
            }
        }

        private static void WriteRouting(
            EntityManager entityManager,
            Entity scope,
            Entity projectileDestination,
            Entity aoeDestination)
        {
            if (scope == Entity.Null || !entityManager.Exists(scope))
            {
                return;
            }

            var routing = new CombatSpawnRouting
            {
                ProjectileDestinationScope = projectileDestination,
                AoeDestinationScope = aoeDestination
            };

            if (entityManager.HasComponent<CombatSpawnRouting>(scope))
            {
                entityManager.SetComponentData(scope, routing);
            }
            else
            {
                entityManager.AddComponentData(scope, routing);
            }
        }
    }
}

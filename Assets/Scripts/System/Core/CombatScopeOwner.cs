using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Vfx;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Core
{
    // Ref-counted owner of the single shared combat scope entity. Mirrors
    // CombatEcsWorld's Acquire/Release pattern: the first CombatRoot to bind
    // creates the entity and its buffers; later CombatRoots (the other
    // faction) just get the same Entity back; the entity is destroyed only
    // when the last owner releases it.
    internal static class CombatScopeOwner
    {
        private const int InitialTemplateRegistryCapacity = 64;

        private static Entity ownedScope;
        private static World ownedWorld;
        private static int ownerCount;
        private static NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnCommand> ownedProjectileMap;
        private static NativeHashMap<Unity.Entities.Hash128, AoeSpawnCommand> ownedAoeMap;
        private static NativeHashMap<Unity.Entities.Hash128, TargetedSpawnCommand> ownedTargetedMap;

        public static Entity Acquire(EntityManager entityManager)
        {
            World world = entityManager.World;
            if (ownedWorld != world)
            {
                DisposeMaps();
                ownedScope = Entity.Null;
                ownedWorld = world;
                ownerCount = 0;
            }

            if (ownedScope == Entity.Null || !entityManager.Exists(ownedScope))
            {
                ownedProjectileMap = new NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnCommand>(
                    InitialTemplateRegistryCapacity, Allocator.Persistent);
                ownedAoeMap = new NativeHashMap<Unity.Entities.Hash128, AoeSpawnCommand>(
                    InitialTemplateRegistryCapacity, Allocator.Persistent);
                ownedTargetedMap = new NativeHashMap<Unity.Entities.Hash128, TargetedSpawnCommand>(
                    InitialTemplateRegistryCapacity, Allocator.Persistent);

                ownedScope = entityManager.CreateEntity(typeof(CombatScope));
                entityManager.AddBuffer<CombatSpawnRequest>(ownedScope);
                entityManager.AddBuffer<ExternalSpawnRequest>(ownedScope);
                entityManager.AddBuffer<ProjectileSpawnEvent>(ownedScope);
                entityManager.AddBuffer<ImpactAoeSpawnEvent>(ownedScope);
                entityManager.AddBuffer<LingeringAoeSpawnEvent>(ownedScope);
                entityManager.AddBuffer<TargetedSpawnEvent>(ownedScope);
                entityManager.AddBuffer<TargetProxyCreateEvent>(ownedScope);
                entityManager.AddBuffer<TargetProxyUpdateEvent>(ownedScope);
                entityManager.AddBuffer<TargetProxyDeleteEvent>(ownedScope);
                entityManager.AddComponentData(ownedScope, new ProjectileSpawnTemplate { Map = ownedProjectileMap });
                entityManager.AddComponentData(ownedScope, new AoeSpawnTemplate { Map = ownedAoeMap });
                entityManager.AddComponentData(ownedScope, new TargetedSpawnTemplate { Map = ownedTargetedMap });
                ownedWorld = world;
                ownerCount = 0;
            }

            ownerCount++;
            return ownedScope;
        }

        public static void Release(EntityManager entityManager, Entity scope)
        {
            if (scope == Entity.Null || scope != ownedScope || entityManager.World != ownedWorld)
            {
                return;
            }

            ownerCount = global::System.Math.Max(0, ownerCount - 1);
            if (ownerCount > 0)
            {
                return;
            }

            DisposeMaps();

            if (ownedWorld != null && ownedWorld.IsCreated && entityManager.Exists(ownedScope))
            {
                entityManager.DestroyEntity(ownedScope);
            }

            ownedScope = Entity.Null;
            ownedWorld = null;
        }

        // Called when the world is already disposed and entity access is not possible.
        // The maps are tracked as static refs so they can still be freed.
        public static void ReleaseAfterWorldDispose(World world, Entity scope)
        {
            if (scope == Entity.Null || scope != ownedScope || world != ownedWorld)
            {
                return;
            }

            ownerCount = global::System.Math.Max(0, ownerCount - 1);
            if (ownerCount > 0)
            {
                return;
            }

            DisposeMaps();
            ownedScope = Entity.Null;
            ownedWorld = null;
        }

        private static void DisposeMaps()
        {
            if (ownedProjectileMap.IsCreated)
            {
                ownedProjectileMap.Dispose();
            }

            if (ownedAoeMap.IsCreated)
            {
                ownedAoeMap.Dispose();
            }

            if (ownedTargetedMap.IsCreated)
            {
                ownedTargetedMap.Dispose();
            }

            ownedProjectileMap = default;
            ownedAoeMap = default;
            ownedTargetedMap = default;
        }
    }
}

using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Common
{
    internal static class CombatEcsWorld
    {
        private static World ownedWorld;
        private static int ownerCount;

        public static World Acquire()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                world = new World("PlayGround ECS World");
                World.DefaultGameObjectInjectionWorld = world;
                var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);
                ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
                ownedWorld = world;
                ownerCount = 0;
            }

            if (world == ownedWorld)
            {
                ownerCount++;
            }

            return world;
        }

        public static void Release(World world)
        {
            if (world == null || world != ownedWorld)
            {
                return;
            }

            ownerCount = global::System.Math.Max(0, ownerCount - 1);
            if (ownerCount > 0)
            {
                return;
            }

            if (World.DefaultGameObjectInjectionWorld == world)
            {
                World.DefaultGameObjectInjectionWorld = null;
            }

            if (world.IsCreated)
            {
                ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world);
                world.Dispose();
            }

            ownedWorld = null;
        }
    }

    // Ref-counted owner of the single shared combat scope entity. Mirrors
    // CombatEcsWorld's Acquire/Release pattern: the first CombatRoot to bind
    // creates the entity and its buffers; later CombatRoots (the other
    // faction) just get the same Entity back; the entity is destroyed only
    // when the last owner releases it.
    internal static class CombatScopeOwner
    {
        private const int InitialTemplateRegistryCapacity = 64;

        private static Entity ownedScope;
        private static int ownerCount;
        private static NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnCommand> ownedProjectileMap;
        private static NativeHashMap<Unity.Entities.Hash128, AoeSpawnCommand> ownedAoeMap;

        public static Entity Acquire(EntityManager entityManager)
        {
            if (ownedScope == Entity.Null || !entityManager.Exists(ownedScope))
            {
                ownedProjectileMap = new NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnCommand>(
                    InitialTemplateRegistryCapacity, Allocator.Persistent);
                ownedAoeMap = new NativeHashMap<Unity.Entities.Hash128, AoeSpawnCommand>(
                    InitialTemplateRegistryCapacity, Allocator.Persistent);

                ownedScope = entityManager.CreateEntity(typeof(CombatScope));
                entityManager.AddBuffer<ProjectileSpawnEvent>(ownedScope);
                entityManager.AddBuffer<ImpactAoeSpawnEvent>(ownedScope);
                entityManager.AddBuffer<LingeringAoeSpawnEvent>(ownedScope);
                entityManager.AddComponentData(ownedScope, new ProjectileSpawnTemplate { Map = ownedProjectileMap });
                entityManager.AddComponentData(ownedScope, new AoeSpawnTemplate { Map = ownedAoeMap });
                ownerCount = 0;
            }

            ownerCount++;
            return ownedScope;
        }

        public static void Release(EntityManager entityManager, Entity scope)
        {
            if (scope == Entity.Null || scope != ownedScope)
            {
                return;
            }

            ownerCount = global::System.Math.Max(0, ownerCount - 1);
            if (ownerCount > 0)
            {
                return;
            }

            DisposeMaps();

            if (entityManager.Exists(ownedScope))
            {
                entityManager.DestroyEntity(ownedScope);
            }

            ownedScope = Entity.Null;
        }

        // Called when the world is already disposed and entity access is not possible.
        // The maps are tracked as static refs so they can still be freed.
        public static void ReleaseAfterWorldDispose(Entity scope)
        {
            if (scope == Entity.Null || scope != ownedScope)
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

            ownedProjectileMap = default;
            ownedAoeMap = default;
        }
    }

    // ECS Lifecycle: common combat component; owned by domain entities; lifecycle is defined by each domain tag/scope.
    public struct CombatKinematicsComponent : IComponentData
    {
        public float2 Position;
        public float2 Velocity;
    }

    // ECS Lifecycle: common combat component; owned by domain entities; lifecycle is defined by each domain tag/scope.
    public struct CombatCollisionComponent : IComponentData
    {
        public CombatShapeType ShapeType;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public float2 BoundsMin;
        public float2 BoundsMax;
    }

    // Fire-time hit snapshot shared by all combat domains (projectile, AOE). Carries damage, crit, and stack data snapshotted at spawn time.
    public struct CombatHitPayload
    {
        public float DamageAmount;
        public float CritChance;
        public float CritMultiplier;
        public bool DirectDamageEnabled;
        public EntityId SourceNodeId;
        public StackEffectSnapshot StackEffect;
    }

    // ECS Lifecycle: enableable common occupancy flag; added to reusable combat entities at creation; enabled on spawn, disabled on despawn.
    public struct Active : IComponentData, IEnableableComponent
    {
    }

    // ECS Lifecycle: enableable timed-spawn data; present on projectiles and lingering AOEs; absent from impact AOEs.
    // Enabled only while interval children should emit. Enabling this requires enabled CombatLifetimeComponent.
    public struct TimedSpawnComponent : IComponentData, IEnableableComponent
    {
        public CombatFaction Faction;
        public int SourceId;
        public IntervalChildKind ChildKind;
        public Unity.Entities.Hash128 TemplateKey;
        public float IntervalSeconds;
        public float IntervalJitterSeconds;
        public int JitterSeed;
    }

    // ECS Lifecycle: timed-spawn state; present on projectiles and lingering AOEs; absent from impact AOEs; reset when timed spawn is enabled.
    public struct TimedSpawnStateComponent : IComponentData
    {
        public float CooldownRemaining;
        public int TickIndex;
    }

    // ECS Lifecycle: enableable common lifetime component; present and enabled on projectiles and lingering AOEs; absent from impact AOEs.
    // Presence is the impact-vs-lingering AOE discriminator. CombatLifetimeSystem counts it down and disables Active on expiry.
    public struct CombatLifetimeComponent : IComponentData, IEnableableComponent
    {
        public float Remaining;
    }

    // ECS Lifecycle: common scope buffer; owned by domain scope entities; lifecycle and clearing rules are defined by each domain.
    public struct CombatTargetElement : IBufferElementData
    {
        public CombatFaction Faction;
        public int TargetId;
        public int TargetMask;
        public float2 Position;
        public CombatShapeType ShapeType;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public float2 BoundsMin;
        public float2 BoundsMax;
    }
}

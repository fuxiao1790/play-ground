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

    // ECS Lifecycle: common combat component; owned by domain entities; lifecycle is defined by each domain tag/scope.
    public struct CombatHitComponent : IComponentData
    {
        public int TargetMask;
        public float DamageAmount;
        public bool DirectDamageEnabled;
        public EntityId SourceNodeId;
    }

    // ECS Lifecycle: common scope buffer; owned by domain scope entities; lifecycle and clearing rules are defined by each domain.
    public struct CombatTargetElement : IBufferElementData
    {
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

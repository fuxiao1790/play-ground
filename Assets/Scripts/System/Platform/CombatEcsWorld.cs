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
using PlayGround.System.Combat.Vfx;
using Unity.Entities;

namespace PlayGround.System.Combat.Platform
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
}

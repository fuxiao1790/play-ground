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
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Lifetime
{
    internal static class CombatDeathUtility
    {
        public static void Kill(EnabledRefRW<Active> active)
        {
            active.ValueRW = false;
        }

        public static void Kill(
            EnabledRefRW<Active> active,
            EnabledRefRW<ArmingTag> arming)
        {
            Kill(active);
            arming.ValueRW = false;
        }

        public static void Kill(
            EnabledRefRW<Active> active,
            EnabledRefRW<CombatCollisionActiveTag> collisionActive)
        {
            Kill(active);
            collisionActive.ValueRW = false;
        }

        public static void Kill(
            EnabledRefRW<Active> active,
            EnabledRefRW<CombatCollisionActiveTag> collisionActive,
            EnabledRefRW<ArmingTag> arming)
        {
            Kill(active, collisionActive);
            arming.ValueRW = false;
        }

        public static void Kill(
            EnabledRefRW<Active> active,
            EnabledRefRW<CombatCollisionActiveTag> collisionActive,
            EnabledRefRW<ArmingTag> arming,
            NativeQueue<CircularVfxSpawnRequest>.ParallelWriter circularVfxPending,
            NativeQueue<TimedCircularVfxSpawnRequest>.ParallelWriter timedCircularVfxPending,
            int expireVfxId,
            float2 position,
            float areaSize,
            in VfxTimingData timing)
        {
            Kill(active, collisionActive, arming);
            VfxEmit.Enqueue(
                expireVfxId,
                position,
                areaSize,
                timing,
                circularVfxPending,
                timedCircularVfxPending);
        }
    }
}

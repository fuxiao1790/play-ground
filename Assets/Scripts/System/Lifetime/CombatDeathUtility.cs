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
            NativeQueue<AoeVfxSpawnRequest>.ParallelWriter vfxPending,
            bool hasVfxWriter,
            int expireVfxId,
            float2 position,
            float areaSize)
        {
            Kill(active, collisionActive, arming);
            EnqueueExpireVfx(vfxPending, hasVfxWriter, expireVfxId, position, areaSize);
        }

        private static void EnqueueExpireVfx(
            NativeQueue<AoeVfxSpawnRequest>.ParallelWriter vfxPending,
            bool hasVfxWriter,
            int expireVfxId,
            float2 position,
            float areaSize)
        {
            if (!hasVfxWriter || expireVfxId <= 0)
            {
                return;
            }

            vfxPending.Enqueue(new AoeVfxSpawnRequest
            {
                VfxId = expireVfxId,
                Position = position,
                AreaSize = areaSize
            });
        }
    }
}

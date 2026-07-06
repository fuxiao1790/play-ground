using PlayGround.System.Vfx;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Common
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
            EnabledRefRW<ArmingTag> arming,
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxPending,
            bool hasVfxWriter,
            int typeId,
            float2 position,
            float areaSize)
        {
            Kill(active, arming);
            EnqueueExpireVfx(vfxPending, hasVfxWriter, typeId, position, areaSize);
        }

        public static void Kill(
            EnabledRefRW<Active> active,
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxPending,
            bool hasVfxWriter,
            int typeId,
            float2 position,
            float areaSize)
        {
            Kill(active);
            EnqueueExpireVfx(vfxPending, hasVfxWriter, typeId, position, areaSize);
        }

        public static void Kill(
            EnabledRefRW<Active> active,
            EnabledRefRW<CombatCollisionActiveTag> collisionActive,
            EnabledRefRW<ArmingTag> arming,
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxPending,
            bool hasVfxWriter,
            int typeId,
            float2 position,
            float areaSize)
        {
            Kill(active, collisionActive, arming);
            EnqueueExpireVfx(vfxPending, hasVfxWriter, typeId, position, areaSize);
        }

        public static void Kill(
            EnabledRefRW<Active> active,
            EnabledRefRW<CombatCollisionActiveTag> collisionActive,
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxPending,
            bool hasVfxWriter,
            int typeId,
            float2 position,
            float areaSize)
        {
            Kill(active, collisionActive);
            EnqueueExpireVfx(vfxPending, hasVfxWriter, typeId, position, areaSize);
        }

        private static void EnqueueExpireVfx(
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxPending,
            bool hasVfxWriter,
            int typeId,
            float2 position,
            float areaSize)
        {
            if (!hasVfxWriter)
            {
                return;
            }

            vfxPending.Enqueue(new VfxPendingSpawn
            {
                TypeId = typeId,
                Trigger = 2,
                Position = position,
                AreaSize = areaSize
            });
        }
    }
}

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Common
{
    // Dead code — no system schedules this job (projectile path removed T004, AoE path removed T006).
    // Struct retained for Task 007 to delete alongside CombatPendingSpawn.
    [BurstCompile]
    public struct CombatSpawnConvertJob : IJob
    {
        public Entity Scope;
        public NativeStream PendingSpawns;

        public void Execute()
        {
        }
    }
}

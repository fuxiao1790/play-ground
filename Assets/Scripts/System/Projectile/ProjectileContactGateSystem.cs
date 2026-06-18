using PlayGround.System.Common;
using Unity.Burst;
using Unity.Entities;

namespace PlayGround.System.Projectile
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileMovementSystem))]
    [UpdateBefore(typeof(ProjectileCollisionSystem))]
    public partial struct ProjectileContactGateSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new ProjectileContactGateJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active))]
        private partial struct ProjectileContactGateJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(DynamicBuffer<ProjectileContactGateElement> contactGates)
            {
                // Pass 1: subtract DeltaTime from all cooldowns (no branches — Burst can vectorize).
                for (int i = 0; i < contactGates.Length; i++)
                    contactGates.ElementAt(i).CooldownRemaining -= DeltaTime;

                // Pass 2: compact expired gates (structural mutation kept separate).
                for (int i = contactGates.Length - 1; i >= 0; i--)
                    if (contactGates[i].CooldownRemaining <= 0f)
                        contactGates.RemoveAt(i);
            }
        }
    }
}

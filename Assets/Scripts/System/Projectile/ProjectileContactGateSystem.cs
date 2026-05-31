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
        private partial struct ProjectileContactGateJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(DynamicBuffer<ProjectileContactGateElement> contactGates)
            {
                for (int i = contactGates.Length - 1; i >= 0; i--)
                {
                    ProjectileContactGateElement gate = contactGates[i];
                    gate.CooldownRemaining -= DeltaTime;
                    if (gate.CooldownRemaining <= 0f)
                    {
                        contactGates.RemoveAt(i);
                    }
                    else
                    {
                        contactGates[i] = gate;
                    }
                }
            }
        }
    }
}

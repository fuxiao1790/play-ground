using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using Unity.Burst;
using Unity.Entities;

namespace PlayGround.System.Combat.Projectiles
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
                // Pass 1: subtract DeltaTime from all cooldowns (no branches â€?Burst can vectorize).
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

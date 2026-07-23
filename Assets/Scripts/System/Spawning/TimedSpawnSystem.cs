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
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Spawning
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CombatLifetimeSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnExpansionSystem))]
    public partial struct TimedSpawnSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // The spawn lanes are created unconditionally by their expansion systems' OnCreate.
            // Read them directly: a missing lane is a broken world and must throw, not be skipped.
            RefRW<ProjectileSpawnEventSingleton> projectileLane =
                SystemAPI.GetSingletonRW<ProjectileSpawnEventSingleton>();
            RefRW<ImpactAoeSpawnEventSingleton> impactAoeLane =
                SystemAPI.GetSingletonRW<ImpactAoeSpawnEventSingleton>();
            RefRW<LingeringAoeSpawnEventSingleton> lingeringAoeLane =
                SystemAPI.GetSingletonRW<LingeringAoeSpawnEventSingleton>();

            JobHandle handle = new TimedSpawnJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ProjectileEventQueue = projectileLane.ValueRO.EventQueue.AsParallelWriter(),
                ImpactAoeEventQueue = impactAoeLane.ValueRO.EventQueue.AsParallelWriter(),
                LingeringAoeEventQueue = lingeringAoeLane.ValueRO.EventQueue.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);

            state.Dependency = handle;

            projectileLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(projectileLane.ValueRW.ProducerHandle, handle);
            impactAoeLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(impactAoeLane.ValueRW.ProducerHandle, handle);
            lingeringAoeLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(lingeringAoeLane.ValueRW.ProducerHandle, handle);
        }

        [BurstCompile]
        [WithAll(typeof(Active), typeof(CombatLifetimeComponent), typeof(TimedSpawnComponent))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct TimedSpawnJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventQueue;
            public NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter ImpactAoeEventQueue;
            public NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter LingeringAoeEventQueue;

            // Safety guards: a bad threshold stays positive and catch-up remains bounded.
            private const float MinEnergyThreshold = 1e-3f;
            private const int MaxTicksPerUpdate = 256;

            private void Execute(
                ref TimedSpawnStateComponent state,
                in TimedSpawnComponent spawn,
                in CombatKinematicsComponent kinematics,
                in CombatLifetimeComponent lifetime)
            {
                if (lifetime.Remaining <= 0f
                    || spawn.Faction == CombatFaction.None
                    || spawn.EnergyPerSecond <= 0f)
                {
                    return;
                }

                state.EnergyAccumulated += spawn.EnergyPerSecond * DeltaTime;
                int tickIndex = state.TickIndex;
                int ticksThisUpdate = 0;
                float threshold = ThresholdFor(spawn, tickIndex + 1);
                while (state.EnergyAccumulated >= threshold && ticksThisUpdate < MaxTicksPerUpdate)
                {
                    ticksThisUpdate++;
                    tickIndex++;
                    if (spawn.ChildKind == IntervalChildKind.ImpactAoe)
                    {
                        ImpactAoeEventQueue.Enqueue(new ImpactAoeSpawnEvent
                        {
                            Kind = IntervalChildKind.ImpactAoe,
                            TemplateKey = spawn.TemplateKey,
                            Position = kinematics.Position,
                            AimDirection = default,
                            Faction = spawn.Faction,
                            SourceId = spawn.SourceId,
                            JitterSeed = (uint)spawn.JitterSeed,
                            DeterministicIdTickIndex = tickIndex
                        });
                    }
                    else if (spawn.ChildKind == IntervalChildKind.LingeringAoe)
                    {
                        LingeringAoeEventQueue.Enqueue(new LingeringAoeSpawnEvent
                        {
                            Kind = IntervalChildKind.LingeringAoe,
                            TemplateKey = spawn.TemplateKey,
                            Position = kinematics.Position,
                            AimDirection = default,
                            Faction = spawn.Faction,
                            SourceId = spawn.SourceId,
                            JitterSeed = (uint)spawn.JitterSeed,
                            DeterministicIdTickIndex = tickIndex
                        });
                    }
                    else
                    {
                        ProjectileEventQueue.Enqueue(new ProjectileSpawnEvent
                        {
                            Kind = IntervalChildKind.Projectile,
                            TemplateKey = spawn.TemplateKey,
                            Position = kinematics.Position,
                            AimDirection = default,
                            Faction = spawn.Faction,
                            SourceId = spawn.SourceId,
                            JitterSeed = (uint)spawn.JitterSeed,
                            DeterministicIdTickIndex = tickIndex
                        });
                    }

                    state.EnergyAccumulated -= threshold;
                    threshold = ThresholdFor(spawn, tickIndex + 1);
                }

                state.TickIndex = tickIndex;
            }

            private static float ThresholdFor(in TimedSpawnComponent spawn, int tickIndex)
            {
                return math.max(
                    MinEnergyThreshold,
                    spawn.EnergyThreshold + DeterministicJitter(
                        spawn.SourceId,
                        spawn.JitterSeed,
                        tickIndex,
                        spawn.EnergyThresholdJitter));
            }

            private static float DeterministicJitter(
                int sourceId,
                int jitterSeed,
                int tickIndex,
                float maxOffsetSeconds)
            {
                if (maxOffsetSeconds <= 0f)
                {
                    return 0f;
                }

                unchecked
                {
                    uint hash = (uint)sourceId;
                    hash = (hash * 397u) ^ (uint)jitterSeed;
                    hash = (hash * 397u) ^ (uint)tickIndex;
                    hash *= 0x9E3779B9u;
                    hash ^= hash >> 16;
                    hash *= 0x7FEB352Du;
                    hash ^= hash >> 15;
                    hash *= 0x846CA68Bu;
                    hash ^= hash >> 16;
                    return ((hash & 0x00FFFFFFu) + 1u) / 16777217f * maxOffsetSeconds;
                }
            }
        }
    }
}

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
using PlayGround.System.Combat.Targeted;
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
    [UpdateBefore(typeof(TargetedSpawnExpansionSystem))]
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
            bool hasTargetedLane = SystemAPI.TryGetSingletonRW<TargetedSpawnEventSingleton>(out RefRW<TargetedSpawnEventSingleton> targetedLane);
            NativeQueue<TargetedSpawnEvent> fallbackTargetedQueue = hasTargetedLane
                ? default
                : new NativeQueue<TargetedSpawnEvent>(Allocator.TempJob);

            JobHandle handle = new TimedSpawnJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ProjectileEventQueue = projectileLane.ValueRO.EventQueue.AsParallelWriter(),
                ImpactAoeEventQueue = impactAoeLane.ValueRO.EventQueue.AsParallelWriter(),
                LingeringAoeEventQueue = lingeringAoeLane.ValueRO.EventQueue.AsParallelWriter(),
                HasTargetedLane = hasTargetedLane,
                TargetedEventQueue = hasTargetedLane
                    ? targetedLane.ValueRO.EventQueue.AsParallelWriter()
                    : fallbackTargetedQueue.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);

            state.Dependency = handle;
            if (!hasTargetedLane)
            {
                state.Dependency = fallbackTargetedQueue.Dispose(state.Dependency);
            }

            projectileLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(projectileLane.ValueRW.ProducerHandle, handle);
            impactAoeLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(impactAoeLane.ValueRW.ProducerHandle, handle);
            lingeringAoeLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(lingeringAoeLane.ValueRW.ProducerHandle, handle);
            if (hasTargetedLane)
            {
                targetedLane.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(targetedLane.ValueRW.ProducerHandle, handle);
            }
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
            public bool HasTargetedLane;
            public NativeQueue<TargetedSpawnEvent>.ParallelWriter TargetedEventQueue;

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
                float threshold = math.max(MinEnergyThreshold, spawn.EnergyThreshold);
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
                    else if (spawn.ChildKind == IntervalChildKind.Projectile)
                    {
                        ProjectileEventQueue.Enqueue(new ProjectileSpawnEvent
                        {
                            Kind = IntervalChildKind.Projectile,
                            TemplateKey = spawn.TemplateKey,
                            Position = kinematics.Position,
                            AimDirection = TravelDirectionFor(kinematics),
                            Faction = spawn.Faction,
                            SourceId = spawn.SourceId,
                            JitterSeed = (uint)spawn.JitterSeed,
                            DeterministicIdTickIndex = tickIndex
                        });
                    }
                    else if (spawn.ChildKind == IntervalChildKind.Targeted)
                    {
                        if (!HasTargetedLane)
                            throw new global::System.InvalidOperationException("Targeted timed-spawn lane is missing.");
                        TargetedEventQueue.Enqueue(new TargetedSpawnEvent
                        {
                            Kind = IntervalChildKind.Targeted,
                            TemplateKey = spawn.TemplateKey,
                            Position = kinematics.Position,
                            AcquireAnchor = kinematics.Position,
                            AimDirection = default,
                            Faction = spawn.Faction,
                            SourceId = spawn.SourceId,
                            JitterSeed = (uint)spawn.JitterSeed,
                            DeterministicIdTickIndex = tickIndex
                        });
                    }
                    else
                    {
                        throw new global::System.InvalidOperationException(
                            $"Unhandled interval child kind {spawn.ChildKind}.");
                    }

                    state.EnergyAccumulated -= threshold;
                }

                state.TickIndex = tickIndex;
            }

            // A stationary source (zero velocity) has no travel direction; the expansion
            // system reads a zero AimDirection as "fan the interval children in a nova"
            // instead of side-spraying relative to a direction that doesn't exist.
            private static float2 TravelDirectionFor(in CombatKinematicsComponent kinematics) =>
                math.lengthsq(kinematics.Velocity) > 0.0001f ? kinematics.Velocity : default;
        }
    }
}

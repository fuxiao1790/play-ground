using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Common
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
            var projectileExpansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            var impactAoeExpansion = state.World.GetExistingSystemManaged<ImpactAoeSpawnExpansionSystem>();
            var lingeringAoeExpansion = state.World.GetExistingSystemManaged<LingeringAoeSpawnExpansionSystem>();
            if (projectileExpansion == null && impactAoeExpansion == null && lingeringAoeExpansion == null)
            {
                return;
            }

            JobHandle handle = new TimedSpawnJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ProjectileEventQueue = projectileExpansion != null
                    ? projectileExpansion.EventQueue.AsParallelWriter()
                    : default,
                ImpactAoeEventQueue = impactAoeExpansion != null
                    ? impactAoeExpansion.EventQueue.AsParallelWriter()
                    : default,
                LingeringAoeEventQueue = lingeringAoeExpansion != null
                    ? lingeringAoeExpansion.EventQueue.AsParallelWriter()
                    : default,
                HasProjectileEventQueue = projectileExpansion != null,
                HasImpactAoeEventQueue = impactAoeExpansion != null,
                HasLingeringAoeEventQueue = lingeringAoeExpansion != null
            }.ScheduleParallel(state.Dependency);

            state.Dependency = handle;

            if (projectileExpansion != null)
            {
                projectileExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(projectileExpansion.ProducerHandle, handle);
            }

            if (impactAoeExpansion != null)
            {
                impactAoeExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(impactAoeExpansion.ProducerHandle, handle);
            }

            if (lingeringAoeExpansion != null)
            {
                lingeringAoeExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(lingeringAoeExpansion.ProducerHandle, handle);
            }
        }

        [BurstCompile]
        [WithAll(typeof(Active), typeof(CombatLifetimeComponent), typeof(TimedSpawnComponent))]
        private partial struct TimedSpawnJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventQueue;
            public NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter ImpactAoeEventQueue;
            public NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter LingeringAoeEventQueue;
            public bool HasProjectileEventQueue;
            public bool HasImpactAoeEventQueue;
            public bool HasLingeringAoeEventQueue;

            // Safety guards: a bad interval must advance by a positive amount and stop after a bounded catch-up.
            private const float MinIntervalSeconds = 1e-3f;
            private const int MaxTicksPerUpdate = 256;

            private void Execute(
                ref TimedSpawnStateComponent state,
                in TimedSpawnComponent spawn,
                in CombatKinematicsComponent kinematics,
                in CombatLifetimeComponent lifetime)
            {
                if (lifetime.Remaining <= 0f || spawn.Faction == CombatFaction.None)
                {
                    return;
                }

                float cooldown = state.CooldownRemaining - DeltaTime;
                int tickIndex = state.TickIndex;
                int ticksThisUpdate = 0;
                while (cooldown <= 0f && ticksThisUpdate < MaxTicksPerUpdate)
                {
                    ticksThisUpdate++;
                    tickIndex++;
                    if (spawn.ChildKind == IntervalChildKind.ImpactAoe)
                    {
                        if (HasImpactAoeEventQueue)
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
                    }
                    else if (spawn.ChildKind == IntervalChildKind.LingeringAoe)
                    {
                        if (HasLingeringAoeEventQueue)
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
                    }
                    else
                    {
                        if (HasProjectileEventQueue)
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
                    }

                    cooldown += math.max(MinIntervalSeconds, NextIntervalSeconds(
                        spawn.SourceId,
                        spawn.JitterSeed,
                        tickIndex,
                        spawn.IntervalSeconds,
                        spawn.IntervalJitterSeconds));
                }

                state.CooldownRemaining = cooldown;
                state.TickIndex = tickIndex;
            }

            private static float NextIntervalSeconds(
                int sourceId,
                int jitterSeed,
                int tickIndex,
                float intervalSeconds,
                float intervalJitterSeconds)
            {
                return intervalSeconds
                    + DeterministicJitter(sourceId, jitterSeed, tickIndex, intervalJitterSeconds);
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

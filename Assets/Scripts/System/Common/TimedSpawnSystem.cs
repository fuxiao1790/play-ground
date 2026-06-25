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
    [UpdateBefore(typeof(AoeSpawnExpansionSystem))]
    public partial struct TimedSpawnSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var projectileExpansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            var aoeExpansion = state.World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();
            if (projectileExpansion == null && aoeExpansion == null)
            {
                return;
            }

            bool hasProjectileTemplates = SystemAPI.TryGetSingleton(out ProjectileSpawnTemplate projectileTemplates);
            bool hasAoeTemplates = SystemAPI.TryGetSingleton(out AoeSpawnTemplate aoeTemplates);

            JobHandle handle = new TimedSpawnJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ProjectileEventQueue = projectileExpansion != null
                    ? projectileExpansion.EventQueue.AsParallelWriter()
                    : default,
                AoeEventQueue = aoeExpansion != null
                    ? aoeExpansion.EventQueue.AsParallelWriter()
                    : default,
                HasProjectileEventQueue = projectileExpansion != null,
                HasAoeEventQueue = aoeExpansion != null,
                ProjectileTemplates = hasProjectileTemplates ? projectileTemplates.Map : default,
                AoeTemplates = hasAoeTemplates ? aoeTemplates.Map : default,
                HasProjectileTemplates = hasProjectileTemplates,
                HasAoeTemplates = hasAoeTemplates
            }.ScheduleParallel(state.Dependency);

            state.Dependency = handle;

            if (projectileExpansion != null)
            {
                projectileExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(projectileExpansion.ProducerHandle, handle);
            }

            if (aoeExpansion != null)
            {
                aoeExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(aoeExpansion.ProducerHandle, handle);
            }
        }

        [BurstCompile]
        [WithAll(typeof(Active), typeof(CombatLifetimeComponent), typeof(TimedSpawnTag))]
        private partial struct TimedSpawnJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventQueue;
            public NativeQueue<AoeSpawnEvent>.ParallelWriter AoeEventQueue;
            [ReadOnly] public NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnEvent> ProjectileTemplates;
            [ReadOnly] public NativeHashMap<Unity.Entities.Hash128, AoeSpawnEvent> AoeTemplates;
            public bool HasProjectileEventQueue;
            public bool HasAoeEventQueue;
            public bool HasProjectileTemplates;
            public bool HasAoeTemplates;

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
                    if (spawn.ChildKind == IntervalChildKind.Aoe)
                    {
                        if (HasAoeEventQueue
                            && HasAoeTemplates
                            && AoeTemplates.TryGetValue(spawn.TemplateKey, out AoeSpawnEvent aoe))
                        {
                            Stamp(ref aoe, in spawn, in kinematics, tickIndex);
                            AoeEventQueue.Enqueue(aoe);
                        }
                    }
                    else
                    {
                        if (HasProjectileEventQueue
                            && HasProjectileTemplates
                            && ProjectileTemplates.TryGetValue(spawn.TemplateKey, out ProjectileSpawnEvent projectile))
                        {
                            Stamp(ref projectile, in spawn, in kinematics, tickIndex);
                            ProjectileEventQueue.Enqueue(projectile);
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

            private static void Stamp(
                ref ProjectileSpawnEvent evt,
                in TimedSpawnComponent spawn,
                in CombatKinematicsComponent kinematics,
                int tickIndex)
            {
                evt.Faction = spawn.Faction;
                evt.BaseProjectileId = spawn.SourceId;
                evt.Position = kinematics.Position;
                evt.JitterSeed = (uint)spawn.JitterSeed;
                evt.DeterministicIdTickIndex = tickIndex;
            }

            private static void Stamp(
                ref AoeSpawnEvent evt,
                in TimedSpawnComponent spawn,
                in CombatKinematicsComponent kinematics,
                int tickIndex)
            {
                evt.Faction = spawn.Faction;
                evt.AoeId = spawn.SourceId;
                evt.Position = kinematics.Position;
                evt.JitterSeed = (uint)spawn.JitterSeed;
                evt.DeterministicIdTickIndex = tickIndex;
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

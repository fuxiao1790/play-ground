using PlayGround.System.Common;
using PlayGround.System.Aoe;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CombatLifetimeSystem))]
    [UpdateBefore(typeof(ProjectileTrackingSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    public partial struct TimedProjectileSpawnSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var expansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            if (expansion == null)
            {
                return;
            }

            var aoeExpansion = state.World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();
            bool hasProjectileTemplates = SystemAPI.TryGetSingleton(out ProjectileSpawnTemplate projectileTemplates);
            bool hasAoeTemplates = SystemAPI.TryGetSingleton(out AoeSpawnTemplate aoeTemplates);

            JobHandle handle = new ProjectileChildSpawnEntityJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ProjectileEventQueue = expansion.EventQueue.AsParallelWriter(),
                AoeEventQueue = aoeExpansion != null ? aoeExpansion.EventQueue.AsParallelWriter() : default,
                HasAoeEventQueue = aoeExpansion != null,
                ProjectileTemplates = hasProjectileTemplates ? projectileTemplates.Map : default,
                AoeTemplates = hasAoeTemplates ? aoeTemplates.Map : default,
                HasProjectileTemplates = hasProjectileTemplates,
                HasAoeTemplates = hasAoeTemplates
            }.ScheduleParallel(state.Dependency);

            state.Dependency = handle;
            expansion.ProducerHandle = JobHandle.CombineDependencies(expansion.ProducerHandle, handle);
            if (aoeExpansion != null)
            {
                aoeExpansion.ProducerHandle = JobHandle.CombineDependencies(aoeExpansion.ProducerHandle, handle);
            }
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active), typeof(ProjectileChildSpawnerTag))]
        private partial struct ProjectileChildSpawnEntityJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventQueue;
            public NativeQueue<AoeSpawnEvent>.ParallelWriter AoeEventQueue;
            [ReadOnly] public NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnEvent> ProjectileTemplates;
            [ReadOnly] public NativeHashMap<Unity.Entities.Hash128, AoeSpawnEvent> AoeTemplates;
            public bool HasAoeEventQueue;
            public bool HasProjectileTemplates;
            public bool HasAoeTemplates;

            // Safety guards: the catch-up loop below advances cooldown by the per-tick
            // interval. If that interval ever resolves to <= 0 (bad/zero config reaching the
            // baked component), the loop would never terminate and hard-freeze the editor in
            // Burst. Clamp the advance to a positive minimum and hard-cap iterations per update.
            private const float MinIntervalSeconds = 1e-3f;
            private const int MaxTicksPerUpdate = 256;

            private void Execute(
                ref ProjectileChildSpawnStateComponent childSpawnState,
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatLifetimeComponent lifetime,
                in ProjectileChildSpawnerComponent spawner,
                in AoeIntervalSpawnerComponent aoeSpawner)
            {
                if (lifetime.Remaining <= 0f || identity.Faction == CombatFaction.None)
                {
                    return;
                }

                float cooldown = childSpawnState.ChildSpawnCooldownRemaining - DeltaTime;
                int tickIndex = childSpawnState.ChildSpawnTickIndex;
                int ticksThisUpdate = 0;
                while (cooldown <= 0f && ticksThisUpdate < MaxTicksPerUpdate)
                {
                    ticksThisUpdate++;
                    tickIndex++;
                    if (childSpawnState.ChildKind == IntervalChildKind.Aoe)
                    {
                        if (HasAoeEventQueue
                            && HasAoeTemplates
                            && AoeTemplates.TryGetValue(aoeSpawner.TemplateKey, out AoeSpawnEvent child))
                        {
                            EnqueueAoeChildSpawn(identity, kinematics, in aoeSpawner, in child, tickIndex);
                        }

                        cooldown += math.max(MinIntervalSeconds, NextIntervalSeconds(
                            identity.ProjectileId,
                            aoeSpawner.JitterSeed,
                            tickIndex,
                            aoeSpawner.IntervalSeconds,
                            aoeSpawner.IntervalJitterSeconds));
                    }
                    else
                    {
                        if (HasProjectileTemplates
                            && ProjectileTemplates.TryGetValue(spawner.TemplateKey, out ProjectileSpawnEvent child))
                        {
                            EnqueueProjectileChildSpawn(identity, kinematics, in spawner, in child, tickIndex);
                        }

                        cooldown += math.max(MinIntervalSeconds, NextIntervalSeconds(
                            identity.ProjectileId,
                            spawner.JitterSeed,
                            tickIndex,
                            spawner.IntervalSeconds,
                            spawner.IntervalJitterSeconds));
                    }
                }

                childSpawnState.ChildSpawnCooldownRemaining = cooldown;
                childSpawnState.ChildSpawnTickIndex = tickIndex;
            }

            private void EnqueueProjectileChildSpawn(
                ProjectileIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in ProjectileChildSpawnerComponent spawner,
                in ProjectileSpawnEvent child,
                int tickIndex)
            {
                float parentSpeed = math.length(parentKinematics.Velocity);
                float2 baseDirection = math.normalizesafe(parentKinematics.Velocity, new float2(1f, 0f));
                ProjectileSpawnEvent evt = child;
                evt.Faction = parentIdentity.Faction;
                evt.BaseProjectileId = parentIdentity.ProjectileId;
                evt.SeedContactGateTargetId = 0;
                evt.Position = parentKinematics.Position;
                evt.BaseDirection = baseDirection;
                evt.Speed = evt.Speed > 0f ? evt.Speed : parentSpeed;
                evt.Count = math.max(1, evt.Count);
                evt.JitterDegrees = 0f;
                evt.JitterSeed = (uint)spawner.JitterSeed;
                evt.DeterministicIdTickIndex = tickIndex;
                ProjectileEventQueue.Enqueue(evt);
            }

            private void EnqueueAoeChildSpawn(
                ProjectileIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in AoeIntervalSpawnerComponent spawner,
                in AoeSpawnEvent child,
                int tickIndex)
            {
                AoeSpawnEvent evt = child;
                evt.Faction = parentIdentity.Faction;
                evt.AoeId = parentIdentity.ProjectileId;
                evt.Position = parentKinematics.Position;
                evt.BoundsMin = default;
                evt.BoundsMax = default;
                evt.Count = math.max(1, evt.Count);
                evt.JitterSeed = (uint)spawner.JitterSeed;
                evt.DeterministicIdTickIndex = tickIndex;
                AoeEventQueue.Enqueue(evt);
            }

            private static float NextIntervalSeconds(
                int parentProjectileId,
                int jitterSeed,
                int tickIndex,
                float intervalSeconds,
                float intervalJitterSeconds)
            {
                return intervalSeconds
                    + DeterministicJitter(parentProjectileId, jitterSeed, tickIndex, intervalJitterSeconds);
            }

            private static float DeterministicJitter(
                int parentProjectileId,
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
                    uint hash = (uint)parentProjectileId;
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

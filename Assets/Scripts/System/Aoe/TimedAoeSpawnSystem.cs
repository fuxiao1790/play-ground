using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CombatLifetimeSystem))]
    [UpdateBefore(typeof(AoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    public partial struct TimedAoeSpawnSystem : ISystem
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

            JobHandle handle = new AoeIntervalSpawnJob
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
        [WithAll(typeof(AoeTag), typeof(Active), typeof(CombatLifetimeComponent), typeof(AoeIntervalSpawnerTag))]
        private partial struct AoeIntervalSpawnJob : IJobEntity
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

            // Safety guards: the catch-up loop below advances cooldown by the per-tick
            // interval. If that interval ever resolves to <= 0 (bad/zero config reaching the
            // baked component), the loop would never terminate and hard-freeze the editor in
            // Burst. Clamp the advance to a positive minimum and hard-cap iterations per update.
            private const float MinIntervalSeconds = 1e-3f;
            private const int MaxTicksPerUpdate = 256;

            private void Execute(
                ref AoeIntervalSpawnStateComponent state,
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatLifetimeComponent lifetime,
                in AoeIntervalSpawnerComponent spawner)
            {
                if (lifetime.Remaining <= 0f || identity.Faction == CombatFaction.None)
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
                    if (spawner.ChildKind == IntervalChildKind.Aoe)
                    {
                        if (HasAoeEventQueue
                            && HasAoeTemplates
                            && AoeTemplates.TryGetValue(spawner.TemplateKey, out AoeSpawnEvent child))
                        {
                            EnqueueAoeChildSpawn(identity, kinematics, in spawner, in child, tickIndex);
                        }
                    }
                    else
                    {
                        if (HasProjectileEventQueue
                            && HasProjectileTemplates
                            && ProjectileTemplates.TryGetValue(spawner.TemplateKey, out ProjectileSpawnEvent child))
                        {
                            EnqueueProjectileChildSpawn(identity, kinematics, in spawner, in child, tickIndex);
                        }
                    }

                    cooldown += math.max(MinIntervalSeconds, NextIntervalSeconds(
                        identity.AoeId,
                        spawner.JitterSeed,
                        tickIndex,
                        spawner.IntervalSeconds,
                        spawner.IntervalJitterSeconds));
                }

                state.CooldownRemaining = cooldown;
                state.TickIndex = tickIndex;
            }

            private void EnqueueProjectileChildSpawn(
                AoeIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in AoeIntervalSpawnerComponent spawner,
                in ProjectileSpawnEvent child,
                int tickIndex)
            {
                ProjectileSpawnEvent evt = child;
                evt.Faction = parentIdentity.Faction;
                evt.BaseProjectileId = parentIdentity.AoeId;
                evt.SeedContactGateTargetId = 0;
                evt.Position = parentKinematics.Position;
                evt.BaseDirection = new float2(1f, 0f);
                evt.Count = math.max(1, evt.Count);
                evt.SpreadDegrees = 0f;
                evt.JitterDegrees = 0f;
                evt.JitterSeed = (uint)spawner.JitterSeed;
                evt.SpawnPatternType = ProjectileChildSpawnPatternType.Radial;
                evt.DeterministicIdTickIndex = tickIndex;
                ProjectileEventQueue.Enqueue(evt);
            }

            private void EnqueueAoeChildSpawn(
                AoeIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in AoeIntervalSpawnerComponent spawner,
                in AoeSpawnEvent child,
                int tickIndex)
            {
                AoeSpawnEvent evt = child;
                evt.Faction = parentIdentity.Faction;
                evt.AoeId = parentIdentity.AoeId;
                evt.Position = parentKinematics.Position;
                evt.BoundsMin = default;
                evt.BoundsMax = default;
                evt.Count = math.max(1, evt.Count);
                evt.JitterSeed = (uint)spawner.JitterSeed;
                evt.DeterministicIdTickIndex = tickIndex;
                AoeEventQueue.Enqueue(evt);
            }

            private static float NextIntervalSeconds(
                int parentAoeId,
                int jitterSeed,
                int tickIndex,
                float intervalSeconds,
                float intervalJitterSeconds)
            {
                return intervalSeconds
                    + DeterministicJitter(parentAoeId, jitterSeed, tickIndex, intervalJitterSeconds);
            }

            private static float DeterministicJitter(
                int parentAoeId,
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
                    uint hash = (uint)parentAoeId;
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

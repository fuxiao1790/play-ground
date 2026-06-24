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
                HasAoeEventQueue = aoeExpansion != null
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
            public bool HasProjectileEventQueue;
            public bool HasAoeEventQueue;

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
                while (cooldown <= 0f)
                {
                    tickIndex++;
                    if (spawner.ChildKind == IntervalChildKind.Aoe)
                    {
                        int childCount = math.max(1, spawner.AoeChild.Count);
                        for (int childIndex = 0; childIndex < childCount; childIndex++)
                        {
                            EnqueueAoeChildSpawn(identity, kinematics, in spawner, tickIndex, childIndex);
                        }
                    }
                    else
                    {
                        int childCount = math.max(1, spawner.ProjectileChild.ChildCountPerTick);
                        for (int childIndex = 0; childIndex < childCount; childIndex++)
                        {
                            EnqueueProjectileChildSpawn(identity, kinematics, in spawner, tickIndex, childIndex, childCount);
                        }
                    }

                    cooldown += NextIntervalSeconds(
                        identity.AoeId,
                        spawner.SpawnerId,
                        tickIndex,
                        spawner.IntervalSeconds,
                        spawner.IntervalJitterSeconds);
                }

                state.CooldownRemaining = cooldown;
                state.TickIndex = tickIndex;
            }

            private void EnqueueProjectileChildSpawn(
                AoeIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in AoeIntervalSpawnerComponent spawner,
                int tickIndex,
                int childIndex,
                int childCount)
            {
                if (!HasProjectileEventQueue)
                {
                    return;
                }

                IntervalProjectileChild child = spawner.ProjectileChild;
                float2 direction = RadialDirection(childIndex, childCount);
                int childProjectileId = ChildId(parentIdentity.AoeId, spawner.SpawnerId, tickIndex, childIndex);

                var hitPayload = new ProjectileHitPayload(
                    new CombatHitPayload
                    {
                        DamageAmount = child.DamageAmount,
                        DirectDamageEnabled = child.DirectDamageEnabled,
                        SourceNodeId = child.SourceNodeId,
                        StackEffect = child.StackEffect
                    },
                    child.ImpactAoe,
                    child.ImpactProjectile);

                ProjectileEventQueue.Enqueue(new ProjectileSpawnEvent
                {
                    Faction = parentIdentity.Faction,
                    BaseProjectileId = childProjectileId,
                    TypeId = child.TypeId,
                    HasChildSpawner = 0,
                    SeedContactGateTargetId = 0,
                    Position = parentKinematics.Position,
                    BaseDirection = direction,
                    Speed = child.Speed,
                    Count = 1,
                    SpreadDegrees = 0f,
                    JitterDegrees = 0f,
                    JitterSeed = 0u,
                    PierceRemaining = child.PierceCount,
                    RepeatHitCooldownSeconds = child.RepeatHitCooldownSeconds,
                    Lifetime = child.Lifetime,
                    Radius = child.Radius,
                    RotationRadians = child.RotationRadians,
                    HalfExtents = child.HalfExtents,
                    ShapeType = child.ShapeType,
                    HitPayload = hitPayload,
                    Tracking = new ProjectileTrackingComponent
                    {
                        TrackingEnabled = child.TrackingEnabled,
                        TrackingTurnSpeedRadians = child.TrackingTurnSpeedRadians,
                        TrackingQueryCooldownRemaining = child.TrackingInitialQueryDelaySeconds,
                        TrackingQueryIntervalSeconds = child.TrackingQueryIntervalSeconds,
                        TrackedTargetId = 0,
                        TrackedTargetIndex = -1,
                        TrackedTargetPosition = default,
                        TrackingRandomState = 0
                    },
                    Render = new CombatRenderComponent
                    {
                        IsRenderable = 1,
                        AlignToVelocity = 1,
                        VisualScale = new float2(child.VisualScale, child.VisualScale),
                        VisualRotationSin = child.VisualRotationSin,
                        VisualRotationCos = child.VisualRotationCos,
                        RenderZ = 0f
                    }
                });
            }

            private void EnqueueAoeChildSpawn(
                AoeIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in AoeIntervalSpawnerComponent spawner,
                int tickIndex,
                int childIndex)
            {
                if (!HasAoeEventQueue)
                {
                    return;
                }

                IntervalAoeChild child = spawner.AoeChild;
                AoeEventQueue.Enqueue(new AoeSpawnEvent
                {
                    Faction = parentIdentity.Faction,
                    AoeId = ChildId(parentIdentity.AoeId, spawner.SpawnerId, tickIndex, childIndex),
                    TypeId = child.TypeId,
                    Lifetime = child.Lifetime,
                    RepeatHitCooldownSeconds = child.RepeatHitCooldownSeconds,
                    HitPayload = child.HitPayload,
                    AreaSize = child.AreaSize,
                    Radius = child.Radius,
                    RotationRadians = child.RotationRadians,
                    Position = parentKinematics.Position,
                    HalfExtents = child.HalfExtents,
                    BoundsMin = default,
                    BoundsMax = default,
                    ShapeType = child.ShapeType,
                    Render = child.Render,
                    ProjectileBurst = child.ProjectileBurst,
                    AoeSpawn = child.AoeSpawn
                });
            }

            private static float2 RadialDirection(int childIndex, int childCount)
            {
                if (childCount <= 1)
                {
                    return new float2(1f, 0f);
                }

                float radians = math.PI * 2f * childIndex / childCount;
                math.sincos(radians, out float s, out float c);
                return new float2(c, s);
            }

            private static int ChildId(int parentAoeId, int spawnerId, int tickIndex, int childIndex)
            {
                unchecked
                {
                    int hash = parentAoeId;
                    hash = (hash * 397) ^ spawnerId;
                    hash = (hash * 397) ^ tickIndex;
                    hash = (hash * 397) ^ childIndex;
                    hash &= int.MaxValue;
                    return hash == 0 ? 1 : hash;
                }
            }

            private static float NextIntervalSeconds(
                int parentAoeId,
                int spawnerId,
                int tickIndex,
                float intervalSeconds,
                float intervalJitterSeconds)
            {
                return intervalSeconds
                    + DeterministicJitter(parentAoeId, spawnerId, tickIndex, intervalJitterSeconds);
            }

            private static float DeterministicJitter(
                int parentAoeId,
                int spawnerId,
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
                    hash = (hash * 397u) ^ (uint)spawnerId;
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

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    // Timed child spawns create fully initialized child projectile entities through ECB.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileMovementSystem))]
    [UpdateBefore(typeof(ProjectileLifetimeSystem))]
    public partial struct ProjectileChildSpawnSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            state.Dependency = new ProjectileChildSpawnJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Ecb = ecb
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag), typeof(ProjectileChildSpawnerTag))]
        private partial struct ProjectileChildSpawnJob : IJobEntity
        {
            public float DeltaTime;
            public EntityCommandBuffer.ParallelWriter Ecb;

            private void Execute(
                [ChunkIndexInQuery] int chunkIndex,
                ref ProjectileChildSpawnStateComponent childSpawnState,
                in ProjectileIdentityComponent identity,
                in ProjectileKinematicsComponent kinematics,
                in ProjectileLifetimeComponent lifetime,
                in ProjectileHitComponent hit,
                in ProjectileChildSpawnerComponent spawner)
            {
                if (lifetime.RemainingLifetime <= 0f || identity.Scope == Entity.Null)
                {
                    return;
                }

                float cooldown = childSpawnState.ChildSpawnCooldownRemaining - DeltaTime;
                int tickIndex = childSpawnState.ChildSpawnTickIndex;
                while (cooldown <= 0f)
                {
                    tickIndex++;
                    for (int childIndex = 0; childIndex < spawner.ChildCountPerTick; childIndex++)
                    {
                        SpawnChild(chunkIndex, identity, kinematics, hit, in spawner, tickIndex, childIndex);
                    }
                    cooldown += spawner.IntervalSeconds;
                }

                childSpawnState.ChildSpawnCooldownRemaining = cooldown;
                childSpawnState.ChildSpawnTickIndex = tickIndex;
            }

            private void SpawnChild(
                int chunkIndex,
                ProjectileIdentityComponent parentIdentity,
                ProjectileKinematicsComponent parentKinematics,
                ProjectileHitComponent parentHit,
                in ProjectileChildSpawnerComponent spawner,
                int tickIndex,
                int childIndex)
            {
                float2 velocity = ComputeChildVelocity(parentKinematics, in spawner, childIndex);
                int targetMask = spawner.TargetMask != 0 ? spawner.TargetMask : parentHit.TargetMask;
                ProjectileHitPayload hitPayload = new(
                    parentHit.HitPayload.SourceNodeId,
                    spawner.DamageAmount,
                    spawner.DirectDamageEnabled);

                ProjectileCollisionMath.ComputeWorldBounds(
                    parentKinematics.Position,
                    spawner.Radius,
                    spawner.HalfExtents,
                    spawner.RotationRadians,
                    spawner.ShapeType,
                    out float2 boundsMin,
                    out float2 boundsMax);
                
                Entity child = Ecb.CreateEntity(chunkIndex);
                Ecb.AddComponent(chunkIndex, child, new ProjectileIdentityComponent
                {
                    Scope = parentIdentity.Scope,
                    ProjectileId = 0,
                    TypeId = spawner.TypeId
                });
                Ecb.AddComponent(chunkIndex, child, new ProjectileKinematicsComponent
                {
                    Position = parentKinematics.Position,
                    Velocity = velocity
                });
                Ecb.AddComponent(chunkIndex, child, new ProjectileCollisionComponent
                {
                    Radius = spawner.Radius,
                    HalfExtents = spawner.HalfExtents,
                    RotationRadians = spawner.RotationRadians,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax,
                    ShapeType = spawner.ShapeType
                });
                Ecb.AddComponent(chunkIndex, child, new ProjectileLifetimeComponent
                {
                    RemainingLifetime = spawner.Lifetime
                });
                Ecb.AddComponent(chunkIndex, child, new ProjectileHitComponent
                {
                    TargetMask = targetMask,
                    HitPayload = hitPayload,
                    PierceRemaining = spawner.PierceCount,
                    RepeatHitCooldownSeconds = spawner.RepeatHitCooldownSeconds
                });
                Ecb.AddComponent(chunkIndex, child, new ProjectileTrackingComponent
                {
                    TrackingEnabled = spawner.TrackingEnabled,
                    TrackingRangeSquared = spawner.TrackingRangeSquared,
                    TrackingTurnSpeedRadians = spawner.TrackingTurnSpeedRadians,
                    TrackingQueryCooldownRemaining = spawner.TrackingInitialQueryDelaySeconds,
                    TrackingQueryIntervalSeconds = spawner.TrackingQueryIntervalSeconds,
                    TrackedTargetId = 0,
                    TrackedTargetIndex = -1
                });
                Ecb.AddComponent(chunkIndex, child, new ProjectileRenderComponent
                {
                    IsRenderable = 1,
                    VisualScale = spawner.VisualScale,
                    VisualRotationSin = spawner.VisualRotationSin,
                    VisualRotationCos = spawner.VisualRotationCos
                });
                Ecb.AddComponent<ProjectileActiveTag>(chunkIndex, child);
                Ecb.SetComponentEnabled<ProjectileActiveTag>(chunkIndex, child, true);
                Ecb.AddBuffer<ProjectileContactGateElement>(chunkIndex, child);
                AddRenderTypeTag(chunkIndex, child, spawner.TypeId);
            }

            private static float2 ComputeChildVelocity(
                ProjectileKinematicsComponent parentKinematics,
                in ProjectileChildSpawnerComponent spawner,
                int childIndex)
            {
                float2 forward = math.normalizesafe(parentKinematics.Velocity, new float2(1f, 0f));
                float2 dir;
                switch (spawner.SpawnPatternType)
                {
                    case ProjectileChildSpawnPatternType.SideSpray:
                    {
                        // Mirror of ProjectileSideSpraySpawnPattern:
                        // even childIndex → left side, odd → right side.
                        // Each side fans its shots across SideSpreadDegrees.
                        float2 left  = new float2(-forward.y,  forward.x);
                        float2 right = new float2( forward.y, -forward.x);
                        bool isLeft  = (childIndex & 1) == 0;
                        int leftCount  = (spawner.ChildCountPerTick + 1) / 2;
                        int rightCount =  spawner.ChildCountPerTick / 2;
                        int sideIndex  = childIndex / 2;
                        int sideCount  = isLeft ? leftCount : rightCount;
                        float2 sideDir = isLeft ? left : right;
                        float spreadRad = math.radians(spawner.SideSpreadDegrees);
                        float angle = SideSpreadAngle(spreadRad, sideIndex, sideCount);
                        dir = Rotate(sideDir, angle);
                        break;
                    }
                    default: // Forward
                        dir = forward;
                        break;
                }
                return spawner.Speed > 0f ? dir * spawner.Speed : dir * math.length(parentKinematics.Velocity);
            }

            private static float SideSpreadAngle(float totalRad, int shotIndex, int shotCount)
            {
                if (shotCount <= 1) return 0f;
                return -totalRad * 0.5f + totalRad / (shotCount - 1) * shotIndex;
            }

            private static float2 Rotate(float2 v, float radians)
            {
                math.sincos(radians, out float s, out float c);
                return new float2(c * v.x - s * v.y, s * v.x + c * v.y);
            }

            private void AddRenderTypeTag(int chunkIndex, Entity child, int typeId)
            {
                switch (typeId)
                {
                    case 0:  Ecb.AddComponent<ProjectileRenderType0Tag>(chunkIndex, child);  break;
                    case 1:  Ecb.AddComponent<ProjectileRenderType1Tag>(chunkIndex, child);  break;
                    case 2:  Ecb.AddComponent<ProjectileRenderType2Tag>(chunkIndex, child);  break;
                    case 3:  Ecb.AddComponent<ProjectileRenderType3Tag>(chunkIndex, child);  break;
                    case 4:  Ecb.AddComponent<ProjectileRenderType4Tag>(chunkIndex, child);  break;
                    case 5:  Ecb.AddComponent<ProjectileRenderType5Tag>(chunkIndex, child);  break;
                    case 6:  Ecb.AddComponent<ProjectileRenderType6Tag>(chunkIndex, child);  break;
                    case 7:  Ecb.AddComponent<ProjectileRenderType7Tag>(chunkIndex, child);  break;
                    case 8:  Ecb.AddComponent<ProjectileRenderType8Tag>(chunkIndex, child);  break;
                    case 9:  Ecb.AddComponent<ProjectileRenderType9Tag>(chunkIndex, child);  break;
                    case 10: Ecb.AddComponent<ProjectileRenderType10Tag>(chunkIndex, child); break;
                    case 11: Ecb.AddComponent<ProjectileRenderType11Tag>(chunkIndex, child); break;
                    case 12: Ecb.AddComponent<ProjectileRenderType12Tag>(chunkIndex, child); break;
                    case 13: Ecb.AddComponent<ProjectileRenderType13Tag>(chunkIndex, child); break;
                    case 14: Ecb.AddComponent<ProjectileRenderType14Tag>(chunkIndex, child); break;
                    case 15: Ecb.AddComponent<ProjectileRenderType15Tag>(chunkIndex, child); break;
                }
            }
        }
    }
}

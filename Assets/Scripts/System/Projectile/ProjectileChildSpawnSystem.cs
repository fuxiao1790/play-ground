using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
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
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileChildSpawnJob : IJobEntity
        {
            public float DeltaTime;
            public EntityCommandBuffer.ParallelWriter Ecb;

            private void Execute([ChunkIndexInQuery] int chunkIndex, ref ProjectileComponent projectile)
            {
                if (projectile.RemainingLifetime <= 0f
                    || projectile.Scope == Entity.Null
                    || !projectile.ChildSpawnerConfig.IsCreated)
                {
                    return;
                }

                ref ProjectileChildSpawnerBlob config = ref projectile.ChildSpawnerConfig.Value;
                float cooldown = projectile.ChildSpawnCooldownRemaining - DeltaTime;
                int tickIndex = projectile.ChildSpawnTickIndex;
                while (cooldown <= 0f)
                {
                    tickIndex++;
                    for (int childIndex = 0; childIndex < config.ChildCountPerTick; childIndex++)
                    {
                        SpawnChild(chunkIndex, ref projectile, ref config, tickIndex, childIndex);
                    }
                    cooldown += config.IntervalSeconds;
                }

                projectile.ChildSpawnCooldownRemaining = cooldown;
                projectile.ChildSpawnTickIndex = tickIndex;
            }

            private void SpawnChild(
                int chunkIndex,
                ref ProjectileComponent parent,
                ref ProjectileChildSpawnerBlob config,
                int tickIndex,
                int childIndex)
            {
                float2 velocity = ComputeChildVelocity(ref parent, ref config, childIndex);
                int targetMask = config.TargetMask != 0 ? config.TargetMask : parent.TargetMask;

                ProjectileCollisionMath.ComputeWorldBounds(
                    parent.Position,
                    config.Radius,
                    config.HalfExtents,
                    config.RotationRadians,
                    config.ShapeType,
                    out float2 boundsMin,
                    out float2 boundsMax);

                Entity child = Ecb.CreateEntity(chunkIndex);
                Ecb.AddComponent(chunkIndex, child, new ProjectileComponent
                {
                    Scope = parent.Scope,
                    ProjectileId = 0, // assigned by ProjectileRoot.DrainChildSpawnRequests after ECB playback
                    TypeId = config.TypeId,
                    TargetMask = targetMask,
                    Position = parent.Position,
                    Velocity = velocity,
                    Radius = config.Radius,
                    HalfExtents = config.HalfExtents,
                    RotationRadians = config.RotationRadians,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax,
                    RemainingLifetime = config.Lifetime,
                    DamageAmount = config.DamageAmount,
                    DirectDamageEnabled = config.DirectDamageEnabled,
                    ShapeType = config.ShapeType,
                    PierceRemaining = config.PierceCount,
                    RepeatHitCooldownSeconds = config.RepeatHitCooldownSeconds,
                    TrackingEnabled = config.TrackingEnabled,
                    TrackingRangeSquared = config.TrackingRangeSquared,
                    TrackingTurnSpeedRadians = config.TrackingTurnSpeedRadians,
                    TrackingQueryCooldownRemaining = config.TrackingInitialQueryDelaySeconds,
                    TrackingQueryIntervalSeconds = config.TrackingQueryIntervalSeconds,
                    TrackedTargetId = 0,
                    TrackedTargetIndex = -1,
                    ChildSpawnerConfig = default,
                    ChildSpawnCooldownRemaining = 0f,
                    ChildSpawnTickIndex = 0
                });
                Ecb.AddComponent(chunkIndex, child, new ProjectileRenderComponent
                {
                    IsRenderable = 1,
                    VisualScale = config.VisualScale,
                    VisualRotationSin = config.VisualRotationSin,
                    VisualRotationCos = config.VisualRotationCos
                });
                Ecb.AddComponent(chunkIndex, child, new ProjectileChildSpawnedComponent
                {
                    Scope = parent.Scope,
                    ParentProjectileId = parent.ProjectileId,
                    ParentProjectileTypeId = parent.TypeId,
                    SpawnerId = config.SpawnerId,
                    TickIndex = tickIndex
                });
                Ecb.AddComponent<ProjectileActiveTag>(chunkIndex, child);
                Ecb.SetComponentEnabled<ProjectileActiveTag>(chunkIndex, child, true);
                Ecb.AddBuffer<ProjectileContactGateElement>(chunkIndex, child);
                AddRenderTypeTag(chunkIndex, child, config.TypeId);
            }

            private static float2 ComputeChildVelocity(
                ref ProjectileComponent parent,
                ref ProjectileChildSpawnerBlob config,
                int childIndex)
            {
                float2 forward = math.normalizesafe(parent.Velocity, new float2(1f, 0f));
                float2 dir;
                switch (config.SpawnPatternType)
                {
                    case ProjectileChildSpawnPatternType.SideSpray:
                    {
                        // Mirror of ProjectileSideSpraySpawnPattern:
                        // even childIndex → left side, odd → right side.
                        // Each side fans its shots across SideSpreadDegrees.
                        float2 left  = new float2(-forward.y,  forward.x);
                        float2 right = new float2( forward.y, -forward.x);
                        bool isLeft  = (childIndex & 1) == 0;
                        int leftCount  = (config.ChildCountPerTick + 1) / 2;
                        int rightCount =  config.ChildCountPerTick / 2;
                        int sideIndex  = childIndex / 2;
                        int sideCount  = isLeft ? leftCount : rightCount;
                        float2 sideDir = isLeft ? left : right;
                        float spreadRad = math.radians(config.SideSpreadDegrees);
                        float angle = SideSpreadAngle(spreadRad, sideIndex, sideCount);
                        dir = Rotate(sideDir, angle);
                        break;
                    }
                    default: // Forward
                        dir = forward;
                        break;
                }
                return config.Speed > 0f ? dir * config.Speed : dir * math.length(parent.Velocity);
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

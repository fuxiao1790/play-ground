using PlayGround.Common;
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
    [UpdateAfter(typeof(CombatApplyFinalizeSystem))]
    [UpdateBefore(typeof(AoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    public partial class StatusProcessSystem : SystemBase
    {
        private const int MaxTargetStackEntries = 32;

        private EntityQuery targetStackQuery;
        private int nextAoeId = 1;
        private int nextProjectileDetonationSourceId = 1;

        protected override void OnCreate()
        {
            targetStackQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TargetPosition>(),
                ComponentType.ReadWrite<TargetStackEntry>());
        }

        protected override void OnDestroy()
        {
            targetStackQuery.Dispose();
        }

        protected override void OnUpdate()
        {
            int entityCount = targetStackQuery.CalculateEntityCount();
            if (entityCount == 0)
            {
                return;
            }

            AoeSpawnExpansionSystem aoeExpansion = World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();
            ProjectileSpawnExpansionSystem projectileExpansion =
                World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            CombatApplyFinalizeSystem hitApply = World.GetExistingSystemManaged<CombatApplyFinalizeSystem>();

            int aoeIdBase = ReserveIdBlock(ref nextAoeId, entityCount);
            int projectileIdBase = ReserveIdBlock(ref nextProjectileDetonationSourceId, entityCount);

            // This job writes the expansion EventQueues via ParallelWriter, the same queues
            // other producers (TimedProjectileSpawnSystem, collision systems) already wrote
            // this frame. Those writes are tracked in each expansion system's ProducerHandle,
            // not in this SystemBase's component-derived Dependency, so we must depend on them
            // explicitly or the job-safety system rejects the schedule.
            JobHandle producerDeps = Dependency;
            if (aoeExpansion != null)
                producerDeps = JobHandle.CombineDependencies(producerDeps, aoeExpansion.ProducerHandle);
            if (projectileExpansion != null)
                producerDeps = JobHandle.CombineDependencies(producerDeps, projectileExpansion.ProducerHandle);

            JobHandle statusHandle = new StatusProcessJob
            {
                DeltaTime = math.max(0f, SystemAPI.Time.DeltaTime),
                AccrualFrame = hitApply != null ? hitApply.AccrualFrame : 0,
                AoeIdBase = aoeIdBase,
                ProjectileDetonationSourceIdBase = projectileIdBase,
                AoeEventWriter = aoeExpansion != null
                    ? aoeExpansion.EventQueue.AsParallelWriter()
                    : default,
                HasAoeEventWriter = aoeExpansion != null && aoeExpansion.EventQueue.IsCreated,
                ProjectileEventWriter = projectileExpansion != null
                    ? projectileExpansion.EventQueue.AsParallelWriter()
                    : default,
                HasProjectileEventWriter = projectileExpansion != null && projectileExpansion.EventQueue.IsCreated
            }.ScheduleParallel(targetStackQuery, producerDeps);

            if (aoeExpansion != null)
            {
                aoeExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(aoeExpansion.ProducerHandle, statusHandle);
            }

            if (projectileExpansion != null)
            {
                projectileExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(projectileExpansion.ProducerHandle, statusHandle);
            }

            Dependency = statusHandle;
        }

        private static int ReserveIdBlock(ref int nextId, int entityCount)
        {
            int blockSize = math.max(1, entityCount * MaxTargetStackEntries);
            if (nextId <= 0 || nextId > int.MaxValue - blockSize)
            {
                nextId = 1;
            }

            int baseId = nextId;
            nextId += blockSize;
            return baseId;
        }

        [BurstCompile]
        private partial struct StatusProcessJob : IJobEntity
        {
            public float DeltaTime;
            public int AccrualFrame;
            public int AoeIdBase;
            public int ProjectileDetonationSourceIdBase;
            public NativeQueue<AoeSpawnEvent>.ParallelWriter AoeEventWriter;
            public bool HasAoeEventWriter;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;
            public bool HasProjectileEventWriter;

            private void Execute(
                [EntityIndexInQuery] int entityIndexInQuery,
                in TargetPosition targetPosition,
                DynamicBuffer<TargetStackEntry> stackEntries)
            {
                int localDetonationIndex = 0;
                for (int i = stackEntries.Length - 1; i >= 0; i--)
                {
                    TargetStackEntry entry = stackEntries[i];
                    if (DeltaTime > 0f && entry.LastAccruedFrame != AccrualFrame)
                    {
                        entry.LifetimeRemaining = math.max(0f, entry.LifetimeRemaining - DeltaTime);
                    }

                    if (entry.LifetimeRemaining <= 0f)
                    {
                        stackEntries.RemoveAt(i);
                        continue;
                    }

                    int threshold = math.max(1, entry.Threshold);
                    if (entry.Count >= threshold)
                    {
                        int localId = entityIndexInQuery * MaxTargetStackEntries + localDetonationIndex;
                        BuildDetonationSpawn(entry, targetPosition.Value, localId);
                        stackEntries.RemoveAt(i);
                        localDetonationIndex++;
                        continue;
                    }

                    stackEntries[i] = entry;
                }
            }

            private void BuildDetonationSpawn(
                in TargetStackEntry entry,
                float2 position,
                int localId)
            {
                DetonationSnapshot snapshot = entry.Detonation;
                switch (snapshot.Kind)
                {
                    case StackDetonationKind.Aoe:
                        if (HasAoeEventWriter)
                        {
                            AoeEventWriter.Enqueue(BuildAoeSpawnEvent(entry, position, AoeIdBase + localId));
                        }

                        return;
                    case StackDetonationKind.Projectile:
                        if (HasProjectileEventWriter && snapshot.ProjectileBurst.Enabled)
                        {
                            ProjectileEventWriter.Enqueue(BuildProjectileDetonation(
                                entry,
                                position,
                                ProjectileDetonationSourceIdBase + localId));
                        }

                        return;
                }
            }

            private static AoeSpawnEvent BuildAoeSpawnEvent(
                in TargetStackEntry entry,
                float2 position,
                int aoeId)
            {
                DetonationSnapshot detonation = entry.Detonation;
                AoeSpawnGeometry geometry = detonation.AoeGeometry;
                float areaScale = geometry.AreaSize > 0f && entry.SummedArea > 0f
                    ? entry.SummedArea / geometry.AreaSize
                    : 1f;
                float radius = geometry.Radius * areaScale;
                float2 halfExtents = new(geometry.HalfExtents.x * areaScale, geometry.HalfExtents.y * areaScale);
                CombatCollisionMath.ComputeWorldBounds(
                    position,
                    radius,
                    halfExtents,
                    geometry.RotationRadians,
                    geometry.ShapeType,
                    out float2 boundsMin,
                    out float2 boundsMax);

                return new AoeSpawnEvent
                {
                    Faction = detonation.Faction,
                    AoeId = aoeId,
                    TypeId = detonation.TypeId,
                    Lifetime = math.max(0f, detonation.LifetimeSeconds),
                    RepeatHitCooldownSeconds = math.max(0f, detonation.TickIntervalSeconds),
                    HitPayload = new CombatHitPayload
                    {
                        DamageAmount = math.max(0f, entry.SummedDamage),
                        CritChance = detonation.CritChance,
                        CritMultiplier = detonation.CritMultiplier,
                        DirectDamageEnabled = true,
                        SourceNodeId = default,
                        StackEffect = default
                    },
                    AreaSize = entry.SummedArea > 0f ? entry.SummedArea : geometry.AreaSize,
                    Radius = radius,
                    RotationRadians = geometry.RotationRadians,
                    Position = position,
                    HalfExtents = halfExtents,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax,
                    ShapeType = geometry.ShapeType,
                    Render = RenderFor(geometry, areaScale),
                    ProjectileBurst = default,
                    AoeSpawn = detonation.AoeOnHitSpawn
                };
            }

            private static ProjectileSpawnEvent BuildProjectileDetonation(
                in TargetStackEntry entry,
                float2 position,
                int sourceId)
            {
                DetonationSnapshot detonation = entry.Detonation;
                AoeProjectileBurstSnapshot burst = detonation.ProjectileBurst;
                int count = math.max(1, entry.SummedProjectileCount);
                float totalDamage = math.max(0f, entry.SummedDamage);
                int baseId = HashId(sourceId, detonation.TypeId, 0, 0x7AB025);

                return new ProjectileSpawnEvent
                {
                    Faction = detonation.Faction,
                    TypeId = burst.ProjectileTypeId,
                    BaseProjectileId = baseId,
                    HasChildSpawner = 0,
                    SeedContactGateTargetId = 0,
                    Position = position,
                    BaseDirection = new float2(1f, 0f),
                    Speed = burst.Speed,
                    Count = count,
                    SpreadDegrees = burst.SpreadDegrees,
                    JitterDegrees = 0f,
                    JitterSeed = (uint)baseId * 2654435761u,
                    PierceRemaining = burst.PierceCount,
                    RepeatHitCooldownSeconds = burst.RepeatHitCooldownSeconds,
                    Lifetime = burst.LifetimeSeconds,
                    Radius = burst.Radius,
                    RotationRadians = burst.RotationRadians,
                    HalfExtents = new float2(burst.HalfExtents.x, burst.HalfExtents.y),
                    ShapeType = burst.ShapeType,
                    HitPayload = new ProjectileHitPayload(new CombatHitPayload
                    {
                        DamageAmount = totalDamage / count,
                        CritChance = 0f,
                        CritMultiplier = 1.5f,
                        DirectDamageEnabled = burst.DirectDamageEnabled,
                        SourceNodeId = default,
                        StackEffect = default
                    }),
                    Tracking = default,
                    Render = ProjectileRender(burst.VisualScale, burst.VisualRotationDegrees, baseId),
                    ChildSpawner = default,
                    ChildSpawnState = default
                };
            }

            private static CombatRenderComponent RenderFor(in AoeSpawnGeometry geometry, float areaScale)
            {
                if (geometry.VisualScale.x <= 0f && geometry.VisualScale.y <= 0f)
                {
                    return default;
                }

                return new CombatRenderComponent
                {
                    IsRenderable = 1,
                    AlignToVelocity = 0,
                    VisualScale = new float2(geometry.VisualScale.x * areaScale, geometry.VisualScale.y * areaScale),
                    VisualRotationSin = geometry.VisualRotationSin,
                    VisualRotationCos = geometry.VisualRotationCos,
                    RenderZ = CombatRoot.AoeRenderZ
                };
            }

            private static CombatRenderComponent ProjectileRender(float visualScale, float visualRotationDegrees, int projectileId)
            {
                if (visualScale <= 0f)
                {
                    return default;
                }

                math.sincos(math.radians(visualRotationDegrees), out float sin, out float cos);
                return new CombatRenderComponent
                {
                    IsRenderable = 1,
                    AlignToVelocity = 1,
                    VisualScale = new float2(visualScale, visualScale),
                    VisualRotationSin = sin,
                    VisualRotationCos = cos,
                    RenderZ = CombatRoot.ProjectileRenderZ
                        - (projectileId % CombatRoot.ProjectileRenderZSlots) * CombatRoot.ProjectileRenderZStep
                };
            }

            private static int HashId(int a, int b, int c, int salt)
            {
                unchecked
                {
                    int hash = salt;
                    hash = (hash * 397) ^ a;
                    hash = (hash * 397) ^ b;
                    hash = (hash * 397) ^ c;
                    hash &= int.MaxValue;
                    return hash == 0 ? 1 : hash;
                }
            }
        }
    }
}

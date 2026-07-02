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
    [UpdateAfter(typeof(CombatApplyFinalizeSingleSystem))]
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
            CombatApplyFinalizeSingleSystem hitApply = World.GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>();

            int aoeIdBase = ReserveIdBlock(ref nextAoeId, entityCount);
            int projectileIdBase = ReserveIdBlock(ref nextProjectileDetonationSourceId, entityCount);

            // This job writes the expansion EventQueues via ParallelWriter, the same queues
            // other producers (TimedSpawnSystem, collision systems) already wrote
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
                Entity targetEntity,
                [EntityIndexInQuery] int entityIndexInQuery,
                in TargetPosition targetPosition,
                DynamicBuffer<TargetStackEntry> stackEntries)
            {
                int targetKey = TargetKey(targetEntity);
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
                        BuildDetonationSpawn(entry, targetPosition.Value, localId, targetKey);
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
                int localId,
                int targetKey)
            {
                DetonationSnapshot snapshot = entry.Detonation;
                switch (snapshot.Kind)
                {
                    case StackDetonationKind.Aoe:
                        if (HasAoeEventWriter && snapshot.Enabled)
                        {
                            int aoeId = AoeIdBase + localId;
                            AoeEventWriter.Enqueue(new AoeSpawnEvent
                            {
                                Kind = IntervalChildKind.Aoe,
                                TemplateKey = snapshot.TemplateKey,
                                Faction = snapshot.Faction,
                                Position = position,
                                SourceId = aoeId,
                                JitterSeed = (uint)aoeId * 2654435761u
                            });
                        }

                        return;
                    case StackDetonationKind.Projectile:
                        if (HasProjectileEventWriter && snapshot.Enabled)
                        {
                            int sourceId = ProjectileDetonationSourceIdBase + localId;
                            int baseId = HashId(sourceId, entry.DebuffKey, 0, 0x7AB025);
                            ProjectileEventWriter.Enqueue(new ProjectileSpawnEvent
                            {
                                Kind = IntervalChildKind.Projectile,
                                TemplateKey = snapshot.TemplateKey,
                                Faction = snapshot.Faction,
                                Position = position,
                                AimDirection = new float2(1f, 0f),
                                SourceId = baseId,
                                JitterSeed = (uint)baseId * 2654435761u,
                                // Non-zero tick index selects the deterministic pattern-expansion
                                // path so the detonation fans out as a radial nova (its template is
                                // registered with the radial pattern). baseId already makes the
                                // per-shot ids unique, so a constant tick index is fine here.
                                DeterministicIdTickIndex = 1,
                                // Gate the nova against the detonation target so its projectiles
                                // spread outward instead of instantly re-hitting the target they
                                // spawn on top of — matches impact-projectile spawns
                                // (ProjectileCollisionSystem) and AOE-hit projectile bursts.
                                ContactGateSeedTargetId = targetKey
                            });
                        }

                        return;
                }
            }

            // Matches CombatTargetProxy.TargetKey / the collision systems' TargetKey so a
            // seeded contact gate refers to the same target key the collision job checks.
            private static int TargetKey(Entity entity)
            {
                unchecked
                {
                    int key = ((entity.Index + 1) * 397) ^ entity.Version;
                    key &= 0x7fffffff;
                    return key == 0 ? 1 : key;
                }
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

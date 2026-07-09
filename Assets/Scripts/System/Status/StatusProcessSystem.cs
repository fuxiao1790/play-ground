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
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Status
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CombatApplyFinalizeSingleSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    public partial class StatusProcessSystem : SystemBase
    {
        // A target that banks well past threshold in one frame detonates once per
        // threshold's worth of stacks, all in the same frame. This caps how many
        // detonations one target can emit per frame so the reserved id block stays
        // bounded and spawn ids stay unique; overflow rolls to the next frame.
        private const int MaxDetonationsPerTarget = 256;

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

            bool hasImpactAoeEvents = SystemAPI.TryGetSingletonRW<ImpactAoeSpawnEventSingleton>(
                out RefRW<ImpactAoeSpawnEventSingleton> impactAoeLane);
            NativeQueue<ImpactAoeSpawnEvent> impactAoeEventQueue =
                hasImpactAoeEvents ? impactAoeLane.ValueRO.EventQueue : default;
            hasImpactAoeEvents = hasImpactAoeEvents && impactAoeEventQueue.IsCreated;

            bool hasLingeringAoeEvents = SystemAPI.TryGetSingletonRW<LingeringAoeSpawnEventSingleton>(
                out RefRW<LingeringAoeSpawnEventSingleton> lingeringAoeLane);
            NativeQueue<LingeringAoeSpawnEvent> lingeringAoeEventQueue =
                hasLingeringAoeEvents ? lingeringAoeLane.ValueRO.EventQueue : default;
            hasLingeringAoeEvents = hasLingeringAoeEvents && lingeringAoeEventQueue.IsCreated;

            bool hasProjectileEvents = SystemAPI.TryGetSingletonRW<ProjectileSpawnEventSingleton>(
                out RefRW<ProjectileSpawnEventSingleton> projectileLane);
            NativeQueue<ProjectileSpawnEvent> projectileEventQueue =
                hasProjectileEvents ? projectileLane.ValueRO.EventQueue : default;
            hasProjectileEvents = hasProjectileEvents && projectileEventQueue.IsCreated;
            // Intentional managed lookup: AccrualFrame is finalize-system state, not a native container lane.
            CombatApplyFinalizeSingleSystem hitApply = World.GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>();

            int aoeIdBase = ReserveIdBlock(ref nextAoeId, entityCount);
            int projectileIdBase = ReserveIdBlock(ref nextProjectileDetonationSourceId, entityCount);

            // This job writes the expansion EventQueues via ParallelWriter, the same queues
            // other producers (TimedSpawnSystem, collision systems) already wrote
            // this frame. Those writes are tracked in each expansion system's ProducerHandle,
            // not in this SystemBase's component-derived Dependency, so we must depend on them
            // explicitly or the job-safety system rejects the schedule.
            JobHandle producerDeps = Dependency;
            if (hasImpactAoeEvents)
                producerDeps = JobHandle.CombineDependencies(producerDeps, impactAoeLane.ValueRO.ProducerHandle);
            if (hasLingeringAoeEvents)
                producerDeps = JobHandle.CombineDependencies(producerDeps, lingeringAoeLane.ValueRO.ProducerHandle);
            if (hasProjectileEvents)
                producerDeps = JobHandle.CombineDependencies(producerDeps, projectileLane.ValueRO.ProducerHandle);

            JobHandle statusHandle = new StatusProcessJob
            {
                DeltaTime = math.max(0f, SystemAPI.Time.DeltaTime),
                AccrualFrame = hitApply != null ? hitApply.AccrualFrame : 0,
                AoeIdBase = aoeIdBase,
                ProjectileDetonationSourceIdBase = projectileIdBase,
                ImpactAoeEventWriter = hasImpactAoeEvents
                    ? impactAoeEventQueue.AsParallelWriter()
                    : default,
                HasImpactAoeEventWriter = hasImpactAoeEvents,
                LingeringAoeEventWriter = hasLingeringAoeEvents
                    ? lingeringAoeEventQueue.AsParallelWriter()
                    : default,
                HasLingeringAoeEventWriter = hasLingeringAoeEvents,
                ProjectileEventWriter = hasProjectileEvents
                    ? projectileEventQueue.AsParallelWriter()
                    : default,
                HasProjectileEventWriter = hasProjectileEvents
            }.ScheduleParallel(targetStackQuery, producerDeps);

            if (hasImpactAoeEvents)
            {
                impactAoeLane.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(impactAoeLane.ValueRW.ProducerHandle, statusHandle);
            }

            if (hasLingeringAoeEvents)
            {
                lingeringAoeLane.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(lingeringAoeLane.ValueRW.ProducerHandle, statusHandle);
            }

            if (hasProjectileEvents)
            {
                projectileLane.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(projectileLane.ValueRW.ProducerHandle, statusHandle);
            }

            Dependency = statusHandle;
        }

        private static int ReserveIdBlock(ref int nextId, int entityCount)
        {
            int blockSize = math.max(1, entityCount * MaxDetonationsPerTarget);
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
            public NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter ImpactAoeEventWriter;
            public bool HasImpactAoeEventWriter;
            public NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter LingeringAoeEventWriter;
            public bool HasLingeringAoeEventWriter;
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
                        // One detonation per full threshold banked. All fire this frame;
                        // sub-threshold remainder stays banked for the next hit/detonation.
                        int bursts = entry.Count / threshold;
                        int budget = MaxDetonationsPerTarget - localDetonationIndex;
                        if (bursts > budget)
                        {
                            bursts = budget;
                        }

                        for (int b = 0; b < bursts; b++)
                        {
                            int localId = entityIndexInQuery * MaxDetonationsPerTarget + localDetonationIndex;
                            BuildDetonationSpawn(entry, targetPosition.Value, localId, targetKey);
                            localDetonationIndex++;
                        }

                        entry.Count -= bursts * threshold;
                        if (entry.Count <= 0)
                        {
                            stackEntries.RemoveAt(i);
                            continue;
                        }
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
                    case StackDetonationKind.ImpactAoe:
                        if (HasImpactAoeEventWriter && snapshot.Enabled)
                        {
                            int aoeId = AoeIdBase + localId;
                            ImpactAoeEventWriter.Enqueue(new ImpactAoeSpawnEvent
                            {
                                Kind = IntervalChildKind.ImpactAoe,
                                TemplateKey = snapshot.TemplateKey,
                                Faction = snapshot.Faction,
                                Position = position,
                                SourceId = aoeId,
                                JitterSeed = (uint)aoeId * 2654435761u
                            });
                        }

                        return;
                    case StackDetonationKind.LingeringAoe:
                        if (HasLingeringAoeEventWriter && snapshot.Enabled)
                        {
                            int aoeId = AoeIdBase + localId;
                            LingeringAoeEventWriter.Enqueue(new LingeringAoeSpawnEvent
                            {
                                Kind = IntervalChildKind.LingeringAoe,
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
                                // spawn on top of â€?matches impact-projectile spawns
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

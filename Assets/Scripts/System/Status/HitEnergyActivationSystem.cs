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
    [UpdateBefore(typeof(CombatApplyFinalizeSingleSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    public partial class HitEnergyActivationSystem : SystemBase
    {
        // Caps emitted activations per target/update so reserved id blocks remain
        // bounded and spawn ids stay unique. Unemitted energy remains banked.
        private const int MaxActivationsPerTarget = 256;

        private EntityQuery targetHitEnergyQuery;
        private int nextAoeId = 1;
        private int nextProjectileActivationSourceId = 1;

        protected override void OnCreate()
        {
            targetHitEnergyQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TargetPosition>(),
                ComponentType.ReadWrite<TargetHitEnergy>());
        }

        protected override void OnDestroy()
        {
            targetHitEnergyQuery.Dispose();
        }

        protected override void OnUpdate()
        {
            int entityCount = targetHitEnergyQuery.CalculateEntityCount();
            if (entityCount == 0)
            {
                return;
            }

            // The spawn lanes are created unconditionally by their expansion systems' OnCreate.
            // Read them directly: a missing lane is a broken world and must throw, not be skipped.
            RefRW<ImpactAoeSpawnEventSingleton> impactAoeLane =
                SystemAPI.GetSingletonRW<ImpactAoeSpawnEventSingleton>();
            RefRW<LingeringAoeSpawnEventSingleton> lingeringAoeLane =
                SystemAPI.GetSingletonRW<LingeringAoeSpawnEventSingleton>();
            RefRW<ProjectileSpawnEventSingleton> projectileLane =
                SystemAPI.GetSingletonRW<ProjectileSpawnEventSingleton>();

            int aoeIdBase = ReserveIdBlock(ref nextAoeId, entityCount);
            int projectileIdBase = ReserveIdBlock(ref nextProjectileActivationSourceId, entityCount);

            // Runs single-threaded in place (.Run) on the main thread. The job enqueues into
            // the expansion EventQueues, the same queues other producers (TimedSpawnSystem,
            // collision systems) already wrote this frame. Those in-flight writes are tracked
            // in each expansion system's ProducerHandle, not in this SystemBase's
            // component-derived Dependency, and .Run only completes the ECS component deps it
            // can see. So we must complete the producer handles by hand before writing the
            // queues synchronously ourselves, or the job-safety system rejects the access.
            JobHandle producerDeps = JobHandle.CombineDependencies(
                Dependency, impactAoeLane.ValueRO.ProducerHandle, lingeringAoeLane.ValueRO.ProducerHandle);
            producerDeps = JobHandle.CombineDependencies(producerDeps, projectileLane.ValueRO.ProducerHandle);
            producerDeps.Complete();

            new HitEnergyActivationJob
            {
                Now = SystemAPI.Time.ElapsedTime,
                AoeIdBase = aoeIdBase,
                ProjectileActivationSourceIdBase = projectileIdBase,
                ImpactAoeEventWriter = impactAoeLane.ValueRO.EventQueue.AsParallelWriter(),
                LingeringAoeEventWriter = lingeringAoeLane.ValueRO.EventQueue.AsParallelWriter(),
                ProjectileEventWriter = projectileLane.ValueRO.EventQueue.AsParallelWriter()
            }.Run(targetHitEnergyQuery);

            // The job enqueued synchronously on the main thread, so the queues are already
            // populated with no pending producer job to publish; downstream expansion systems
            // read them directly. The producer handles stay as-is (completed above).
        }

        private static int ReserveIdBlock(ref int nextId, int entityCount)
        {
            int blockSize = math.max(1, entityCount * MaxActivationsPerTarget);
            if (nextId <= 0 || nextId > int.MaxValue - blockSize)
            {
                nextId = 1;
            }

            int baseId = nextId;
            nextId += blockSize;
            return baseId;
        }

        [BurstCompile]
        private partial struct HitEnergyActivationJob : IJobEntity
        {
            public double Now;
            public int AoeIdBase;
            public int ProjectileActivationSourceIdBase;
            public NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter ImpactAoeEventWriter;
            public NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter LingeringAoeEventWriter;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;

            private void Execute(
                Entity targetEntity,
                [EntityIndexInQuery] int entityIndexInQuery,
                in TargetPosition targetPosition,
                DynamicBuffer<TargetHitEnergy> entries)
            {
                int targetKey = TargetKey(targetEntity);
                int localActivationIndex = 0;
                for (int i = entries.Length - 1; i >= 0; i--)
                {
                    TargetHitEnergy entry = entries[i];
                    if (Now >= entry.ExpiresAt)
                    {
                        entries.RemoveAt(i);
                        continue;
                    }

                    int budget = MaxActivationsPerTarget - localActivationIndex;
                    if (budget > 0 && entry.StoredEnergy >= entry.EnergyRequired)
                    {
                        int activations = (int)math.min(
                            math.floor(entry.StoredEnergy / entry.EnergyRequired),
                            budget);

                        for (int activationIndex = 0; activationIndex < activations; activationIndex++)
                        {
                            int localId = entityIndexInQuery * MaxActivationsPerTarget + localActivationIndex;
                            BuildActivationSpawn(entry, targetPosition.Value, localId, targetKey);
                            localActivationIndex++;
                        }

                        entry.StoredEnergy -= activations * entry.EnergyRequired;
                        if (entry.StoredEnergy < 0f)
                        {
                            entry.StoredEnergy = 0f;
                        }

                        if (entry.StoredEnergy == 0f)
                        {
                            entries.RemoveAt(i);
                            continue;
                        }
                    }

                    entries[i] = entry;
                }
            }

            private void BuildActivationSpawn(
                in TargetHitEnergy entry,
                float2 position,
                int localId,
                int targetKey)
            {
                HitEnergySpawn spawn = entry.HitEnergySpawn;
                switch (spawn.Kind)
                {
                    case HitEnergySpawnKind.ImpactAoe:
                        if (spawn.Enabled)
                        {
                            int aoeId = AoeIdBase + localId;
                            ImpactAoeEventWriter.Enqueue(new ImpactAoeSpawnEvent
                            {
                                Kind = IntervalChildKind.ImpactAoe,
                                TemplateKey = spawn.TemplateKey,
                                Faction = spawn.Faction,
                                Position = position,
                                SourceId = aoeId,
                                JitterSeed = (uint)aoeId * 2654435761u
                            });
                        }

                        return;
                    case HitEnergySpawnKind.LingeringAoe:
                        if (spawn.Enabled)
                        {
                            int aoeId = AoeIdBase + localId;
                            LingeringAoeEventWriter.Enqueue(new LingeringAoeSpawnEvent
                            {
                                Kind = IntervalChildKind.LingeringAoe,
                                TemplateKey = spawn.TemplateKey,
                                Faction = spawn.Faction,
                                Position = position,
                                SourceId = aoeId,
                                JitterSeed = (uint)aoeId * 2654435761u
                            });
                        }

                        return;
                    case HitEnergySpawnKind.Projectile:
                        if (spawn.Enabled)
                        {
                            int sourceId = ProjectileActivationSourceIdBase + localId;
                            int baseId = HashId(sourceId, entry.AccumulatorId, 0, 0x7AB025);
                            ProjectileEventWriter.Enqueue(new ProjectileSpawnEvent
                            {
                                Kind = IntervalChildKind.Projectile,
                                TemplateKey = spawn.TemplateKey,
                                Faction = spawn.Faction,
                                Position = position,
                                AimDirection = new float2(1f, 0f),
                                SourceId = baseId,
                                JitterSeed = (uint)baseId * 2654435761u,
                                // Non-zero tick index selects the deterministic pattern-expansion
                                // path so activation fans out as a radial nova (its template is
                                // registered with the radial pattern). baseId already makes the
                                // per-shot ids unique, so a constant tick index is fine here.
                                DeterministicIdTickIndex = 1,
                                // Gate nova against activation target so its projectiles
                                // spread outward instead of instantly re-hitting the target they
                                // spawn on top of �?matches impact-projectile spawns
                                // (ProjectileDiscreteCollisionSystem) and AOE-hit projectile bursts.
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

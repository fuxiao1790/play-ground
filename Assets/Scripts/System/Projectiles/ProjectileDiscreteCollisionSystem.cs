using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Targeted;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Combat.Projectiles
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileContactGateSystem))]
    [UpdateBefore(typeof(CombatApplyFinalizeSingleSystem))]
    public partial struct ProjectileDiscreteCollisionSystem : ISystem
    {
        private static readonly ProfilerMarker QueryMarker =
            new("ProjectileDiscreteCollisionSystem.BvhQuery");

        private EntityQuery activeProjectileQuery;
        private EntityTypeHandle entityHandle;
        private ComponentTypeHandle<ProjectileIdentityComponent> identityHandle;
        private ComponentTypeHandle<CombatHitPayload> payloadHandle;
        private ComponentTypeHandle<CombatKinematicsComponent> kinematicsHandle;
        private ComponentTypeHandle<CombatCollisionComponent> collisionShapeHandle;
        private ComponentTypeHandle<TimedSpawnComponent> timedSpawnHandle;
        private ComponentTypeHandle<CombatLifetimeComponent> lifetimeHandle;
        private ComponentTypeHandle<ProjectileHitComponent> projectileHitHandle;
        private ComponentTypeHandle<Active> activeHandle;
        private ComponentTypeHandle<ArmingTag> armingHandle;
        private BufferTypeHandle<ProjectileContactGateElement> contactGateHandle;

        public void OnCreate(ref SystemState state)
        {
            // One explicit query now drives both the emptiness pre-check and IJobChunk
            // scheduling. It must reproduce exactly what the generated IJobEntity query used
            // to express, so every enabled-state clause is spelled out here: Active and
            // CombatCollisionActiveTag present and enabled, ArmingTag present and disabled.
            // Built through EntityQueryBuilder rather than GetEntityQuery because the
            // ComponentType[] form cannot express disabled/present matching at all.
            // TimedSpawnComponent is read only to release its template key on death. It is
            // enableable and disabled on non-timed projectiles, so it must be Present rather
            // than All or the query would drop every non-timed projectile from collision.
            activeProjectileQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag, CombatCollisionActiveTag>()
                .WithAll<ProjectileIdentityComponent, CombatHitPayload>()
                .WithAll<CombatKinematicsComponent, CombatCollisionComponent>()
                .WithAllRW<Active>()
                .WithAllRW<CombatLifetimeComponent, ProjectileHitComponent>()
                .WithAllRW<ProjectileContactGateElement>()
                .WithDisabledRW<ArmingTag>()
                .WithPresent<TimedSpawnComponent>()
                .WithNone<ProjectileContinuousTag>()
                .Build(ref state);

            entityHandle = state.GetEntityTypeHandle();
            identityHandle = state.GetComponentTypeHandle<ProjectileIdentityComponent>(true);
            payloadHandle = state.GetComponentTypeHandle<CombatHitPayload>(true);
            kinematicsHandle = state.GetComponentTypeHandle<CombatKinematicsComponent>(true);
            collisionShapeHandle = state.GetComponentTypeHandle<CombatCollisionComponent>(true);
            timedSpawnHandle = state.GetComponentTypeHandle<TimedSpawnComponent>(true);

            // Read-write handles: the two component values the job mutates, plus the two
            // enableable tags whose bits it flips through EnabledRefRW. GetEnabledMask only
            // yields a writable mask from a read-write handle.
            lifetimeHandle = state.GetComponentTypeHandle<CombatLifetimeComponent>(false);
            projectileHitHandle = state.GetComponentTypeHandle<ProjectileHitComponent>(false);
            activeHandle = state.GetComponentTypeHandle<Active>(false);
            armingHandle = state.GetComponentTypeHandle<ArmingTag>(false);
            contactGateHandle = state.GetBufferTypeHandle<ProjectileContactGateElement>(false);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (activeProjectileQuery.IsEmpty)
            {
                return;
            }

            TargetBroadphaseSingleton hash = SystemAPI.GetSingleton<TargetBroadphaseSingleton>();
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle);

            // The spawn lanes and hit-dispatch lane are created unconditionally by their owning
            // systems' OnCreate. Read them directly: a missing lane is a broken world and must
            // throw here, not be silently skipped.
            RefRW<ProjectileSpawnEventSingleton> projectileLane =
                SystemAPI.GetSingletonRW<ProjectileSpawnEventSingleton>();
            RefRW<ImpactAoeSpawnEventSingleton> impactAoeLane =
                SystemAPI.GetSingletonRW<ImpactAoeSpawnEventSingleton>();
            RefRW<LingeringAoeSpawnEventSingleton> lingeringAoeLane =
                SystemAPI.GetSingletonRW<LingeringAoeSpawnEventSingleton>();
            RefRW<TargetedSpawnEventSingleton> targetedLane =
                SystemAPI.GetSingletonRW<TargetedSpawnEventSingleton>();
            RefRW<CombatHitDispatchSingleton> hitDispatch =
                SystemAPI.GetSingletonRW<CombatHitDispatchSingleton>();
            SpawnTemplateRegistryState registryState = SystemAPI.GetSingleton<SpawnTemplateRegistryState>();

            entityHandle.Update(ref state);
            identityHandle.Update(ref state);
            payloadHandle.Update(ref state);
            kinematicsHandle.Update(ref state);
            collisionShapeHandle.Update(ref state);
            timedSpawnHandle.Update(ref state);
            lifetimeHandle.Update(ref state);
            projectileHitHandle.Update(ref state);
            activeHandle.Update(ref state);
            armingHandle.Update(ref state);
            contactGateHandle.Update(ref state);

            var job = new ProjectileCollisionJob
            {
                EntityHandle = entityHandle,
                IdentityHandle = identityHandle,
                PayloadHandle = payloadHandle,
                KinematicsHandle = kinematicsHandle,
                CollisionShapeHandle = collisionShapeHandle,
                TimedSpawnHandle = timedSpawnHandle,
                LifetimeHandle = lifetimeHandle,
                ProjectileHitHandle = projectileHitHandle,
                ActiveHandle = activeHandle,
                ArmingHandle = armingHandle,
                ContactGateHandle = contactGateHandle,
                SpawnTemplateDeltas = registryState.Deltas.AsParallelWriter(),
                TargetEntities = hash.TargetEntities.AsArray(),
                TargetPositions = hash.TargetPositions.AsArray(),
                TargetShapes = hash.TargetShapes.AsArray(),
                TargetFactions = hash.TargetFactions.AsArray(),
                Tree = hash.DiscreteBvh,
#if ENABLE_PROFILER
                MetricsWriter = hash.DiscreteQueryMetrics.AsParallelWriter(),
#endif
                TotalTargetCount = hash.TargetCount,
                HitWriter = hitDispatch.ValueRO.HitQueue.AsParallelWriter(),
                ProjectileEventWriter = projectileLane.ValueRO.EventQueue.AsParallelWriter(),
                ImpactAoeEventWriter = impactAoeLane.ValueRO.EventQueue.AsParallelWriter(),
                LingeringAoeEventWriter = lingeringAoeLane.ValueRO.EventQueue.AsParallelWriter(),
                TargetedEventWriter = targetedLane.ValueRO.EventQueue.AsParallelWriter()
            };

            JobHandle collisionHandle = job.ScheduleParallel(activeProjectileQuery, state.Dependency);

            // The collision job writes both expansion EventQueues via ParallelWriter. Those
            // queues are read on the main thread by the expansion systems, which only complete
            // their own component-derived dependency. Forward this write job so they wait on it.
            projectileLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(projectileLane.ValueRW.ProducerHandle, collisionHandle);
            impactAoeLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(impactAoeLane.ValueRW.ProducerHandle, collisionHandle);
            lingeringAoeLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(lingeringAoeLane.ValueRW.ProducerHandle, collisionHandle);
            targetedLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(targetedLane.ValueRW.ProducerHandle, collisionHandle);
            hitDispatch.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(hitDispatch.ValueRW.ProducerHandle, collisionHandle);
            RefRW<TargetBroadphaseSingleton> hashRw = SystemAPI.GetSingletonRW<TargetBroadphaseSingleton>();
            hashRw.ValueRW.ConsumerHandle = JobHandle.CombineDependencies(
                hashRw.ValueRW.ConsumerHandle,
                collisionHandle);

            state.Dependency = collisionHandle;
        }

        [BurstCompile]
        private struct ProjectileCollisionJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<ProjectileIdentityComponent> IdentityHandle;
            [ReadOnly] public ComponentTypeHandle<CombatHitPayload> PayloadHandle;
            [ReadOnly] public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            [ReadOnly] public ComponentTypeHandle<CombatCollisionComponent> CollisionShapeHandle;
            [ReadOnly] public ComponentTypeHandle<TimedSpawnComponent> TimedSpawnHandle;
            public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            public ComponentTypeHandle<ProjectileHitComponent> ProjectileHitHandle;
            public ComponentTypeHandle<Active> ActiveHandle;
            public ComponentTypeHandle<ArmingTag> ArmingHandle;
            public BufferTypeHandle<ProjectileContactGateElement> ContactGateHandle;

            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public NativeArray<TargetFaction> TargetFactions;

            /// <summary>
            /// Discrete broadphase tree owned by TargetBroadphaseSystem. Read-only: its
            /// buffers are shared with every other chunk invocation running in parallel.
            /// </summary>
            [ReadOnly] public BvhTree Tree;
#if ENABLE_PROFILER
            public NativeQueue<BvhQueryMetrics>.ParallelWriter MetricsWriter;
#endif

            public int TotalTargetCount;
            public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;
            public NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter ImpactAoeEventWriter;
            public NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter LingeringAoeEventWriter;
            public NativeQueue<TargetedSpawnEvent>.ParallelWriter TargetedEventWriter;
            public NativeQueue<SpawnTemplateRefDelta>.ParallelWriter SpawnTemplateDeltas;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                // IJobChunk gives one sample per chunk, not one sample per projectile.
                using ProfilerMarker.AutoScope marker = QueryMarker.Auto();
                NativeArray<Entity> entities = chunk.GetNativeArray(EntityHandle);
                NativeArray<ProjectileIdentityComponent> identities = chunk.GetNativeArray(ref IdentityHandle);
                NativeArray<CombatHitPayload> payloads = chunk.GetNativeArray(ref PayloadHandle);
                NativeArray<CombatKinematicsComponent> kinematics = chunk.GetNativeArray(ref KinematicsHandle);
                NativeArray<CombatCollisionComponent> collisions = chunk.GetNativeArray(ref CollisionShapeHandle);
                NativeArray<TimedSpawnComponent> timedSpawns = chunk.GetNativeArray(ref TimedSpawnHandle);
                NativeArray<CombatLifetimeComponent> lifetimes = chunk.GetNativeArray(ref LifetimeHandle);
                NativeArray<ProjectileHitComponent> projectileHits = chunk.GetNativeArray(ref ProjectileHitHandle);
                BufferAccessor<ProjectileContactGateElement> contactGates =
                    chunk.GetBufferAccessor(ref ContactGateHandle);
                EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);
                EnabledMask armingMask = chunk.GetEnabledMask(ref ArmingHandle);

                // One traversal workspace per chunk invocation, reseeded per projectile below.
                // This Execute call is the chunk invocation, so a local here is exactly one
                // stack per concurrently executing chunk and never one per projectile.
                BvhTraversalWorkspace workspace = BvhTraversalWorkspace.Create(Tree);

                ChunkEntityEnumerator enumerator =
                    new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    // NativeArray hands back copies, so the two mutated components are read
                    // into locals and written back once per entity; the buffer and the two
                    // enabled bits are views and mutate in place.
                    CombatLifetimeComponent lifetime = lifetimes[i];
                    ProjectileHitComponent projectileHit = projectileHits[i];

                    Collide(
                        ref workspace,
                        entities[i],
                        identities[i],
                        payloads[i],
                        kinematics[i],
                        collisions[i],
                        timedSpawns[i],
                        ref lifetime,
                        ref projectileHit,
                        activeMask.GetEnabledRefRW<Active>(i),
                        armingMask.GetEnabledRefRW<ArmingTag>(i),
                        contactGates[i]);

                    lifetimes[i] = lifetime;
                    projectileHits[i] = projectileHit;
                }

#if ENABLE_PROFILER
                // One write after whole chunk: candidate traversal and narrowphase never
                // contend on a shared counter or perform one atomic operation per candidate.
                MetricsWriter.Enqueue(workspace.Metrics);
#endif
            }

            private void Collide(
                ref BvhTraversalWorkspace workspace,
                Entity entity,
                in ProjectileIdentityComponent identity,
                in CombatHitPayload payload,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                in TimedSpawnComponent timedSpawn,
                ref CombatLifetimeComponent lifetime,
                ref ProjectileHitComponent projectileHit,
                EnabledRefRW<Active> active,
                EnabledRefRW<ArmingTag> arming,
                DynamicBuffer<ProjectileContactGateElement> contactGates)
            {
                if (identity.Faction == CombatFaction.None)
                {
                    ProjectileHitEmission.Deactivate(
                        ref lifetime,
                        active,
                        arming,
                        in projectileHit,
                        in timedSpawn,
                        in payload,
                        SpawnTemplateDeltas);
                    return;
                }

                if (lifetime.Remaining <= 0f)
                {
                    ProjectileHitEmission.Deactivate(
                        ref lifetime,
                        active,
                        arming,
                        in projectileHit,
                        in timedSpawn,
                        in payload,
                        SpawnTemplateDeltas);
                    return;
                }

                // Pierce is the projectile's hit cap. It may still hit at 0; below zero is exhausted.
                if (projectileHit.PierceRemaining < 0)
                {
                    ProjectileHitEmission.Deactivate(
                        ref lifetime,
                        active,
                        arming,
                        in projectileHit,
                        in timedSpawn,
                        in payload,
                        SpawnTemplateDeltas);
                    return;
                }

                if (TotalTargetCount == 0)
                {
                    return;
                }

                // Conservative query circle circumscribing the projectile's own world AABB:
                // centre at the box midpoint, radius the half-diagonal. The AABB already
                // contains the exact shape by construction, so this circle can never miss a
                // target the shape could touch. Target circles already carry their own
                // bounding radius in the tree, so no global radius expansion belongs here.
                float2 queryCenter = (collision.BoundsMin + collision.BoundsMax) * 0.5f;
                float queryRadius = math.distance(queryCenter, collision.BoundsMax);
                workspace.Reset(queryCenter, queryRadius);

                while (workspace.TryMoveNext(out int targetIdx))
                {
                    if (TargetFactions[targetIdx].Value == identity.Faction)
                    {
                        continue;
                    }

                    Entity targetEntity = TargetEntities[targetIdx];
                    TargetPosition targetPosition = TargetPositions[targetIdx];
                    TargetCollisionShape target = TargetShapes[targetIdx];
                    int targetKey = ProjectileHitEmission.TargetKey(targetEntity);

                    if (ProjectileHitEmission.IsGated(contactGates, targetKey))
                    {
                        continue;
                    }

                    // Second, tighter filter: the broadphase circle is deliberately loose, so
                    // the exact AABB test still prunes its false positives before narrowphase.
                    if (!CombatCollisionMath.BoundsIntersect(
                        collision.BoundsMin,
                        collision.BoundsMax,
                        target.BoundsMin,
                        target.BoundsMax))
                    {
                        continue;
                    }

#if ENABLE_PROFILER
                    workspace.RecordExactTest();
#endif
                    if (!CombatCollisionMath.Hit(
                            kinematics.Position,
                            collision.Radius,
                            collision.HalfExtents,
                            collision.RotationRadians,
                            collision.ShapeType,
                            targetPosition.Value,
                            target.Radius,
                            target.HalfExtents,
                            target.RotationRadians,
                            target.ShapeType))
                    {
                        continue;
                    }

                    ProjectileHitEmission.EnqueueHitEvent(HitWriter, entity, targetEntity, payload);
                    ProjectileHitEmission.EnqueueOnHitProjectile(
                        identity,
                        projectileHit,
                        kinematics.Position,
                        targetPosition.Value,
                        targetKey,
                        ProjectileEventWriter);
                    ProjectileHitEmission.EnqueueOnHitAoe(
                        identity,
                        projectileHit,
                        kinematics.Position,
                        targetKey,
                        ImpactAoeEventWriter,
                        LingeringAoeEventWriter);
                    ProjectileHitEmission.EnqueueOnHitTargeted(
                        identity,
                        projectileHit,
                        kinematics.Position,
                        targetKey,
                        TargetedEventWriter);

                    ProjectileHitEmission.AddOrRefreshGate(contactGates, targetKey,
                        projectileHit.RepeatHitCooldownSeconds);

                    projectileHit.PierceRemaining--;
                    if (projectileHit.PierceRemaining < 0)
                    {
                        ProjectileHitEmission.Deactivate(
                            ref lifetime,
                            active,
                            arming,
                            in projectileHit,
                            in timedSpawn,
                            in payload,
                            SpawnTemplateDeltas);
                        return;
                    }
                }
            }

        }

    }

    internal static class ProjectileHitEmission
    {
        private const int ImpactAoeIdSalt = 0x5F1A0E;
        private const int ImpactProjectileIdSalt = 0x2C1297;

        internal static void EnqueueHitEvent(
            NativeQueue<CombatHitEvent>.ParallelWriter hitWriter,
            Entity source,
            Entity target,
            in CombatHitPayload payload)
        {
            if (!HasHitEvent(payload))
            {
                return;
            }

            hitWriter.Enqueue(new CombatHitEvent
            {
                Source = source,
                Target = target
            });
        }

        internal static void EnqueueOnHitProjectile(
            in ProjectileIdentityComponent identity,
            in ProjectileHitComponent projectileHit,
            float2 impactPosition,
            float2 targetPosition,
            int targetKey,
            NativeQueue<ProjectileSpawnEvent>.ParallelWriter projectileEventWriter)
        {
            if (!projectileHit.OnHitSpawn.Enabled)
            {
                return;
            }

            if (projectileHit.OnHitSpawn.Kind == IntervalChildKind.ImpactAoe
                || projectileHit.OnHitSpawn.Kind == IntervalChildKind.LingeringAoe)
            {
                return;
            }

            if (projectileHit.OnHitSpawn.Kind == IntervalChildKind.Targeted)
            {
                return;
            }

            if (projectileHit.OnHitSpawn.Kind != IntervalChildKind.Projectile)
            {
                throw new global::System.InvalidOperationException(
                    "Unhandled interval child kind.");
            }

            int baseId = HashId(
                identity.ProjectileId,
                identity.TypeId,
                targetKey,
                ImpactProjectileIdSalt);
            projectileEventWriter.Enqueue(new ProjectileSpawnEvent
            {
                Kind = projectileHit.OnHitSpawn.Kind,
                TemplateKey = projectileHit.OnHitSpawn.TemplateKey,
                Faction = identity.Faction,
                Position = impactPosition,
                AimDirection = DirectionFromTo(impactPosition, targetPosition, invert: true),
                SourceId = baseId,
                JitterSeed = (uint)baseId * 2654435761u,
                ContactGateSeedTargetId = targetKey
            });
        }

        internal static void EnqueueOnHitAoe(
            in ProjectileIdentityComponent identity,
            in ProjectileHitComponent projectileHit,
            float2 impactPosition,
            int targetKey,
            NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter impactAoeEventWriter,
            NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter lingeringAoeEventWriter)
        {
            if (!projectileHit.OnHitSpawn.Enabled
                || projectileHit.OnHitSpawn.Kind == IntervalChildKind.Projectile)
            {
                return;
            }

            if (projectileHit.OnHitSpawn.Kind == IntervalChildKind.Targeted)
            {
                return;
            }

            if (projectileHit.OnHitSpawn.Kind != IntervalChildKind.ImpactAoe
                && projectileHit.OnHitSpawn.Kind != IntervalChildKind.LingeringAoe)
            {
                throw new global::System.InvalidOperationException(
                    "Unhandled interval child kind.");
            }

            int aoeId = HashId(
                identity.ProjectileId,
                identity.TypeId,
                targetKey,
                ImpactAoeIdSalt);

            if (projectileHit.OnHitSpawn.Kind == IntervalChildKind.LingeringAoe)
            {
                lingeringAoeEventWriter.Enqueue(new LingeringAoeSpawnEvent
                {
                    Kind = projectileHit.OnHitSpawn.Kind,
                    TemplateKey = projectileHit.OnHitSpawn.TemplateKey,
                    Faction = identity.Faction,
                    Position = impactPosition,
                    SourceId = aoeId,
                    JitterSeed = (uint)aoeId * 2654435761u,
                    ContactGateSeedTargetId = targetKey
                });
                return;
            }

            impactAoeEventWriter.Enqueue(new ImpactAoeSpawnEvent
            {
                Kind = projectileHit.OnHitSpawn.Kind,
                TemplateKey = projectileHit.OnHitSpawn.TemplateKey,
                Faction = identity.Faction,
                Position = impactPosition,
                SourceId = aoeId,
                JitterSeed = (uint)aoeId * 2654435761u,
                ContactGateSeedTargetId = targetKey
            });
        }

        internal static void EnqueueOnHitTargeted(
            in ProjectileIdentityComponent identity,
            in ProjectileHitComponent projectileHit,
            float2 impactPosition,
            int targetKey,
            NativeQueue<TargetedSpawnEvent>.ParallelWriter targetedEventWriter)
        {
            if (!projectileHit.OnHitSpawn.Enabled
                || projectileHit.OnHitSpawn.Kind != IntervalChildKind.Targeted)
            {
                return;
            }

            TargetedSpawnEmission.Enqueue(
                identity.ProjectileId,
                identity.TypeId,
                identity.Faction,
                impactPosition,
                targetKey,
                projectileHit.OnHitSpawn.Kind,
                projectileHit.OnHitSpawn.TemplateKey,
                targetedEventWriter);
        }

        // Single projectile death funnel for both collision lanes. Despawn emits a release
        // event for every template key the entity carries; it never touches a reference count.
        internal static void Deactivate(
            ref CombatLifetimeComponent lifetime,
            EnabledRefRW<Active> active,
            EnabledRefRW<ArmingTag> arming,
            in ProjectileHitComponent projectileHit,
            in TimedSpawnComponent timedSpawn,
            in CombatHitPayload payload,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas)
        {
            lifetime.Remaining = 0f;
            CombatDeathUtility.Kill(active, arming);
            SpawnTemplateRefEmit.ReleaseProjectile(in projectileHit, in timedSpawn, in payload, deltas);
        }

        internal static bool IsGated(DynamicBuffer<ProjectileContactGateElement> contactGates, int targetId)
        {
            for (int i = 0; i < contactGates.Length; i++)
            {
                if (contactGates[i].TargetId == targetId)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void AddOrRefreshGate(
            DynamicBuffer<ProjectileContactGateElement> contactGates,
            int targetId,
            float cooldownSeconds)
        {
            if (cooldownSeconds <= 0f)
            {
                cooldownSeconds = float.MaxValue;
            }

            for (int i = 0; i < contactGates.Length; i++)
            {
                ProjectileContactGateElement gate = contactGates[i];
                if (gate.TargetId != targetId)
                {
                    continue;
                }

                gate.CooldownRemaining = cooldownSeconds;
                contactGates[i] = gate;
                return;
            }

            contactGates.Add(new ProjectileContactGateElement
            {
                TargetId = targetId,
                CooldownRemaining = cooldownSeconds
            });
        }

        internal static bool HasHitEvent(in CombatHitPayload payload) =>
            payload.DirectDamageEnabled || payload.StackEffect.Enabled;

        internal static int TargetKey(Entity entity)
        {
            unchecked
            {
                int key = ((entity.Index + 1) * 397) ^ entity.Version;
                key &= 0x7fffffff;
                return key == 0 ? 1 : key;
            }
        }

        internal static float2 DirectionFromTo(float2 from, float2 to, bool invert)
        {
            float2 toTarget = to - from;
            if (math.lengthsq(toTarget) <= 0.0001f)
            {
                return new float2(1f, 0f);
            }

            float2 direction = math.normalize(toTarget);
            return invert ? -direction : direction;
        }

        internal static int HashId(int a, int b, int c, int salt)
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

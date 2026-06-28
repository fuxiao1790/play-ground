using System;
using System.Collections.Generic;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Projectile
{
    public abstract partial class ProjectileSpawnApplySystemBase : SystemBase
    {
        private readonly ProfilerMarker _spawnMarker;
        private readonly ProfilerMarker _completeDependencyMarker;
        private readonly ProfilerMarker _drainCommandsMarker;
        private readonly ProfilerMarker _reuseJobMarker;
        private readonly ProfilerMarker _coldCreateMarker;
        private readonly ProfilerCounterValue<int> _spawnColdCreateCounter;
        private readonly ProfilerCounterValue<int> _spawnReuseCounter;

        private EntityArchetype _archetype;

        private readonly Dictionary<ProjectileSpawnKey, EntityQuery> _deadSlotQueriesByKey = new();
        private readonly List<ProjectileSpawnWork> _spawnWork = new();

        // Burst counting-sort scratch, reused across frames to avoid reallocation.
        private NativeHashMap<ProjectileSpawnKey, int> _keyToIndex;
        private NativeList<ProjectileSpawnKey> _keys;
        private NativeList<int> _counts;
        private NativeList<int> _offsets;
        // Sorted-position -> original command index. Bucketing permutes indices, not payloads,
        // so the large ProjectileSpawnCommand struct is copied exactly once (by the reuse/cold path).
        private NativeList<int> _order;

        protected ProjectileSpawnApplySystemBase(
            string spawnMarkerName,
            string reuseMarkerName,
            string coldCounterName,
            string reuseCounterName)
        {
            _spawnMarker = new ProfilerMarker(spawnMarkerName);
            _completeDependencyMarker = new ProfilerMarker($"{spawnMarkerName}.CompleteDependency");
            _drainCommandsMarker = new ProfilerMarker($"{spawnMarkerName}.DrainCommands");
            _reuseJobMarker = new ProfilerMarker(reuseMarkerName);
            _coldCreateMarker = new ProfilerMarker($"{spawnMarkerName}.ColdCreate");
            _spawnColdCreateCounter =
                new ProfilerCounterValue<int>(ProfilerCategory.Scripts, coldCounterName, ProfilerMarkerDataUnit.Count);
            _spawnReuseCounter =
                new ProfilerCounterValue<int>(ProfilerCategory.Scripts, reuseCounterName, ProfilerMarkerDataUnit.Count);
        }

        protected override void OnCreate()
        {
            _archetype = CreateArchetype();
            _keyToIndex = new NativeHashMap<ProjectileSpawnKey, int>(16, Allocator.Persistent);
            _keys = new NativeList<ProjectileSpawnKey>(16, Allocator.Persistent);
            _counts = new NativeList<int>(16, Allocator.Persistent);
            _offsets = new NativeList<int>(16, Allocator.Persistent);
            _order = new NativeList<int>(256, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            DisposeSpawnWorkReferences();
            if (_keyToIndex.IsCreated) _keyToIndex.Dispose();
            if (_keys.IsCreated) _keys.Dispose();
            if (_counts.IsCreated) _counts.Dispose();
            if (_offsets.IsCreated) _offsets.Dispose();
            if (_order.IsCreated) _order.Dispose();
            _deadSlotQueriesByKey.Clear();
            _spawnWork.Clear();
        }

        protected override void OnUpdate()
        {
            using (_completeDependencyMarker.Auto())
            {
                Dependency.Complete();
            }

            int totalRequests = 0;
            NativeArray<ProjectileSpawnCommand> commands = default;
            using (_drainCommandsMarker.Auto())
            {
                var expansionSys = World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
                if (expansionSys != null)
                {
                    expansionSys.PendingHandle.Complete();
                    NativeList<ProjectileSpawnCommand> commandContainer = CommandContainer(expansionSys);
                    if (commandContainer.IsCreated && commandContainer.Length > 0)
                    {
                        // Read the producer's NativeList in place (no copy) and bucket by reuse
                        // key inside a Burst job. The job counting-sorts an index permutation
                        // (Order), not the payload: pass 1 counts per key, prefix-sum yields
                        // offsets, pass 2 scatters the 4-byte command indices into per-key
                        // contiguous runs. The large command struct is copied exactly once, by
                        // the reuse/cold path that reads commands[Order[i]]. The expansion system
                        // clears the list at the start of next frame, so we leave it untouched.
                        commands = commandContainer.AsArray();
                        totalRequests = commands.Length;

                        _order.ResizeUninitialized(commands.Length);
                        new BucketCommandsJob
                        {
                            Commands = commands,
                            KeyToIndex = _keyToIndex,
                            Keys = _keys,
                            Counts = _counts,
                            Offsets = _offsets,
                            Order = _order.AsArray(),
                        }.Schedule().Complete();
                    }
                }
            }

            if (totalRequests == 0) return;

            using (_spawnMarker.Auto())
            {
                using var createEcb = new EntityCommandBuffer(Allocator.Temp);
                int reuseCount = 0;
                _spawnWork.Clear();

                using (_reuseJobMarker.Auto())
                {
                    NativeArray<int> order = _order.AsArray();
                    var jobHandles = new NativeList<JobHandle>(_keys.Length, Allocator.Temp);
                    for (int b = 0; b < _keys.Length; b++)
                    {
                        ProjectileSpawnKey key = _keys[b];
                        EntityQuery query = DeadSlotQueryFor(key);
                        NativeArray<int> orderSlice = order.GetSubArray(_offsets[b], _counts[b]);
                        var claimedReference = new NativeReference<int>(Allocator.TempJob);
                        claimedReference.Value = 0;

                        JobHandle spawnHandle = ScheduleReuseJob(commands, orderSlice, claimedReference, query);
                        jobHandles.Add(spawnHandle);
                        _spawnWork.Add(new ProjectileSpawnWork(commands, orderSlice, claimedReference));
                    }

                    JobHandle.CombineDependencies(jobHandles.AsArray()).Complete();
                    jobHandles.Dispose();
                }

                int coldCreateCount = 0;
                using (_coldCreateMarker.Auto())
                {
                    foreach (ProjectileSpawnWork work in _spawnWork)
                    {
                        int claimed = work.ClaimedCount.Value;
                        work.ClaimedCount.Dispose();
                        reuseCount += claimed;
                        for (int i = claimed; i < work.Order.Length; i++)
                        {
                            CreateProjectileEntity(work.Commands[work.Order[i]], _archetype, createEcb);
                            coldCreateCount++;
                        }
                    }
                }
                _spawnWork.Clear();

                if (coldCreateCount > 0)
                    createEcb.Playback(EntityManager);
                _spawnReuseCounter.Value = reuseCount;
                _spawnColdCreateCounter.Value = totalRequests - reuseCount;
            }

            commands.Dispose();
        }

        protected abstract EntityArchetype CreateArchetype();

        protected abstract NativeList<ProjectileSpawnCommand> CommandContainer(
            ProjectileSpawnExpansionSystem expansionSys);

        protected abstract EntityQuery BuildDeadSlotQuery();

        protected abstract JobHandle ScheduleReuseJob(
            NativeArray<ProjectileSpawnCommand> commands,
            NativeArray<int> order,
            NativeReference<int> claimedReference,
            EntityQuery query);

        protected abstract void CreateProjectileEntity(
            ProjectileSpawnCommand cmd,
            EntityArchetype archetype,
            EntityCommandBuffer ecb);

        private EntityQuery DeadSlotQueryFor(ProjectileSpawnKey key)
        {
            if (!_deadSlotQueriesByKey.TryGetValue(key, out EntityQuery query))
            {
                query = BuildDeadSlotQuery();
                _deadSlotQueriesByKey[key] = query;
            }

            query.SetSharedComponentFilter(new CombatRenderBatchId { Value = key.BatchId });
            return query;
        }

        private void DisposeSpawnWorkReferences()
        {
            foreach (ProjectileSpawnWork work in _spawnWork)
            {
                if (work.ClaimedCount.IsCreated)
                    work.ClaimedCount.Dispose();
            }
            _spawnWork.Clear();
        }

        protected static void RecordCommonProjectileReset(
            EntityCommandBuffer ecb,
            Entity entity,
            CombatFaction faction,
            ProjectileSpawnCommand cmd)
        {
            ecb.SetComponent(entity, new ProjectileIdentityComponent
            {
                Faction = faction,
                ProjectileId = cmd.ProjectileId,
                TypeId = cmd.TypeId
            });
            ecb.SetComponent(entity, new CombatKinematicsComponent
            {
                Position = cmd.Position,
                Velocity = cmd.Velocity
            });
            ecb.SetComponent(entity, new CombatCollisionComponent
            {
                ShapeType = cmd.ShapeType,
                Radius = cmd.Radius,
                HalfExtents = cmd.HalfExtents,
                RotationRadians = cmd.RotationRadians,
                BoundsMin = cmd.BoundsMin,
                BoundsMax = cmd.BoundsMax
            });
            ecb.SetComponent(entity, new CombatLifetimeComponent { Remaining = cmd.Lifetime });
            ecb.SetComponentEnabled<CombatLifetimeComponent>(entity, true);
            ecb.SetComponent(entity, new ProjectileHitComponent
            {
                PierceRemaining = cmd.PierceRemaining,
                RepeatHitCooldownSeconds = cmd.RepeatHitCooldownSeconds,
                HitPayload = HitPayloadFor(in cmd, faction)
            });
            ecb.SetComponent(entity, cmd.Tracking);
            ecb.SetComponentEnabled<ProjectileTrackingComponent>(entity, cmd.Tracking.TrackingEnabled);
            ecb.SetComponent(entity, cmd.Render);
            ecb.SetComponent(entity, new CombatRenderElement());

            if (cmd.SeedContactGateTargetId > 0)
            {
                ecb.AppendToBuffer(entity, new ProjectileContactGateElement
                {
                    TargetId = cmd.SeedContactGateTargetId,
                    CooldownRemaining = math.max(0.1f, cmd.RepeatHitCooldownSeconds)
                });
            }

            ecb.SetComponentEnabled<Active>(entity, true);
            ecb.SetComponentEnabled<ProjectileCollisionActiveTag>(entity, NeedsCollision(cmd.HitPayload));
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
        }

        protected static bool NeedsCollision(in ProjectileHitPayload payload) =>
            payload.DirectDamageEnabled
            || payload.StackEffect.Enabled
            || payload.OnHitSpawn.Enabled;

        internal static ProjectileHitPayload HitPayloadFor(in ProjectileSpawnCommand cmd, CombatFaction faction)
        {
            CombatHitPayload hitPayload = cmd.HitPayload.HitPayload;
            StackEffectSnapshot stack = hitPayload.StackEffect;
            stack.Faction = faction;
            hitPayload.StackEffect = stack;
            return new ProjectileHitPayload(hitPayload, cmd.HitPayload.OnHitSpawn);
        }

        private readonly struct ProjectileSpawnKey : IEquatable<ProjectileSpawnKey>
        {
            private readonly int _batchId;

            public int BatchId => _batchId;

            public ProjectileSpawnKey(int batchId) { _batchId = batchId; }

            public bool Equals(ProjectileSpawnKey other) => _batchId == other._batchId;
            public override bool Equals(object obj) => obj is ProjectileSpawnKey k && Equals(k);
            public override int GetHashCode() => _batchId;
        }

        // Counting-sort the drained commands into per-key contiguous runs, in Burst.
        // Pass 1 assigns a dense bucket index per distinct typeId key and counts per bucket;
        // prefix-sum yields offsets; pass 2 scatters the command *indices* (4 bytes) into Order.
        // The large ProjectileSpawnCommand struct is never copied here.
        [BurstCompile]
        private struct BucketCommandsJob : IJob
        {
            [ReadOnly] public NativeArray<ProjectileSpawnCommand> Commands;
            public NativeHashMap<ProjectileSpawnKey, int> KeyToIndex;
            public NativeList<ProjectileSpawnKey> Keys;
            public NativeList<int> Counts;
            public NativeList<int> Offsets;
            public NativeArray<int> Order;

            public void Execute()
            {
                KeyToIndex.Clear();
                Keys.Clear();
                Counts.Clear();

                int count = Commands.Length;
                var bucketIds = new NativeArray<int>(count, Allocator.Temp);
                for (int i = 0; i < count; i++)
                {
                    ProjectileSpawnCommand cmd = Commands[i];
                    var key = new ProjectileSpawnKey(cmd.RenderTypeId);
                    if (!KeyToIndex.TryGetValue(key, out int idx))
                    {
                        idx = Keys.Length;
                        KeyToIndex.Add(key, idx);
                        Keys.Add(key);
                        Counts.Add(0);
                    }
                    bucketIds[i] = idx;
                    Counts[idx] = Counts[idx] + 1;
                }

                int buckets = Counts.Length;
                Offsets.ResizeUninitialized(buckets);
                var cursor = new NativeArray<int>(buckets, Allocator.Temp);
                int running = 0;
                for (int b = 0; b < buckets; b++)
                {
                    Offsets[b] = running;
                    running += Counts[b];
                }

                for (int i = 0; i < count; i++)
                {
                    int idx = bucketIds[i];
                    Order[Offsets[idx] + cursor[idx]] = i;
                    cursor[idx] = cursor[idx] + 1;
                }

                cursor.Dispose();
                bucketIds.Dispose();
            }
        }

        private readonly struct ProjectileSpawnWork
        {
            public readonly NativeArray<ProjectileSpawnCommand> Commands;
            public readonly NativeArray<int> Order;
            public readonly NativeReference<int> ClaimedCount;

            public ProjectileSpawnWork(
                NativeArray<ProjectileSpawnCommand> commands,
                NativeArray<int> order,
                NativeReference<int> claimedCount)
            {
                Commands = commands;
                Order = order;
                ClaimedCount = claimedCount;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateAfter(typeof(AoeSpawnExpansionSystem))]
    public sealed partial class BasicProjectileSpawnApplySystem : ProjectileSpawnApplySystemBase
    {
        public BasicProjectileSpawnApplySystem()
            : base(
                "BasicProjectileSpawnApplySystem",
                "BasicProjectileSpawnApplySystem.ReuseJob",
                "BasicProjectileSpawnApplySystem.Cold",
                "BasicProjectileSpawnApplySystem.Reuse")
        {
        }

        protected override EntityArchetype CreateArchetype() =>
            EntityManager.CreateArchetype(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(ProjectileTrackingComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(Active),
                typeof(ProjectileCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(ProjectileContactGateElement));

        protected override NativeList<ProjectileSpawnCommand> CommandContainer(
            ProjectileSpawnExpansionSystem expansionSys) =>
            expansionSys.BasicProjectileCommandContainer;

        protected override EntityQuery BuildDeadSlotQuery() =>
            new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>()
                .WithAll<CombatRenderBatchId>()
                .WithDisabled<Active>()
                .WithNone<TimedSpawnTag>()
                .Build(this);

        protected override JobHandle ScheduleReuseJob(
            NativeArray<ProjectileSpawnCommand> commands,
            NativeArray<int> order,
            NativeReference<int> claimedReference,
            EntityQuery query) =>
            new BasicProjectileSpawnJob
            {
                Commands = commands,
                Order = order,
                ClaimedCount = claimedReference,
                ActiveHandle = GetComponentTypeHandle<Active>(false),
                CollisionActiveHandle = GetComponentTypeHandle<ProjectileCollisionActiveTag>(false),
                RenderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                IdentityHandle = GetComponentTypeHandle<ProjectileIdentityComponent>(false),
                KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                CollisionHandle = GetComponentTypeHandle<CombatCollisionComponent>(false),
                LifetimeHandle = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                HitHandle = GetComponentTypeHandle<ProjectileHitComponent>(false),
                TrackingHandle = GetComponentTypeHandle<ProjectileTrackingComponent>(false),
                RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                RenderElementHandle = GetComponentTypeHandle<CombatRenderElement>(false),
                ContactGateHandle = GetBufferTypeHandle<ProjectileContactGateElement>(false),
            }.Schedule(query, default);

        protected override void CreateProjectileEntity(
            ProjectileSpawnCommand cmd,
            EntityArchetype archetype,
            EntityCommandBuffer ecb)
        {
            Entity entity = ecb.CreateEntity(archetype);
            ecb.AddSharedComponent(entity, new CombatRenderBatchId { Value = cmd.RenderTypeId });
            RecordCommonProjectileReset(ecb, entity, cmd.Faction, cmd);
        }

        [BurstCompile]
        private struct BasicProjectileSpawnJob : IJobChunk
        {
            [ReadOnly] public NativeArray<ProjectileSpawnCommand> Commands;
            [ReadOnly] public NativeArray<int> Order;
            [NativeDisableContainerSafetyRestriction] public NativeReference<int> ClaimedCount;

            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<Active> ActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileCollisionActiveTag> CollisionActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderActiveTag> RenderActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileIdentityComponent> IdentityHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileHitComponent> HitHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileTrackingComponent> TrackingHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderElement> RenderElementHandle;
            [NativeDisableContainerSafetyRestriction] public BufferTypeHandle<ProjectileContactGateElement> ContactGateHandle;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                int cfgIdx = ClaimedCount.Value;
                if (cfgIdx >= Order.Length) return;

                EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);
                EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                EnabledMask renderActiveMask = chunk.GetEnabledMask(ref RenderActiveHandle);
                EnabledMask trackingMask = chunk.GetEnabledMask(ref TrackingHandle);

                NativeArray<ProjectileIdentityComponent> identities = chunk.GetNativeArray(ref IdentityHandle);
                NativeArray<CombatKinematicsComponent> kinematics = chunk.GetNativeArray(ref KinematicsHandle);
                NativeArray<CombatCollisionComponent> collisions = chunk.GetNativeArray(ref CollisionHandle);
                EnabledMask lifetimeMask = chunk.GetEnabledMask(ref LifetimeHandle);
                NativeArray<CombatLifetimeComponent> lifetimes = chunk.GetNativeArray(ref LifetimeHandle);
                NativeArray<ProjectileHitComponent> hits = chunk.GetNativeArray(ref HitHandle);
                NativeArray<ProjectileTrackingComponent> tracking = chunk.GetNativeArray(ref TrackingHandle);
                NativeArray<CombatRenderComponent> renders = chunk.GetNativeArray(ref RenderHandle);
                NativeArray<CombatRenderElement> renderElems = chunk.GetNativeArray(ref RenderElementHandle);
                BufferAccessor<ProjectileContactGateElement> gates = chunk.GetBufferAccessor(ref ContactGateHandle);

                for (int i = 0; i < chunk.Count && cfgIdx < Order.Length; i++)
                {
                    if (activeMask[i]) continue;

                    ProjectileSpawnCommand cfg = Commands[Order[cfgIdx++]];

                    identities[i] = new ProjectileIdentityComponent
                    {
                        Faction = cfg.Faction,
                        ProjectileId = cfg.ProjectileId,
                        TypeId = cfg.TypeId
                    };
                    kinematics[i] = new CombatKinematicsComponent
                    {
                        Position = cfg.Position,
                        Velocity = cfg.Velocity
                    };
                    collisions[i] = new CombatCollisionComponent
                    {
                        ShapeType = cfg.ShapeType,
                        Radius = cfg.Radius,
                        HalfExtents = cfg.HalfExtents,
                        RotationRadians = cfg.RotationRadians,
                        BoundsMin = cfg.BoundsMin,
                        BoundsMax = cfg.BoundsMax
                    };
                    lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.Lifetime };
                    lifetimeMask[i] = true;
                    hits[i] = new ProjectileHitComponent
                    {
                        PierceRemaining = cfg.PierceRemaining,
                        RepeatHitCooldownSeconds = cfg.RepeatHitCooldownSeconds,
                        HitPayload = HitPayloadFor(in cfg, cfg.Faction)
                    };
                    tracking[i] = cfg.Tracking;
                    trackingMask[i] = cfg.Tracking.TrackingEnabled;
                    renders[i] = cfg.Render;
                    renderElems[i] = new CombatRenderElement();

                    DynamicBuffer<ProjectileContactGateElement> gate = gates[i];
                    gate.Clear();
                    if (cfg.SeedContactGateTargetId > 0)
                    {
                        gate.Add(new ProjectileContactGateElement
                        {
                            TargetId = cfg.SeedContactGateTargetId,
                            CooldownRemaining = math.max(0.1f, cfg.RepeatHitCooldownSeconds)
                        });
                    }

                    activeMask[i] = true;
                    collisionActiveMask[i] = NeedsCollision(cfg.HitPayload);
                    renderActiveMask[i] = true;
                }

                ClaimedCount.Value = cfgIdx;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateAfter(typeof(AoeSpawnExpansionSystem))]
    public sealed partial class ChildSpawnerProjectileSpawnApplySystem : ProjectileSpawnApplySystemBase
    {
        public ChildSpawnerProjectileSpawnApplySystem()
            : base(
                "ChildSpawnerProjectileSpawnApplySystem",
                "ChildSpawnerProjectileSpawnApplySystem.ReuseJob",
                "ChildSpawnerProjectileSpawnApplySystem.Cold",
                "ChildSpawnerProjectileSpawnApplySystem.Reuse")
        {
        }

        protected override EntityArchetype CreateArchetype() =>
            EntityManager.CreateArchetype(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(ProjectileTrackingComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(Active),
                typeof(ProjectileCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(ProjectileContactGateElement),
                typeof(TimedSpawnTag),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));

        protected override NativeList<ProjectileSpawnCommand> CommandContainer(
            ProjectileSpawnExpansionSystem expansionSys) =>
            expansionSys.ChildSpawnerProjectileCommandContainer;

        protected override EntityQuery BuildDeadSlotQuery() =>
            new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>()
                .WithAll<CombatRenderBatchId>()
                .WithAll<TimedSpawnTag>()
                .WithDisabled<Active>()
                .Build(this);

        protected override JobHandle ScheduleReuseJob(
            NativeArray<ProjectileSpawnCommand> commands,
            NativeArray<int> order,
            NativeReference<int> claimedReference,
            EntityQuery query) =>
            new ChildSpawnerProjectileSpawnJob
            {
                Commands = commands,
                Order = order,
                ClaimedCount = claimedReference,
                ActiveHandle = GetComponentTypeHandle<Active>(false),
                CollisionActiveHandle = GetComponentTypeHandle<ProjectileCollisionActiveTag>(false),
                RenderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                IdentityHandle = GetComponentTypeHandle<ProjectileIdentityComponent>(false),
                KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                CollisionHandle = GetComponentTypeHandle<CombatCollisionComponent>(false),
                LifetimeHandle = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                HitHandle = GetComponentTypeHandle<ProjectileHitComponent>(false),
                TrackingHandle = GetComponentTypeHandle<ProjectileTrackingComponent>(false),
                RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                RenderElementHandle = GetComponentTypeHandle<CombatRenderElement>(false),
                ContactGateHandle = GetBufferTypeHandle<ProjectileContactGateElement>(false),
                TimedSpawnHandle = GetComponentTypeHandle<TimedSpawnComponent>(false),
                TimedSpawnStateHandle = GetComponentTypeHandle<TimedSpawnStateComponent>(false),
            }.Schedule(query, default);

        protected override void CreateProjectileEntity(
            ProjectileSpawnCommand cmd,
            EntityArchetype archetype,
            EntityCommandBuffer ecb)
        {
            Entity entity = ecb.CreateEntity(archetype);
            ecb.AddSharedComponent(entity, new CombatRenderBatchId { Value = cmd.RenderTypeId });
            RecordCommonProjectileReset(ecb, entity, cmd.Faction, cmd);
            ecb.SetComponent(entity, cmd.TimedSpawn);
            ecb.SetComponent(entity, InitialTimedSpawnStateFor(cmd));
        }

        private static TimedSpawnStateComponent InitialTimedSpawnStateFor(in ProjectileSpawnCommand cmd)
        {
            TimedSpawnComponent timedSpawn = cmd.TimedSpawn;
            return new TimedSpawnStateComponent
            {
                CooldownRemaining = timedSpawn.IntervalSeconds
                    + DeterministicJitter(
                        cmd.ProjectileId,
                        timedSpawn.JitterSeed,
                        timedSpawn.IntervalJitterSeconds),
                TickIndex = 0
            };
        }

        private static float DeterministicJitter(int projectileId, int jitterSeed, float maxOffsetSeconds)
        {
            if (maxOffsetSeconds <= 0f)
            {
                return 0f;
            }

            unchecked
            {
                uint hash = (uint)projectileId;
                hash = (hash * 397u) ^ (uint)jitterSeed;
                hash *= 0x9E3779B9u;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return ((hash & 0x00FFFFFFu) + 1u) / 16777217f * maxOffsetSeconds;
            }
        }

        [BurstCompile]
        private struct ChildSpawnerProjectileSpawnJob : IJobChunk
        {
            [ReadOnly] public NativeArray<ProjectileSpawnCommand> Commands;
            [ReadOnly] public NativeArray<int> Order;
            [NativeDisableContainerSafetyRestriction] public NativeReference<int> ClaimedCount;

            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<Active> ActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileCollisionActiveTag> CollisionActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderActiveTag> RenderActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileIdentityComponent> IdentityHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileHitComponent> HitHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileTrackingComponent> TrackingHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderElement> RenderElementHandle;
            [NativeDisableContainerSafetyRestriction] public BufferTypeHandle<ProjectileContactGateElement> ContactGateHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<TimedSpawnComponent> TimedSpawnHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<TimedSpawnStateComponent> TimedSpawnStateHandle;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                int cfgIdx = ClaimedCount.Value;
                if (cfgIdx >= Order.Length) return;

                EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);
                EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                EnabledMask renderActiveMask = chunk.GetEnabledMask(ref RenderActiveHandle);
                EnabledMask trackingMask = chunk.GetEnabledMask(ref TrackingHandle);

                NativeArray<ProjectileIdentityComponent> identities = chunk.GetNativeArray(ref IdentityHandle);
                NativeArray<CombatKinematicsComponent> kinematics = chunk.GetNativeArray(ref KinematicsHandle);
                NativeArray<CombatCollisionComponent> collisions = chunk.GetNativeArray(ref CollisionHandle);
                EnabledMask lifetimeMask = chunk.GetEnabledMask(ref LifetimeHandle);
                NativeArray<CombatLifetimeComponent> lifetimes = chunk.GetNativeArray(ref LifetimeHandle);
                NativeArray<ProjectileHitComponent> hits = chunk.GetNativeArray(ref HitHandle);
                NativeArray<ProjectileTrackingComponent> tracking = chunk.GetNativeArray(ref TrackingHandle);
                NativeArray<CombatRenderComponent> renders = chunk.GetNativeArray(ref RenderHandle);
                NativeArray<CombatRenderElement> renderElems = chunk.GetNativeArray(ref RenderElementHandle);
                BufferAccessor<ProjectileContactGateElement> gates = chunk.GetBufferAccessor(ref ContactGateHandle);
                NativeArray<TimedSpawnComponent> timedSpawns =
                    chunk.GetNativeArray(ref TimedSpawnHandle);
                NativeArray<TimedSpawnStateComponent> timedSpawnStates =
                    chunk.GetNativeArray(ref TimedSpawnStateHandle);

                for (int i = 0; i < chunk.Count && cfgIdx < Order.Length; i++)
                {
                    if (activeMask[i]) continue;

                    ProjectileSpawnCommand cfg = Commands[Order[cfgIdx++]];

                    identities[i] = new ProjectileIdentityComponent
                    {
                        Faction = cfg.Faction,
                        ProjectileId = cfg.ProjectileId,
                        TypeId = cfg.TypeId
                    };
                    kinematics[i] = new CombatKinematicsComponent
                    {
                        Position = cfg.Position,
                        Velocity = cfg.Velocity
                    };
                    collisions[i] = new CombatCollisionComponent
                    {
                        ShapeType = cfg.ShapeType,
                        Radius = cfg.Radius,
                        HalfExtents = cfg.HalfExtents,
                        RotationRadians = cfg.RotationRadians,
                        BoundsMin = cfg.BoundsMin,
                        BoundsMax = cfg.BoundsMax
                    };
                    lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.Lifetime };
                    lifetimeMask[i] = true;
                    hits[i] = new ProjectileHitComponent
                    {
                        PierceRemaining = cfg.PierceRemaining,
                        RepeatHitCooldownSeconds = cfg.RepeatHitCooldownSeconds,
                        HitPayload = HitPayloadFor(in cfg, cfg.Faction)
                    };
                    tracking[i] = cfg.Tracking;
                    trackingMask[i] = cfg.Tracking.TrackingEnabled;
                    renders[i] = cfg.Render;
                    renderElems[i] = new CombatRenderElement();

                    DynamicBuffer<ProjectileContactGateElement> gate = gates[i];
                    gate.Clear();
                    if (cfg.SeedContactGateTargetId > 0)
                    {
                        gate.Add(new ProjectileContactGateElement
                        {
                            TargetId = cfg.SeedContactGateTargetId,
                            CooldownRemaining = math.max(0.1f, cfg.RepeatHitCooldownSeconds)
                        });
                    }

                    timedSpawns[i] = cfg.TimedSpawn;
                    timedSpawnStates[i] = InitialTimedSpawnStateFor(cfg);

                    activeMask[i] = true;
                    collisionActiveMask[i] = NeedsCollision(cfg.HitPayload);
                    renderActiveMask[i] = true;
                }

                ClaimedCount.Value = cfgIdx;
            }
        }
    }
}

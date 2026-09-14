using PlayGround.System.Combat;
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
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Projectiles
{
    // ECS Lifecycle: singleton projectile spawn lane; EventQueue + DiscreteCommands + ContinuousCommands created by
    // ProjectileSpawnExpansionSystem on create. EventQueue is filled by producers each frame and
    // drained by the expansion system; DiscreteCommands and ContinuousCommands are allocated per frame by the
    // expansion job and consumed by their matching ProjectileDiscreteSpawnApplySystem lane. Disposed by
    // ProjectileSpawnExpansionSystem on destroy.
    public struct ProjectileSpawnEventSingleton : IComponentData
    {
        public NativeQueue<ProjectileSpawnEvent> EventQueue;
        public NativeList<ProjectileSpawnCommand> DiscreteCommands;
        public NativeList<ProjectileSpawnCommand> ContinuousCommands;
        public JobHandle ProducerHandle;
        public JobHandle PendingHandle;
    }

    // Drains thin ProjectileSpawnEvent values, dereferences command-shaped templates,
    // stamps per-instance frame data, fans out by Count/Spread/Jitter, and writes
    // fully-resolved ProjectileSpawnCommand into apply-system containers.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimedSpawnSystem))]
    [UpdateAfter(typeof(ProjectileDiscreteCollisionSystem))]
    [UpdateAfter(typeof(PlayGround.System.Combat.Aoes.ImpactAoeCollisionSystem))]
    [UpdateAfter(typeof(StatusProcessSystem))]
    [UpdateAfter(typeof(TargetSpatialHashSystem))]
    [UpdateBefore(typeof(ProjectileDiscreteSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Combat.Aoes.ImpactAoeSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Combat.Aoes.LingeringAoeSpawnApplySystem))]
    public partial class ProjectileSpawnExpansionSystem : SystemBase
    {
        private EntityQuery _scopeQuery;
        private Entity singletonEntity;

        protected override void OnCreate()
        {
            singletonEntity = EntityManager.CreateEntity(typeof(ProjectileSpawnEventSingleton));
            EntityManager.SetComponentData(singletonEntity, new ProjectileSpawnEventSingleton
            {
                EventQueue = new NativeQueue<ProjectileSpawnEvent>(Allocator.Persistent)
            });

            _scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<ProjectileSpawnEvent>());
        }

        protected override void OnDestroy()
        {
            if (singletonEntity == Entity.Null
                || !EntityManager.Exists(singletonEntity)
                || !EntityManager.HasComponent<ProjectileSpawnEventSingleton>(singletonEntity))
            {
                return;
            }

            ProjectileSpawnEventSingleton singleton =
                EntityManager.GetComponentData<ProjectileSpawnEventSingleton>(singletonEntity);
            singleton.PendingHandle.Complete();
            singleton.ProducerHandle.Complete();
            if (singleton.DiscreteCommands.IsCreated)
            {
                singleton.DiscreteCommands.Dispose();
            }

            if (singleton.ContinuousCommands.IsCreated)
            {
                singleton.ContinuousCommands.Dispose();
            }

            if (singleton.EventQueue.IsCreated)
            {
                singleton.EventQueue.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            RefRW<ProjectileSpawnEventSingleton> projectileLane =
                SystemAPI.GetSingletonRW<ProjectileSpawnEventSingleton>();
            ref ProjectileSpawnEventSingleton singleton = ref projectileLane.ValueRW;

            singleton.PendingHandle.Complete();
            if (singleton.DiscreteCommands.IsCreated)
            {
                singleton.DiscreteCommands.Dispose();
                singleton.DiscreteCommands = default;
            }

            if (singleton.ContinuousCommands.IsCreated)
            {
                singleton.ContinuousCommands.Dispose();
                singleton.ContinuousCommands = default;
            }

            Dependency.Complete();
            singleton.ProducerHandle.Complete();
            singleton.ProducerHandle = default;

            int queueCount = singleton.EventQueue.Count;
            using NativeArray<Entity> scopes = _scopeQuery.ToEntityArray(Allocator.Temp);
            int bufferCount = 0;
            for (int s = 0; s < scopes.Length; s++)
            {
                bufferCount += EntityManager.GetBuffer<ProjectileSpawnEvent>(scopes[s]).Length;
            }

            int totalEvents = queueCount + bufferCount;
            if (totalEvents == 0)
            {
                singleton.PendingHandle = default;
                return;
            }

            var events = new NativeArray<ProjectileSpawnEvent>(totalEvents, Allocator.TempJob);
            int offset = 0;

            if (queueCount > 0)
            {
                NativeArray<ProjectileSpawnEvent> queued = singleton.EventQueue.ToArray(Allocator.Temp);
                NativeArray<ProjectileSpawnEvent>.Copy(queued, 0, events, offset, queued.Length);
                offset += queued.Length;
                queued.Dispose();
                singleton.EventQueue.Clear();
            }

            for (int s = 0; s < scopes.Length; s++)
            {
                DynamicBuffer<ProjectileSpawnEvent> buf = EntityManager.GetBuffer<ProjectileSpawnEvent>(scopes[s]);
                if (buf.Length > 0)
                {
                    NativeArray<ProjectileSpawnEvent>.Copy(buf.AsNativeArray(), 0, events, offset, buf.Length);
                    offset += buf.Length;
                    buf.Clear();
                }
            }

            if (!SystemAPI.TryGetSingleton(out ProjectileSpawnTemplate templates))
            {
                singleton.PendingHandle = events.Dispose(Dependency);
                Dependency = singleton.PendingHandle;
                return;
            }

            NativeList<ProjectileSpawnCommand> discreteCommands =
                new(events.Length, Allocator.TempJob);
            NativeList<ProjectileSpawnCommand> continuousCommands =
                new(events.Length, Allocator.TempJob);

            bool hasTargetHash = SystemAPI.TryGetSingleton(out TargetSpatialHashSingleton targetHash);
            CombatTargetAcquisition.Snapshot targetSnapshot = hasTargetHash
                ? new CombatTargetAcquisition.Snapshot(
                    targetHash.TargetEntities.AsArray(),
                    targetHash.TargetPositions.AsArray(),
                    targetHash.TargetShapes.AsArray(),
                    targetHash.TargetFactions.AsArray(),
                    targetHash.AoeOccupiedCells)
                : default;
            JobHandle expansionScheduleDependency = hasTargetHash
                ? JobHandle.CombineDependencies(Dependency, targetHash.BuildHandle)
                : Dependency;

            Dependency = new ProjectileExpansionJob
            {
                Events = events,
                Templates = templates.Map,
                DiscreteCommands = discreteCommands,
                ContinuousCommands = continuousCommands,
                HasTargetHash = hasTargetHash,
                TargetSnapshot = targetSnapshot
            }.Schedule(expansionScheduleDependency);

            Dependency = events.Dispose(Dependency);
            if (hasTargetHash)
            {
                RefRW<TargetSpatialHashSingleton> targetHashRw =
                    SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>();
                targetHashRw.ValueRW.ConsumerHandle =
                    JobHandle.CombineDependencies(targetHashRw.ValueRW.ConsumerHandle, Dependency);
            }

            singleton.DiscreteCommands = discreteCommands;
            singleton.ContinuousCommands = continuousCommands;
            singleton.PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct ProjectileExpansionJob : IJob
        {
            [ReadOnly] public NativeArray<ProjectileSpawnEvent> Events;
            [ReadOnly] public NativeHashMap<Hash128, ProjectileSpawnCommand> Templates;
            public NativeList<ProjectileSpawnCommand> DiscreteCommands;
            public NativeList<ProjectileSpawnCommand> ContinuousCommands;
            public bool HasTargetHash;
            public CombatTargetAcquisition.Snapshot TargetSnapshot;

            public void Execute()
            {
                for (int ci = 0; ci < Events.Length; ci++)
                {
                    ProjectileSpawnEvent evt = Events[ci];
                    if (evt.Kind != IntervalChildKind.Projectile
                        || !Templates.TryGetValue(evt.TemplateKey, out ProjectileSpawnCommand command))
                    {
                        continue;
                    }

                    Stamp(ref command, in evt);
                    int count = math.max(1, command.Count);

                    if (TryResolveAimedDirection(in command, out float2 aimedDirection))
                    {
                        CreateAimedNovaPattern(in command, count, aimedDirection);
                        continue;
                    }

                    // Root casts always use forward-volley behavior. Spawn patterns describe child waves only.
                    if (command.DeterministicIdTickIndex <= 0)
                    {
                        CreateForwardPattern(in command, count);
                        continue;
                    }

                    switch (command.SpawnPatternType)
                    {
                        case ProjectileChildSpawnPatternType.SideSpray:
                            var waveRng = new Random(IntervalWaveSeed(in command));
                            CreateSideSprayPattern(in command, count, ref waveRng);
                            break;

                        case ProjectileChildSpawnPatternType.Radial:
                            CreateRadialPattern(in command, count);
                            break;

                        case ProjectileChildSpawnPatternType.Forward:
                        default:
                            CreateForwardPattern(in command, count);
                            break;
                    }
                }
            }

            private bool TryResolveAimedDirection(in ProjectileSpawnCommand command, out float2 aimedDirection)
            {
                aimedDirection = default;
                if (!HasTargetHash
                    || command.LaunchAimMode != ProjectileLaunchAimMode.NearestHostile
                    || command.LaunchAimRange <= 0f
                    || command.Faction == CombatFaction.None)
                {
                    return false;
                }

                if (!CombatTargetAcquisition.TrySelectNthNearest(
                        TargetSnapshot,
                        command.Position,
                        command.LaunchAimRange,
                        0,
                        command.Faction,
                        command.SeedContactGateTargetId,
                        out Entity _,
                        out float2 targetPosition))
                {
                    return false;
                }

                float2 diff = targetPosition - command.Position;
                if (math.lengthsq(diff) <= 0.0001f)
                {
                    return false;
                }

                aimedDirection = math.normalize(diff);
                return true;
            }

            private void CreateAimedNovaPattern(in ProjectileSpawnCommand command, int count, float2 aimedDirection)
            {
                for (int i = 0; i < count; i++)
                {
                    int id = ProjectileIdFor(in command, i);
                    float2 direction = Rotate(aimedDirection, 360f * i / count);
                    WriteCommand(in command, id, direction * command.Speed);
                }
            }

            private void CreateSideSprayPattern(
                in ProjectileSpawnCommand command,
                int count,
                ref Random waveRng)
            {
                for (int i = 0; i < count; i++)
                {
                    int id = ProjectileIdFor(in command, i);
                    float2 velocity = IntervalSideSprayVelocity(in command, i, ref waveRng);
                    WriteCommand(in command, id, velocity);
                }
            }

            private void CreateRadialPattern(in ProjectileSpawnCommand command, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    int id = ProjectileIdFor(in command, i);
                    float2 velocity = RadialDirection(i, count) * command.Speed;
                    WriteCommand(in command, id, velocity);
                }
            }

            private void CreateForwardPattern(
                in ProjectileSpawnCommand command,
                int count)
            {
                if (count <= 1)
                {
                    WriteCommand(in command, ProjectileIdFor(in command, 0), command.BaseDirection * command.Speed);
                    return;
                }

                var spreadRng = new Random(command.JitterSeed != 0 ? command.JitterSeed : 1u);
                WriteForwardWave(in command, count, ref spreadRng);
            }

            private static void Stamp(ref ProjectileSpawnCommand command, in ProjectileSpawnEvent evt)
            {
                command.Faction = evt.Faction;
                command.ProjectileId = evt.SourceId;
                command.Position = evt.Position;
                command.JitterSeed = evt.JitterSeed;
                command.DeterministicIdTickIndex = evt.DeterministicIdTickIndex;
                command.SeedContactGateTargetId = evt.ContactGateSeedTargetId;
                if (math.lengthsq(evt.AimDirection) > 0.0001f)
                {
                    command.BaseDirection = math.normalize(evt.AimDirection);
                }
            }

            private void WriteCommand(in ProjectileSpawnCommand template, int projectileId, float2 velocity)
            {
                CombatCollisionMath.ComputeWorldBounds(
                    template.Position, template.Radius, template.HalfExtents, template.RotationRadians, template.ShapeType,
                    out float2 boundsMin, out float2 boundsMax);

                CombatRenderComponent render = template.Render;
                render.RenderZ = CombatRoot.ProjectileRenderZ
                    - (projectileId % CombatRoot.ProjectileRenderZSlots) * CombatRoot.ProjectileRenderZStep;

                TimedSpawnComponent timedSpawn = template.TimedSpawn;
                if (template.HasTimedSpawner != 0)
                {
                    timedSpawn.Faction = template.Faction;
                    timedSpawn.SourceId = projectileId;
                    // The compiled template shares one JitterSeed across every cast of this skill;
                    // restamp per spawner instance so each one's own interval-spawn waves diverge.
                    timedSpawn.JitterSeed = unchecked((int)((uint)projectileId * 2654435761u));
                }

                var command = template;
                command.ProjectileId = projectileId;
                command.Velocity = velocity;
                command.BoundsMin = boundsMin;
                command.BoundsMax = boundsMax;
                command.Render = render;
                command.TimedSpawn = timedSpawn;

                if (command.ContinuousCollision != 0)
                {
                    ContinuousCommands.Add(command);
                }
                else
                {
                    DiscreteCommands.Add(command);
                }

            }

            private static float SpreadAngle(float spread, int i, int count)
            {
                if (count <= 1)
                {
                    return 0f;
                }

                if ((count & 1) != 0)
                {
                    return -spread * 0.5f + spread / (count - 1) * i;
                }

                // Preserve symmetric spread while keeping a pair directly on the base direction.
                int shotsPerSide = (count - 2) / 2;
                if (i == shotsPerSide || i == shotsPerSide + 1)
                {
                    return 0f;
                }

                if (shotsPerSide == 0)
                {
                    return 0f;
                }

                float step = spread * 0.5f / shotsPerSide;
                return i < shotsPerSide
                    ? -spread * 0.5f + step * i
                    : step * (i - shotsPerSide - 1);
            }

            // Seeded per spawner instance (JitterSeed) and per wave (DeterministicIdTickIndex),
            // so no two spawners and no two waves from the same spawner roll the same shots.
            private static uint IntervalWaveSeed(in ProjectileSpawnCommand command)
            {
                uint seed = command.JitterSeed;
                seed = (seed * 397u) ^ (uint)command.DeterministicIdTickIndex;
                return seed != 0 ? seed : 1u;
            }

            // Half the shots fan left of the wave heading, half fan right;
            // each shot's angle is randomized within +/-SpreadDegrees/2 of that side's
            // perpendicular line (not the wave heading itself).
            private static float2 IntervalSideSprayVelocity(in ProjectileSpawnCommand command, int shotIndex, ref Random rng)
            {
                float2 forward = math.normalizesafe(command.BaseDirection, new float2(1f, 0f));
                float2 left = new float2(-forward.y, forward.x);
                float2 right = new float2(forward.y, -forward.x);
                float2 sideDirection = (shotIndex & 1) == 0 ? left : right;
                float halfSpread = command.SpreadDegrees * 0.5f;
                float angle = rng.NextFloat(-halfSpread, halfSpread);
                return Rotate(sideDirection, angle) * command.Speed;
            }

            private void WriteForwardWave(in ProjectileSpawnCommand command, int count, ref Random rng)
            {
                for (int i = 0; i < count; i++)
                {
                    float angle = SpreadAngle(command.SpreadDegrees, i, count);
                    if (command.JitterDegrees > 0f)
                    {
                        angle += rng.NextFloat(-command.JitterDegrees, command.JitterDegrees);
                    }

                    int id = ProjectileIdFor(in command, i);
                    WriteCommand(in command, id, Rotate(command.BaseDirection, angle) * command.Speed);
                }
            }

            private static float2 RadialDirection(int shotIndex, int shotCount)
            {
                if (shotCount <= 1)
                {
                    return new float2(1f, 0f);
                }

                float radians = math.PI * 2f * shotIndex / shotCount;
                math.sincos(radians, out float s, out float c);
                return new float2(c, s);
            }

            private static int ProjectileIdFor(in ProjectileSpawnCommand command, int shotIndex)
            {
                if (command.DeterministicIdTickIndex <= 0)
                {
                    return command.ProjectileId + shotIndex;
                }

                unchecked
                {
                    int hash = command.ProjectileId;
                    hash = (hash * 397) ^ (int)command.JitterSeed;
                    hash = (hash * 397) ^ command.DeterministicIdTickIndex;
                    hash = (hash * 397) ^ shotIndex;
                    hash &= int.MaxValue;
                    return hash == 0 ? 1 : hash;
                }
            }

            private static float2 Rotate(float2 v, float degrees)
            {
                math.sincos(math.radians(degrees), out float s, out float c);
                return new float2(c * v.x - s * v.y, s * v.x + c * v.y);
            }
        }
    }
}

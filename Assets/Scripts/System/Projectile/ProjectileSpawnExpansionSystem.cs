using PlayGround.System.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    // ECS Lifecycle: singleton projectile spawn lane; EventQueue + Commands created by
    // ProjectileSpawnExpansionSystem on create. EventQueue is filled by producers each frame and
    // drained by the expansion system; Commands is allocated per frame by the expansion job and
    // consumed by ProjectileSpawnApplySystem. Disposed by ProjectileSpawnExpansionSystem on destroy.
    public struct ProjectileSpawnEventSingleton : IComponentData
    {
        public NativeQueue<ProjectileSpawnEvent> EventQueue;
        public NativeList<ProjectileSpawnCommand> Commands;
        public JobHandle ProducerHandle;
        public JobHandle PendingHandle;
    }

    // Drains thin ProjectileSpawnEvent values, dereferences command-shaped templates,
    // stamps per-instance frame data, fans out by Count/Spread/Jitter, and writes
    // fully-resolved ProjectileSpawnCommand into apply-system containers.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimedSpawnSystem))]
    [UpdateAfter(typeof(ProjectileCollisionSystem))]
    [UpdateAfter(typeof(PlayGround.System.Aoe.ImpactAoeCollisionSystem))]
    [UpdateAfter(typeof(StatusProcessSystem))]
    [UpdateBefore(typeof(ProjectileSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Aoe.ImpactAoeSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Aoe.LingeringAoeSpawnApplySystem))]
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
            if (singleton.Commands.IsCreated)
            {
                singleton.Commands.Dispose();
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
            if (singleton.Commands.IsCreated)
            {
                singleton.Commands.Dispose();
                singleton.Commands = default;
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

            NativeList<ProjectileSpawnCommand> commands =
                new(events.Length, Allocator.TempJob);

            Dependency = new ProjectileExpansionJob
            {
                Events = events,
                Templates = templates.Map,
                Commands = commands
            }.Schedule(Dependency);

            Dependency = events.Dispose(Dependency);
            singleton.Commands = commands;
            singleton.PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct ProjectileExpansionJob : IJob
        {
            [ReadOnly] public NativeArray<ProjectileSpawnEvent> Events;
            [ReadOnly] public NativeHashMap<Hash128, ProjectileSpawnCommand> Templates;
            public NativeList<ProjectileSpawnCommand> Commands;

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

                    if (command.DeterministicIdTickIndex > 0
                        && command.SpawnPatternType == ProjectileChildSpawnPatternType.SideSpray)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int id = ProjectileIdFor(in command, i);
                            float2 velocity = SideSprayVelocity(in command, i, count);
                            WriteCommand(in command, id, velocity);
                        }
                    }
                    else if (command.DeterministicIdTickIndex > 0
                        && command.SpawnPatternType == ProjectileChildSpawnPatternType.Radial)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int id = ProjectileIdFor(in command, i);
                            float2 velocity = RadialDirection(i, count) * command.Speed;
                            WriteCommand(in command, id, velocity);
                        }
                    }
                    else if (count <= 1)
                    {
                        WriteCommand(in command, ProjectileIdFor(in command, 0), command.BaseDirection * command.Speed);
                    }
                    else
                    {
                        var rng = new Random(command.JitterSeed != 0 ? command.JitterSeed : 1u);
                        for (int i = 0; i < count; i++)
                        {
                            float angle = SpreadAngle(command.SpreadDegrees, i, count);
                            if (command.JitterDegrees > 0f)
                            {
                                angle += rng.NextFloat(-command.JitterDegrees, command.JitterDegrees);
                            }

                            int id = ProjectileIdFor(in command, i);
                            float2 velocity = Rotate(command.BaseDirection, angle) * command.Speed;
                            WriteCommand(in command, id, velocity);
                        }
                    }
                }
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
                ProjectileCollisionMath.ComputeWorldBounds(
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
                }

                var command = template;
                command.ProjectileId = projectileId;
                command.Velocity = velocity;
                command.BoundsMin = boundsMin;
                command.BoundsMax = boundsMax;
                command.Render = render;
                command.TimedSpawn = timedSpawn;

                Commands.Add(command);
            }

            private static float SpreadAngle(float spread, int i, int count) =>
                count <= 1 ? 0f : -spread * 0.5f + spread / (count - 1) * i;

            private static float2 SideSprayVelocity(in ProjectileSpawnCommand command, int shotIndex, int shotCount)
            {
                float2 forward = math.normalizesafe(command.BaseDirection, new float2(1f, 0f));
                float2 left = new float2(-forward.y, forward.x);
                float2 right = new float2(forward.y, -forward.x);
                bool isLeft = (shotIndex & 1) == 0;
                int leftCount = (shotCount + 1) / 2;
                int rightCount = shotCount / 2;
                int sideIndex = shotIndex / 2;
                int sideCount = isLeft ? leftCount : rightCount;
                float2 sideDirection = isLeft ? left : right;
                float angle = SpreadAngle(command.SpreadDegrees, sideIndex, sideCount);
                return Rotate(sideDirection, angle) * command.Speed;
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

using PlayGround.System.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
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

        internal NativeQueue<ProjectileSpawnEvent> EventQueue;
        internal NativeStream ProjectileCommandStream;
        internal JobHandle PendingHandle;
        internal JobHandle ProducerHandle;

        protected override void OnCreate()
        {
            EventQueue = new NativeQueue<ProjectileSpawnEvent>(Allocator.Persistent);
            _scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<ProjectileSpawnEvent>());
        }

        protected override void OnDestroy()
        {
            PendingHandle.Complete();
            if (ProjectileCommandStream.IsCreated)
            {
                ProjectileCommandStream.Dispose();
            }

            EventQueue.Dispose();
        }

        protected override void OnUpdate()
        {
            if (ProjectileCommandStream.IsCreated)
            {
                ProjectileCommandStream.Dispose();
            }

            Dependency.Complete();
            ProducerHandle.Complete();
            ProducerHandle = default;

            int queueCount = EventQueue.Count;
            using NativeArray<Entity> scopes = _scopeQuery.ToEntityArray(Allocator.Temp);
            int bufferCount = 0;
            for (int s = 0; s < scopes.Length; s++)
            {
                bufferCount += EntityManager.GetBuffer<ProjectileSpawnEvent>(scopes[s]).Length;
            }

            int totalEvents = queueCount + bufferCount;
            if (totalEvents == 0)
            {
                PendingHandle = default;
                return;
            }

            var events = new NativeArray<ProjectileSpawnEvent>(totalEvents, Allocator.TempJob);
            int offset = 0;

            if (queueCount > 0)
            {
                NativeArray<ProjectileSpawnEvent> queued = EventQueue.ToArray(Allocator.Temp);
                NativeArray<ProjectileSpawnEvent>.Copy(queued, 0, events, offset, queued.Length);
                offset += queued.Length;
                queued.Dispose();
                EventQueue.Clear();
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
                PendingHandle = events.Dispose(Dependency);
                Dependency = PendingHandle;
                return;
            }

            ProjectileCommandStream = new NativeStream(events.Length, Allocator.TempJob);

            Dependency = new ProjectileExpansionJob
            {
                Events = events,
                Templates = templates.Map,
                Commands = ProjectileCommandStream.AsWriter()
            }.Schedule(events.Length, 1, Dependency);

            Dependency = events.Dispose(Dependency);
            PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct ProjectileExpansionJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ProjectileSpawnEvent> Events;
            [ReadOnly] public NativeHashMap<Hash128, ProjectileSpawnCommand> Templates;
            public NativeStream.Writer Commands;

            public void Execute(int ci)
            {
                ProjectileSpawnEvent evt = Events[ci];
                if (evt.Kind != IntervalChildKind.Projectile
                    || !Templates.TryGetValue(evt.TemplateKey, out ProjectileSpawnCommand command))
                {
                    return;
                }

                Commands.BeginForEachIndex(ci);
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

                Commands.EndForEachIndex();
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

                Commands.Write(command);
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

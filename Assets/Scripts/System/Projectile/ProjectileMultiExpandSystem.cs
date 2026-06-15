using PlayGround.System.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    // Expands multi-shot spawn commands (Count > 1) into individual spawn elements (Count == 1).
    // Single-shot commands (Count == 1) pass through unchanged.
    // Output is a NativeStream consumed and disposed by ProjectileSpawnSystem.
    // Must run after both collision systems so internal hit-spawns converted to
    // ProjectileSpawnRequestElement this frame are expanded and spawned same-frame.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileCollisionSystem))]
    [UpdateAfter(typeof(PlayGround.System.Aoe.AoeCollisionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnSystem))]
    public partial class ProjectileMultiExpandSystem : SystemBase
    {
        private EntityQuery scopeQuery;

        // Produced each frame; read and disposed by ProjectileSpawnSystem.
        internal NativeStream PendingStream;
        internal JobHandle PendingHandle;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileScope>(),
                ComponentType.ReadWrite<ProjectileSpawnRequestElement>());
        }

        protected override void OnDestroy()
        {
            if (PendingStream.IsCreated)
                PendingStream.Dispose();
        }

        protected override void OnUpdate()
        {
            if (PendingStream.IsCreated)
                PendingStream.Dispose();

            Dependency.Complete();

            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);

            int totalCommands = 0;
            for (int s = 0; s < scopes.Length; s++)
                totalCommands += EntityManager.GetBuffer<ProjectileSpawnRequestElement>(scopes[s]).Length;

            if (totalCommands == 0)
            {
                PendingStream = default;
                return;
            }

            var commands = new NativeArray<ProjectileSpawnRequestElement>(totalCommands, Allocator.TempJob);
            int offset = 0;
            for (int s = 0; s < scopes.Length; s++)
            {
                DynamicBuffer<ProjectileSpawnRequestElement> buf =
                    EntityManager.GetBuffer<ProjectileSpawnRequestElement>(scopes[s]);
                for (int i = 0; i < buf.Length; i++)
                    commands[offset++] = buf[i];
                buf.Clear();
            }

            PendingStream = new NativeStream(totalCommands, Allocator.TempJob);

            Dependency = new ProjectileMultiExpandJob
            {
                Commands = commands,
                Stream   = PendingStream.AsWriter()
            }.Schedule(Dependency);

            Dependency = commands.Dispose(Dependency);
            PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct ProjectileMultiExpandJob : IJob
        {
            [ReadOnly] public NativeArray<ProjectileSpawnRequestElement> Commands;
            public NativeStream.Writer Stream;

            public void Execute()
            {
                for (int ci = 0; ci < Commands.Length; ci++)
                {
                    ProjectileSpawnRequestElement cmd = Commands[ci];
                    Stream.BeginForEachIndex(ci);

                    if (cmd.Count <= 1)
                    {
                        Stream.Write(cmd);
                    }
                    else
                    {
                        var rng = new Random(cmd.JitterSeed != 0 ? cmd.JitterSeed : 1u);
                        for (int i = 0; i < cmd.Count; i++)
                        {
                            float angle = SpreadAngle(cmd.SpreadDegrees, i, cmd.Count);
                            if (cmd.JitterDegrees > 0f)
                                angle += rng.NextFloat(-cmd.JitterDegrees, cmd.JitterDegrees);

                            ProjectileSpawnRequestElement elem = cmd;
                            elem.Count        = 1;
                            elem.ProjectileId = cmd.ProjectileId + i;
                            elem.Velocity     = Rotate(cmd.BaseDirection, angle) * cmd.Speed;
                            elem.Render.RenderZ = ProjectileRoot.ProjectileRenderZ
                                - (elem.ProjectileId % ProjectileRoot.ProjectileRenderZSlots)
                                * ProjectileRoot.ProjectileRenderZStep;
                            Stream.Write(elem);
                        }
                    }

                    Stream.EndForEachIndex();
                }
            }

            private static float SpreadAngle(float spread, int i, int count) =>
                count <= 1 ? 0f : -spread * 0.5f + spread / (count - 1) * i;

            private static float2 Rotate(float2 v, float degrees)
            {
                math.sincos(math.radians(degrees), out float s, out float c);
                return new float2(c * v.x - s * v.y, s * v.x + c * v.y);
            }
        }
    }
}

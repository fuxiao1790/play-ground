using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct ProjectileRenderPrepareSystem : ISystem
    {
        private const float ProjectileRenderZ = -0.25f;
        private EntityQuery projectileQuery;

        public void OnCreate(ref SystemState state)
        {
            projectileQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<ProjectileComponent>(),
                ComponentType.ReadOnly<ProjectileRenderComponent>(),
                ComponentType.ReadOnly<ProjectileActiveTag>());
        }

        public void OnUpdate(ref SystemState state)
        {
            state.Dependency.Complete();
            foreach (DynamicBuffer<ProjectileRenderElement> renderBuffer in SystemAPI.Query<DynamicBuffer<ProjectileRenderElement>>())
            {
                renderBuffer.Clear();
            }

            int capacity = math.max(1, projectileQuery.CalculateEntityCount());
            var pendingRender = new NativeParallelMultiHashMap<int, ProjectilePendingRender>(capacity, Allocator.TempJob);
            var prepareJob = new ProjectileRenderPrepareJob
            {
                PendingRender = pendingRender.AsParallelWriter()
            };

            JobHandle prepareHandle = prepareJob.ScheduleParallel(state.Dependency);
            prepareHandle.Complete();

            NativeArray<int> typeKeys = pendingRender.GetKeyArray(Allocator.TempJob);
            typeKeys.SortJob().Schedule().Complete();

            var flushJob = new ProjectileRenderFlushJob
            {
                PendingRender = pendingRender,
                TypeKeys = typeKeys,
                RenderBuffers = SystemAPI.GetBufferLookup<ProjectileRenderElement>()
            };

            flushJob.Schedule().Complete();
            typeKeys.Dispose();
            pendingRender.Dispose();
            state.Dependency = default;
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileRenderPrepareJob : IJobEntity
        {
            public NativeParallelMultiHashMap<int, ProjectilePendingRender>.ParallelWriter PendingRender;

            private void Execute(in ProjectileComponent projectile, in ProjectileRenderComponent render)
            {
                if (render.IsRenderable == 0 || projectile.Scope == Entity.Null)
                {
                    return;
                }

                float velocityLengthSquared = math.lengthsq(projectile.Velocity);
                float directionX = 1f;
                float directionY = 0f;
                if (velocityLengthSquared > ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                {
                    float inverseLength = math.rsqrt(velocityLengthSquared);
                    directionX = projectile.Velocity.x * inverseLength;
                    directionY = projectile.Velocity.y * inverseLength;
                }

                float cos = directionX * render.VisualRotationCos - directionY * render.VisualRotationSin;
                float sin = directionX * render.VisualRotationSin + directionY * render.VisualRotationCos;

                float scale = render.VisualScale;
                float rightX = cos * scale;
                float rightY = sin * scale;
                float upX = -sin * scale;
                float upY = cos * scale;

                PendingRender.Add(projectile.TypeId, new ProjectilePendingRender
                {
                    Scope = projectile.Scope,
                    Matrix = new float4x4(
                        new float4(rightX, rightY, 0f, 0f),
                        new float4(upX, upY, 0f, 0f),
                        new float4(0f, 0f, scale, 0f),
                        new float4(projectile.Position.x, projectile.Position.y, ProjectileRenderZ, 1f))
                });
            }
        }

        [BurstCompile]
        private struct ProjectileRenderFlushJob : IJob
        {
            [ReadOnly] public NativeParallelMultiHashMap<int, ProjectilePendingRender> PendingRender;
            [ReadOnly] public NativeArray<int> TypeKeys;
            public BufferLookup<ProjectileRenderElement> RenderBuffers;

            public void Execute()
            {
                int previousTypeId = int.MinValue;
                for (int i = 0; i < TypeKeys.Length; i++)
                {
                    int typeId = TypeKeys[i];
                    if (typeId == previousTypeId)
                    {
                        continue;
                    }

                    previousTypeId = typeId;
                    if (!PendingRender.TryGetFirstValue(typeId, out ProjectilePendingRender pending, out NativeParallelMultiHashMapIterator<int> iterator))
                    {
                        continue;
                    }

                    do
                    {
                        if (pending.Scope == Entity.Null || !RenderBuffers.HasBuffer(pending.Scope))
                        {
                            continue;
                        }

                        RenderBuffers[pending.Scope].Add(new ProjectileRenderElement
                        {
                            TypeId = typeId,
                            Matrix = pending.Matrix
                        });
                    }
                    while (PendingRender.TryGetNextValue(out pending, ref iterator));
                }
            }
        }
    }
}

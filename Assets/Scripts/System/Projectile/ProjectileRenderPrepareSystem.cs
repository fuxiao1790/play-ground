using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ProjectileCollisionSystem))]
    public partial struct ProjectileRenderPrepareSystem : ISystem
    {
        private const float ProjectileRenderZ = -0.25f;
        private EntityQuery renderType0Query;
        private EntityQuery renderType1Query;
        private EntityQuery renderType2Query;
        private EntityQuery renderType3Query;
        private EntityQuery renderType4Query;
        private EntityQuery renderType5Query;
        private EntityQuery renderType6Query;
        private EntityQuery renderType7Query;
        private EntityQuery renderType8Query;
        private EntityQuery renderType9Query;
        private EntityQuery renderType10Query;
        private EntityQuery renderType11Query;
        private EntityQuery renderType12Query;
        private EntityQuery renderType13Query;
        private EntityQuery renderType14Query;
        private EntityQuery renderType15Query;
        private ComponentTypeHandle<ProjectileKinematicsComponent> kinematicsTypeHandle;
        private ComponentTypeHandle<ProjectileRenderComponent> renderTypeHandle;
        private ComponentTypeHandle<ProjectileRenderElement> renderElementTypeHandle;

        public void OnCreate(ref SystemState state)
        {
            renderType0Query  = RenderTypeQuery<ProjectileRenderType0Tag>(ref state);
            renderType1Query  = RenderTypeQuery<ProjectileRenderType1Tag>(ref state);
            renderType2Query  = RenderTypeQuery<ProjectileRenderType2Tag>(ref state);
            renderType3Query  = RenderTypeQuery<ProjectileRenderType3Tag>(ref state);
            renderType4Query  = RenderTypeQuery<ProjectileRenderType4Tag>(ref state);
            renderType5Query  = RenderTypeQuery<ProjectileRenderType5Tag>(ref state);
            renderType6Query  = RenderTypeQuery<ProjectileRenderType6Tag>(ref state);
            renderType7Query  = RenderTypeQuery<ProjectileRenderType7Tag>(ref state);
            renderType8Query  = RenderTypeQuery<ProjectileRenderType8Tag>(ref state);
            renderType9Query  = RenderTypeQuery<ProjectileRenderType9Tag>(ref state);
            renderType10Query = RenderTypeQuery<ProjectileRenderType10Tag>(ref state);
            renderType11Query = RenderTypeQuery<ProjectileRenderType11Tag>(ref state);
            renderType12Query = RenderTypeQuery<ProjectileRenderType12Tag>(ref state);
            renderType13Query = RenderTypeQuery<ProjectileRenderType13Tag>(ref state);
            renderType14Query = RenderTypeQuery<ProjectileRenderType14Tag>(ref state);
            renderType15Query = RenderTypeQuery<ProjectileRenderType15Tag>(ref state);

            kinematicsTypeHandle    = state.GetComponentTypeHandle<ProjectileKinematicsComponent>(true);
            renderTypeHandle        = state.GetComponentTypeHandle<ProjectileRenderComponent>(true);
            renderElementTypeHandle = state.GetComponentTypeHandle<ProjectileRenderElement>(false);
        }

        public void OnUpdate(ref SystemState state)
        {
            kinematicsTypeHandle.Update(ref state);
            renderTypeHandle.Update(ref state);
            renderElementTypeHandle.Update(ref state);

            var job = new ProjectileRenderPrepareJob
            {
                Kinematics      = kinematicsTypeHandle,
                RenderComponents = renderTypeHandle,
                RenderElements  = renderElementTypeHandle
            };

            JobHandle h = state.Dependency;
            h = job.ScheduleParallel(renderType0Query,  h);
            h = job.ScheduleParallel(renderType1Query,  h);
            h = job.ScheduleParallel(renderType2Query,  h);
            h = job.ScheduleParallel(renderType3Query,  h);
            h = job.ScheduleParallel(renderType4Query,  h);
            h = job.ScheduleParallel(renderType5Query,  h);
            h = job.ScheduleParallel(renderType6Query,  h);
            h = job.ScheduleParallel(renderType7Query,  h);
            h = job.ScheduleParallel(renderType8Query,  h);
            h = job.ScheduleParallel(renderType9Query,  h);
            h = job.ScheduleParallel(renderType10Query, h);
            h = job.ScheduleParallel(renderType11Query, h);
            h = job.ScheduleParallel(renderType12Query, h);
            h = job.ScheduleParallel(renderType13Query, h);
            h = job.ScheduleParallel(renderType14Query, h);
            h = job.ScheduleParallel(renderType15Query, h);

            state.Dependency = h;
            state.Dependency.Complete();
        }

        private static EntityQuery RenderTypeQuery<T>(ref SystemState state)
            where T : unmanaged, IComponentData
        {
            return state.GetEntityQuery(
                ComponentType.ReadOnly<ProjectileKinematicsComponent>(),
                ComponentType.ReadOnly<ProjectileRenderComponent>(),
                ComponentType.ReadWrite<ProjectileRenderElement>(),
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<ProjectileActiveTag>());
        }

        [BurstCompile]
        private struct ProjectileRenderPrepareJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ProjectileKinematicsComponent> Kinematics;
            [ReadOnly] public ComponentTypeHandle<ProjectileRenderComponent> RenderComponents;
            public ComponentTypeHandle<ProjectileRenderElement> RenderElements;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                NativeArray<ProjectileKinematicsComponent> kin  = chunk.GetNativeArray(ref Kinematics);
                NativeArray<ProjectileRenderComponent>     rend = chunk.GetNativeArray(ref RenderComponents);
                NativeArray<ProjectileRenderElement>       elem = chunk.GetNativeArray(ref RenderElements);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    if (rend[i].IsRenderable == 0)
                    {
                        continue;
                    }

                    ProjectileKinematicsComponent k = kin[i];
                    ProjectileRenderComponent r = rend[i];

                    float velocityLengthSquared = math.lengthsq(k.Velocity);
                    float directionX = 1f;
                    float directionY = 0f;
                    if (velocityLengthSquared > ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                    {
                        float inverseLength = math.rsqrt(velocityLengthSquared);
                        directionX = k.Velocity.x * inverseLength;
                        directionY = k.Velocity.y * inverseLength;
                    }

                    float cos   = directionX * r.VisualRotationCos - directionY * r.VisualRotationSin;
                    float sin   = directionX * r.VisualRotationSin + directionY * r.VisualRotationCos;
                    float scale = r.VisualScale;

                    elem[i] = new ProjectileRenderElement
                    {
                        objectToWorld = new Matrix4x4
                        {
                            m00 = cos * scale,
                            m01 = -sin * scale,
                            m02 = 0f,
                            m03 = k.Position.x,
                            m10 = sin * scale,
                            m11 = cos * scale,
                            m12 = 0f,
                            m13 = k.Position.y,
                            m20 = 0f,
                            m21 = 0f,
                            m22 = scale,
                            m23 = ProjectileRenderZ,
                            m30 = 0f,
                            m31 = 0f,
                            m32 = 0f,
                            m33 = 1f
                        }
                    };
                }
            }
        }
    }
}

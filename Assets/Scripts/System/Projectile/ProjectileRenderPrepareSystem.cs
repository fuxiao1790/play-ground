using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        private EntityQuery batchType0Query;
        private EntityQuery batchType1Query;
        private EntityQuery batchType2Query;
        private EntityQuery batchType3Query;
        private EntityQuery batchType4Query;
        private EntityQuery batchType5Query;
        private EntityQuery batchType6Query;
        private EntityQuery batchType7Query;
        private EntityQuery batchType8Query;
        private EntityQuery batchType9Query;
        private EntityQuery batchType10Query;
        private EntityQuery batchType11Query;
        private EntityQuery batchType12Query;
        private EntityQuery batchType13Query;
        private EntityQuery batchType14Query;
        private EntityQuery batchType15Query;
        private ComponentTypeHandle<ProjectileIdentityComponent> identityTypeHandle;
        private ComponentTypeHandle<ProjectileKinematicsComponent> kinematicsTypeHandle;
        private ComponentTypeHandle<ProjectileRenderComponent> renderTypeHandle;

        public void OnCreate(ref SystemState state)
        {
            renderType0Query = RenderTypeQuery<ProjectileRenderType0Tag>(ref state);
            renderType1Query = RenderTypeQuery<ProjectileRenderType1Tag>(ref state);
            renderType2Query = RenderTypeQuery<ProjectileRenderType2Tag>(ref state);
            renderType3Query = RenderTypeQuery<ProjectileRenderType3Tag>(ref state);
            renderType4Query = RenderTypeQuery<ProjectileRenderType4Tag>(ref state);
            renderType5Query = RenderTypeQuery<ProjectileRenderType5Tag>(ref state);
            renderType6Query = RenderTypeQuery<ProjectileRenderType6Tag>(ref state);
            renderType7Query = RenderTypeQuery<ProjectileRenderType7Tag>(ref state);
            renderType8Query = RenderTypeQuery<ProjectileRenderType8Tag>(ref state);
            renderType9Query = RenderTypeQuery<ProjectileRenderType9Tag>(ref state);
            renderType10Query = RenderTypeQuery<ProjectileRenderType10Tag>(ref state);
            renderType11Query = RenderTypeQuery<ProjectileRenderType11Tag>(ref state);
            renderType12Query = RenderTypeQuery<ProjectileRenderType12Tag>(ref state);
            renderType13Query = RenderTypeQuery<ProjectileRenderType13Tag>(ref state);
            renderType14Query = RenderTypeQuery<ProjectileRenderType14Tag>(ref state);
            renderType15Query = RenderTypeQuery<ProjectileRenderType15Tag>(ref state);

            batchType0Query = BatchTypeQuery<ProjectileRenderType0BatchTag>(ref state);
            batchType1Query = BatchTypeQuery<ProjectileRenderType1BatchTag>(ref state);
            batchType2Query = BatchTypeQuery<ProjectileRenderType2BatchTag>(ref state);
            batchType3Query = BatchTypeQuery<ProjectileRenderType3BatchTag>(ref state);
            batchType4Query = BatchTypeQuery<ProjectileRenderType4BatchTag>(ref state);
            batchType5Query = BatchTypeQuery<ProjectileRenderType5BatchTag>(ref state);
            batchType6Query = BatchTypeQuery<ProjectileRenderType6BatchTag>(ref state);
            batchType7Query = BatchTypeQuery<ProjectileRenderType7BatchTag>(ref state);
            batchType8Query = BatchTypeQuery<ProjectileRenderType8BatchTag>(ref state);
            batchType9Query = BatchTypeQuery<ProjectileRenderType9BatchTag>(ref state);
            batchType10Query = BatchTypeQuery<ProjectileRenderType10BatchTag>(ref state);
            batchType11Query = BatchTypeQuery<ProjectileRenderType11BatchTag>(ref state);
            batchType12Query = BatchTypeQuery<ProjectileRenderType12BatchTag>(ref state);
            batchType13Query = BatchTypeQuery<ProjectileRenderType13BatchTag>(ref state);
            batchType14Query = BatchTypeQuery<ProjectileRenderType14BatchTag>(ref state);
            batchType15Query = BatchTypeQuery<ProjectileRenderType15BatchTag>(ref state);

            // Cache component type handles to avoid creating them every OnUpdate.
            identityTypeHandle = state.GetComponentTypeHandle<ProjectileIdentityComponent>(true);
            kinematicsTypeHandle = state.GetComponentTypeHandle<ProjectileKinematicsComponent>(true);
            renderTypeHandle = state.GetComponentTypeHandle<ProjectileRenderComponent>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            ComponentLookup<ProjectileRenderBatch> batchLookup = SystemAPI.GetComponentLookup<ProjectileRenderBatch>(true);
            BufferLookup<ProjectileRenderElement> renderBuffers = SystemAPI.GetBufferLookup<ProjectileRenderElement>();
            // Keep cached handles up to date instead of creating them each frame.
            identityTypeHandle.Update(ref state);
            kinematicsTypeHandle.Update(ref state);
            renderTypeHandle.Update(ref state);

            JobHandle renderHandle = state.Dependency;
            renderHandle = PrepareRenderType(renderType0Query, batchType0Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType1Query, batchType1Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType2Query, batchType2Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType3Query, batchType3Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType4Query, batchType4Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType5Query, batchType5Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType6Query, batchType6Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType7Query, batchType7Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType8Query, batchType8Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType9Query, batchType9Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType10Query, batchType10Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType11Query, batchType11Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType12Query, batchType12Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType13Query, batchType13Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType14Query, batchType14Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);
            renderHandle = PrepareRenderType(renderType15Query, batchType15Query, batchLookup, renderBuffers, identityTypeHandle, kinematicsTypeHandle, renderTypeHandle, renderHandle);

            // todo: is this actually needed?
            state.Dependency = renderHandle;
        }

        private static EntityQuery RenderTypeQuery<T>(ref SystemState state)
            where T : unmanaged, IComponentData
        {
            return state.GetEntityQuery(
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<ProjectileKinematicsComponent>(),
                ComponentType.ReadOnly<ProjectileRenderComponent>(),
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<ProjectileActiveTag>());
        }

        private static EntityQuery BatchTypeQuery<T>(ref SystemState state)
            where T : unmanaged, IComponentData
        {
            return state.GetEntityQuery(
                ComponentType.ReadOnly<ProjectileRenderBatch>(),
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadWrite<ProjectileRenderElement>());
        }

        private JobHandle PrepareRenderType(
            EntityQuery projectileQuery,
            EntityQuery batchQuery,
            ComponentLookup<ProjectileRenderBatch> batchLookup,
            BufferLookup<ProjectileRenderElement> renderBuffers,
            ComponentTypeHandle<ProjectileIdentityComponent> identityTypeHandle,
            ComponentTypeHandle<ProjectileKinematicsComponent> kinematicsTypeHandle,
            ComponentTypeHandle<ProjectileRenderComponent> renderTypeHandle,
            JobHandle dependency)
        {
            using NativeArray<Entity> batchEntities = batchQuery.ToEntityArray(Allocator.Temp);
            if (batchEntities.Length == 0)
            {
                return dependency;
            }

            int capacity = projectileQuery.CalculateEntityCount();
            if (capacity <= 0)
            {
                for (int i = 0; i < batchEntities.Length; i++)
                {
                    renderBuffers[batchEntities[i]].Clear();
                }

                return dependency;
            }

            using var renderCounts = new NativeArray<int>(batchEntities.Length, Allocator.TempJob);
            JobHandle typeHandle = default;
            bool scheduledAny = false;
            for (int i = 0; i < batchEntities.Length; i++)
            {
                Entity batchEntity = batchEntities[i];
                ProjectileRenderBatch batch = batchLookup[batchEntity];
                DynamicBuffer<ProjectileRenderElement> renderBuffer = renderBuffers[batchEntity];
                renderBuffer.Clear();

                if (batch.Scope == Entity.Null)
                {
                    continue;
                }

                JobHandle batchHandle = PrepareBatch(
                    projectileQuery,
                    batch.Scope,
                    i,
                    ref renderBuffer,
                    renderCounts,
                    capacity,
                    identityTypeHandle,
                    kinematicsTypeHandle,
                    renderTypeHandle,
                    dependency);
                typeHandle = JobHandle.CombineDependencies(typeHandle, batchHandle);
                scheduledAny = true;
            }

            if (!scheduledAny)
            {
                return dependency;
            }

            typeHandle.Complete();
            for (int i = 0; i < batchEntities.Length; i++)
            {
                DynamicBuffer<ProjectileRenderElement> renderBuffer = renderBuffers[batchEntities[i]];
                renderBuffer.ResizeUninitialized(renderCounts[i]);
            }

            return typeHandle;
        }

        private JobHandle PrepareBatch(
            EntityQuery projectileQuery,
            Entity scope,
            int renderCountIndex,
            ref DynamicBuffer<ProjectileRenderElement> renderBuffer,
            NativeArray<int> renderCounts,
            int capacity,
            ComponentTypeHandle<ProjectileIdentityComponent> identityTypeHandle,
            ComponentTypeHandle<ProjectileKinematicsComponent> kinematicsTypeHandle,
            ComponentTypeHandle<ProjectileRenderComponent> renderTypeHandle,
            JobHandle dependency)
        {
            renderBuffer.ResizeUninitialized(capacity);
            var prepareJob = new ProjectileRenderPrepareJob
            {
                Scope = scope,
                Identities = identityTypeHandle,
                Kinematics = kinematicsTypeHandle,
                RenderComponents = renderTypeHandle,
                RenderElements = renderBuffer.AsNativeArray(),
                RenderCounts = renderCounts,
                RenderCountIndex = renderCountIndex
            };

            return prepareJob.Schedule(projectileQuery, dependency);
        }

        [BurstCompile]
        private struct ProjectileRenderPrepareJob : IJobChunk
        {
            public Entity Scope;
            [ReadOnly] public ComponentTypeHandle<ProjectileIdentityComponent> Identities;
            [ReadOnly] public ComponentTypeHandle<ProjectileKinematicsComponent> Kinematics;
            [ReadOnly] public ComponentTypeHandle<ProjectileRenderComponent> RenderComponents;
            [NativeDisableParallelForRestriction] public NativeArray<ProjectileRenderElement> RenderElements;
            [NativeDisableParallelForRestriction] public NativeArray<int> RenderCounts;
            public int RenderCountIndex;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                NativeArray<ProjectileIdentityComponent> identities = chunk.GetNativeArray(ref Identities);
                NativeArray<ProjectileKinematicsComponent> kinematicsComponents = chunk.GetNativeArray(ref Kinematics);
                NativeArray<ProjectileRenderComponent> renderComponents = chunk.GetNativeArray(ref RenderComponents);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    ProjectileIdentityComponent identity = identities[i];
                    ProjectileKinematicsComponent kinematics = kinematicsComponents[i];
                    ProjectileRenderComponent render = renderComponents[i];
                    if (render.IsRenderable == 0 || identity.Scope != Scope)
                    {
                        continue;
                    }

                    int writeIndex = RenderCounts[RenderCountIndex];
                    RenderElements[writeIndex] = BuildRenderElement(kinematics, render);
                    RenderCounts[RenderCountIndex] = writeIndex + 1;
                }
            }

            private static ProjectileRenderElement BuildRenderElement(
                ProjectileKinematicsComponent kinematics,
                ProjectileRenderComponent render)
            {
                float velocityLengthSquared = math.lengthsq(kinematics.Velocity);
                float directionX = 1f;
                float directionY = 0f;
                if (velocityLengthSquared > ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                {
                    float inverseLength = math.rsqrt(velocityLengthSquared);
                    directionX = kinematics.Velocity.x * inverseLength;
                    directionY = kinematics.Velocity.y * inverseLength;
                }

                float cos = directionX * render.VisualRotationCos - directionY * render.VisualRotationSin;
                float sin = directionX * render.VisualRotationSin + directionY * render.VisualRotationCos;

                float scale = render.VisualScale;
                float rightX = cos * scale;
                float rightY = sin * scale;
                float upX = -sin * scale;
                float upY = cos * scale;

                return new ProjectileRenderElement
                {
                    objectToWorld = new Matrix4x4
                    {
                        m00 = rightX,
                        m01 = upX,
                        m02 = 0f,
                        m03 = kinematics.Position.x,
                        m10 = rightY,
                        m11 = upY,
                        m12 = 0f,
                        m13 = kinematics.Position.y,
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

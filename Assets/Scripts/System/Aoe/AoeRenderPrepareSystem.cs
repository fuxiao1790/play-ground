using PlayGround.System.Common;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AoeSpawnSystem))]
    [UpdateBefore(typeof(AoeCollisionSystem))]
    public partial struct AoeRenderPrepareSystem : ISystem
    {
        private const float AoeRenderZ = -0.2f;
        private EntityQuery renderQuery;
        private ComponentTypeHandle<CombatKinematicsComponent> kinematicsTypeHandle;
        private ComponentTypeHandle<AoeRenderComponent> renderTypeHandle;
        private ComponentTypeHandle<AoeRenderElement> renderElementTypeHandle;

        public void OnCreate(ref SystemState state)
        {
            renderQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>(),
                ComponentType.ReadOnly<AoeRenderComponent>(),
                ComponentType.ReadWrite<AoeRenderElement>(),
                ComponentType.ReadOnly<AoeActiveTag>());
            state.RequireForUpdate(renderQuery);

            kinematicsTypeHandle    = state.GetComponentTypeHandle<CombatKinematicsComponent>(true);
            renderTypeHandle        = state.GetComponentTypeHandle<AoeRenderComponent>(true);
            renderElementTypeHandle = state.GetComponentTypeHandle<AoeRenderElement>(false);
        }

        public void OnUpdate(ref SystemState state)
        {
            kinematicsTypeHandle.Update(ref state);
            renderTypeHandle.Update(ref state);
            renderElementTypeHandle.Update(ref state);

            var job = new AoeRenderPrepareJob
            {
                Kinematics = kinematicsTypeHandle,
                RenderComponents = renderTypeHandle,
                RenderElements = renderElementTypeHandle
            };

            state.Dependency = job.ScheduleParallel(renderQuery, state.Dependency);
        }

        [BurstCompile]
        private struct AoeRenderPrepareJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<CombatKinematicsComponent> Kinematics;
            [ReadOnly] public ComponentTypeHandle<AoeRenderComponent> RenderComponents;
            public ComponentTypeHandle<AoeRenderElement> RenderElements;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                NativeArray<CombatKinematicsComponent> kin = chunk.GetNativeArray(ref Kinematics);
                NativeArray<AoeRenderComponent> rend = chunk.GetNativeArray(ref RenderComponents);
                NativeArray<AoeRenderElement> elem = chunk.GetNativeArray(ref RenderElements);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    if (rend[i].IsRenderable == 0)
                    {
                        continue;
                    }

                    CombatKinematicsComponent k = kin[i];
                    AoeRenderComponent r = rend[i];
                    float2 scale = r.VisualScale;

                    elem[i] = new AoeRenderElement
                    {
                        objectToWorld = new Matrix4x4
                        {
                            m00 = r.VisualRotationCos * scale.x,
                            m01 = -r.VisualRotationSin * scale.y,
                            m02 = 0f,
                            m03 = k.Position.x,
                            m10 = r.VisualRotationSin * scale.x,
                            m11 = r.VisualRotationCos * scale.y,
                            m12 = 0f,
                            m13 = k.Position.y,
                            m20 = 0f,
                            m21 = 0f,
                            m22 = math.max(scale.x, scale.y),
                            m23 = AoeRenderZ,
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

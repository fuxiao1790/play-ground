using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: common render component; owned by renderable domain entities; lifecycle is defined by each domain tag/scope.
    public struct CombatRenderComponent : IComponentData
    {
        public int IsRenderable;
        public int AlignToVelocity;
        public float2 VisualScale;
        public float VisualRotationSin;
        public float VisualRotationCos;
        public float RenderZ;
    }

    // ECS Lifecycle: common render component; owned by renderable domain entities; overwritten during render prep.
    public struct CombatRenderElement : IComponentData
    {
        public Matrix4x4 objectToWorld;
    }

    // ECS Lifecycle: common render enable tag; owned by renderable domain entities; enabled/disabled with the owning domain active tag.
    public struct CombatRenderActiveTag : IComponentData, IEnableableComponent
    {
    }

    // ECS Lifecycle: shared render component; added at entity creation; kept until owning domain root teardown.
    public struct CombatRenderScope : ISharedComponentData, global::System.IEquatable<CombatRenderScope>
    {
        public Entity Scope;
        public readonly bool Equals(CombatRenderScope other) => Scope == other.Scope;
        public override int GetHashCode() => Scope.GetHashCode();
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CombatRenderPrepareSystem : ISystem
    {
        private const float MinimumDirectionLengthSquared = 0.000001f;
        private EntityQuery renderQuery;
        private ComponentTypeHandle<CombatKinematicsComponent> kinematicsTypeHandle;
        private ComponentTypeHandle<CombatRenderComponent> renderTypeHandle;
        private ComponentTypeHandle<CombatRenderElement> renderElementTypeHandle;

        public void OnCreate(ref SystemState state)
        {
            renderQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<CombatKinematicsComponent>(),
                ComponentType.ReadOnly<CombatRenderComponent>(),
                ComponentType.ReadWrite<CombatRenderElement>(),
                ComponentType.ReadOnly<CombatRenderActiveTag>());
            state.RequireForUpdate(renderQuery);

            kinematicsTypeHandle = state.GetComponentTypeHandle<CombatKinematicsComponent>(true);
            renderTypeHandle = state.GetComponentTypeHandle<CombatRenderComponent>(true);
            renderElementTypeHandle = state.GetComponentTypeHandle<CombatRenderElement>(false);
        }

        public void OnUpdate(ref SystemState state)
        {
            kinematicsTypeHandle.Update(ref state);
            renderTypeHandle.Update(ref state);
            renderElementTypeHandle.Update(ref state);

            state.Dependency = new CombatRenderPrepareJob
            {
                Kinematics = kinematicsTypeHandle,
                RenderComponents = renderTypeHandle,
                RenderElements = renderElementTypeHandle
            }.ScheduleParallel(renderQuery, state.Dependency);
        }

        [BurstCompile]
        private struct CombatRenderPrepareJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<CombatKinematicsComponent> Kinematics;
            [ReadOnly] public ComponentTypeHandle<CombatRenderComponent> RenderComponents;
            public ComponentTypeHandle<CombatRenderElement> RenderElements;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                NativeArray<CombatKinematicsComponent> kin = chunk.GetNativeArray(ref Kinematics);
                NativeArray<CombatRenderComponent> rend = chunk.GetNativeArray(ref RenderComponents);
                NativeArray<CombatRenderElement> elem = chunk.GetNativeArray(ref RenderElements);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    CombatRenderComponent r = rend[i];
                    if (r.IsRenderable == 0)
                    {
                        continue;
                    }

                    CombatKinematicsComponent k = kin[i];
                    float directionX = 1f;
                    float directionY = 0f;
                    if (r.AlignToVelocity != 0)
                    {
                        float velocityLengthSquared = math.lengthsq(k.Velocity);
                        if (velocityLengthSquared > MinimumDirectionLengthSquared)
                        {
                            float inverseLength = math.rsqrt(velocityLengthSquared);
                            directionX = k.Velocity.x * inverseLength;
                            directionY = k.Velocity.y * inverseLength;
                        }
                    }

                    float cos = directionX * r.VisualRotationCos - directionY * r.VisualRotationSin;
                    float sin = directionX * r.VisualRotationSin + directionY * r.VisualRotationCos;
                    float2 scale = r.VisualScale;

                    elem[i] = new CombatRenderElement
                    {
                        objectToWorld = new Matrix4x4
                        {
                            m00 = cos * scale.x,
                            m01 = -sin * scale.y,
                            m02 = 0f,
                            m03 = k.Position.x,
                            m10 = sin * scale.x,
                            m11 = cos * scale.y,
                            m12 = 0f,
                            m13 = k.Position.y,
                            m20 = 0f,
                            m21 = 0f,
                            m22 = math.max(scale.x, scale.y),
                            m23 = r.RenderZ,
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

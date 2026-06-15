using System.Collections.Generic;
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

    // ECS Lifecycle: shared render component; added at entity creation; kept until owning domain root teardown; partitions render chunks by type without structural archetype cost.
    public struct CombatRenderTypeId : ISharedComponentData, global::System.IEquatable<CombatRenderTypeId>
    {
        public int TypeId;
        public readonly bool Equals(CombatRenderTypeId other) => TypeId == other.TypeId;
        public override int GetHashCode() => TypeId;
    }

    public static class CombatRenderMatrixUtility
    {
        private const float MinimumDirectionLengthSquared = 0.000001f;

        public static CombatRenderElement ElementFor(
            CombatKinematicsComponent kinematics,
            CombatRenderComponent render)
        {
            if (render.IsRenderable == 0)
            {
                return default;
            }

            float directionX = 1f;
            float directionY = 0f;
            if (render.AlignToVelocity != 0)
            {
                float velocityLengthSquared = math.lengthsq(kinematics.Velocity);
                if (velocityLengthSquared > MinimumDirectionLengthSquared)
                {
                    float inverseLength = math.rsqrt(velocityLengthSquared);
                    directionX = kinematics.Velocity.x * inverseLength;
                    directionY = kinematics.Velocity.y * inverseLength;
                }
            }

            float cos = directionX * render.VisualRotationCos - directionY * render.VisualRotationSin;
            float sin = directionX * render.VisualRotationSin + directionY * render.VisualRotationCos;
            float2 scale = render.VisualScale;

            return new CombatRenderElement
            {
                objectToWorld = new Matrix4x4
                {
                    m00 = cos * scale.x,
                    m01 = -sin * scale.y,
                    m02 = 0f,
                    m03 = kinematics.Position.x,
                    m10 = sin * scale.x,
                    m11 = cos * scale.y,
                    m12 = 0f,
                    m13 = kinematics.Position.y,
                    m20 = 0f,
                    m21 = 0f,
                    m22 = math.max(scale.x, scale.y),
                    m23 = render.RenderZ,
                    m30 = 0f,
                    m31 = 0f,
                    m32 = 0f,
                    m33 = 1f
                }
            };
        }
    }

    // ECS Lifecycle: blittable scope component; added at CombatRoot setup; holds
    // only the owning root's id. Render resources live in a static int-keyed
    // registry on CombatRoot (mirrors CombatScopeVfxCatalog/CombatVfxRoot), so one
    // shared scope can serve both projectile and AOE render resources.
    public struct CombatScopeRenderCatalog : IComponentData
    {
        public int RootId;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CombatRenderPrepareSystem : ISystem
    {
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
                    elem[i] = CombatRenderMatrixUtility.ElementFor(kin[i], rend[i]);
                }
            }
        }
    }
}

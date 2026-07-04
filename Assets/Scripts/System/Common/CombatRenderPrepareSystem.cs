using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine.Assertions;

namespace PlayGround.System.Common
{
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial class CombatRenderPrepareSystem : SystemBase
    {
        private EntityQuery renderPrepareQuery;

        private ComponentTypeHandle<CombatKinematicsComponent> kinematicsHandle;
        private ComponentTypeHandle<CombatRenderAuthoring> authoringHandle;
        private ComponentTypeHandle<CombatRenderComponent> renderHandle;
        private ComponentTypeHandle<CombatRenderActiveTag> renderActiveHandle;

        protected override void OnCreate()
        {
            Assert.AreEqual(32, UnsafeUtility.SizeOf<CombatRenderComponent>());

            renderPrepareQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatKinematicsComponent>()
                .WithAll<CombatRenderComponent>()
                .WithAll<CombatRenderAuthoring>()
                .WithAll<CombatRenderActiveTag>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);

            kinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(true);
            authoringHandle = GetComponentTypeHandle<CombatRenderAuthoring>(true);
            renderHandle = GetComponentTypeHandle<CombatRenderComponent>(false);
            renderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(true);
        }

        protected override void OnUpdate()
        {
            kinematicsHandle.Update(this);
            authoringHandle.Update(this);
            renderHandle.Update(this);
            renderActiveHandle.Update(this);

            Dependency = new RenderPrepareJob
            {
                Kinematics = kinematicsHandle,
                Authoring = authoringHandle,
                RenderComponents = renderHandle,
                RenderActive = renderActiveHandle
            }.ScheduleParallel(renderPrepareQuery, Dependency);
        }

        [BurstCompile]
        private struct RenderPrepareJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<CombatKinematicsComponent> Kinematics;
            [ReadOnly] public ComponentTypeHandle<CombatRenderAuthoring> Authoring;
            public ComponentTypeHandle<CombatRenderComponent> RenderComponents;
            [ReadOnly] public ComponentTypeHandle<CombatRenderActiveTag> RenderActive;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                NativeArray<CombatKinematicsComponent> kin = chunk.GetNativeArray(ref Kinematics);
                NativeArray<CombatRenderAuthoring> auth = chunk.GetNativeArray(ref Authoring);
                NativeArray<CombatRenderComponent> rend = chunk.GetNativeArray(ref RenderComponents);
                EnabledMask activeMask = chunk.GetEnabledMask(ref RenderActive);

                for (int i = 0; i < chunk.Count; i++)
                {
                    CombatRenderComponent component = rend[i];
                    rend[i] = activeMask[i]
                        ? CombatRenderMatrixUtility.ElementFor(kin[i], auth[i], component)
                        : CombatRenderMatrixUtility.DegenerateInstance(component);
                }
            }
        }
    }
}

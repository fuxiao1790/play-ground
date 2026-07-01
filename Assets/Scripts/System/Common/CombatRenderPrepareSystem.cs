using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Common
{
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial class CombatRenderPrepareSystem : SystemBase
    {
        private EntityQuery renderPrepareQuery;

        private ComponentTypeHandle<CombatKinematicsComponent> kinematicsHandle;
        private ComponentTypeHandle<CombatRenderComponent> renderHandle;
        private ComponentTypeHandle<CombatRenderElement> elementHandle;

        protected override void OnCreate()
        {
            renderPrepareQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatKinematicsComponent>()
                .WithAll<CombatRenderComponent>()
                .WithAll<CombatRenderElement>()
                .WithAll<CombatRenderActiveTag>()
                .Build(this);

            kinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(true);
            renderHandle = GetComponentTypeHandle<CombatRenderComponent>(true);
            elementHandle = GetComponentTypeHandle<CombatRenderElement>(false);
        }

        protected override void OnUpdate()
        {
            kinematicsHandle.Update(this);
            renderHandle.Update(this);
            elementHandle.Update(this);

            Dependency = new RenderPrepareJob
            {
                Kinematics = kinematicsHandle,
                RenderComponents = renderHandle,
                RenderElements = elementHandle
            }.ScheduleParallel(renderPrepareQuery, Dependency);
        }

        [BurstCompile]
        private struct RenderPrepareJob : IJobChunk
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

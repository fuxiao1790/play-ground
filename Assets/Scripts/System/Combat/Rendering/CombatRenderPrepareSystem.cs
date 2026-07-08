using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine.Assertions;

namespace PlayGround.System.Combat.Rendering
{
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial class CombatRenderPrepareSystem : SystemBase
    {
        private EntityQuery renderPrepareQuery;

        private ComponentTypeHandle<CombatKinematicsComponent> kinematicsHandle;
        private ComponentTypeHandle<CombatRenderAuthoring> authoringHandle;
        private ComponentTypeHandle<CombatRenderComponent> renderHandle;
        private ComponentTypeHandle<Active> activeHandle;
        private ComponentTypeHandle<ArmingTag> armingHandle;

        protected override void OnCreate()
        {
            Assert.AreEqual(32, UnsafeUtility.SizeOf<CombatRenderComponent>());

            renderPrepareQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatKinematicsComponent>()
                .WithAll<CombatRenderComponent>()
                .WithAll<CombatRenderAuthoring>()
                .WithAll<Active>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);

            kinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(true);
            authoringHandle = GetComponentTypeHandle<CombatRenderAuthoring>(true);
            renderHandle = GetComponentTypeHandle<CombatRenderComponent>(false);
            activeHandle = GetComponentTypeHandle<Active>(true);
            armingHandle = GetComponentTypeHandle<ArmingTag>(true);
        }

        protected override void OnUpdate()
        {
            kinematicsHandle.Update(this);
            authoringHandle.Update(this);
            renderHandle.Update(this);
            activeHandle.Update(this);
            armingHandle.Update(this);

            Dependency = new RenderPrepareJob
            {
                Kinematics = kinematicsHandle,
                Authoring = authoringHandle,
                RenderComponents = renderHandle,
                Active = activeHandle,
                Arming = armingHandle
            }.ScheduleParallel(renderPrepareQuery, Dependency);
        }

        [BurstCompile]
        private struct RenderPrepareJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<CombatKinematicsComponent> Kinematics;
            [ReadOnly] public ComponentTypeHandle<CombatRenderAuthoring> Authoring;
            public ComponentTypeHandle<CombatRenderComponent> RenderComponents;
            [ReadOnly] public ComponentTypeHandle<Active> Active;
            [ReadOnly] public ComponentTypeHandle<ArmingTag> Arming;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                NativeArray<CombatKinematicsComponent> kin = chunk.GetNativeArray(ref Kinematics);
                NativeArray<CombatRenderAuthoring> auth = chunk.GetNativeArray(ref Authoring);
                NativeArray<CombatRenderComponent> rend = chunk.GetNativeArray(ref RenderComponents);
                EnabledMask activeMask = chunk.GetEnabledMask(ref Active);
                bool hasArming = chunk.Has(ref Arming);
                EnabledMask armingMask = hasArming ? chunk.GetEnabledMask(ref Arming) : default;

                for (int i = 0; i < chunk.Count; i++)
                {
                    CombatRenderComponent component = rend[i];
                    bool visible = activeMask[i] && (!hasArming || !armingMask[i]);
                    rend[i] = visible
                        ? CombatRenderMatrixUtility.ElementFor(kin[i], auth[i], component)
                        : CombatRenderMatrixUtility.DegenerateInstance(component);
                }
            }
        }
    }
}

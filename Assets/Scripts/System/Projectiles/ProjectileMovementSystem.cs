using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Projectiles
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileTrackingSystem))]
    [UpdateBefore(typeof(ProjectileContactGateSystem))]
    public partial struct ProjectileMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            RefRW<CombatAoeVfxDispatchSingleton> vfx =
                SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();

            var job = new ProjectileMovementJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                LineSegmentVfxPending = vfx.ValueRO.PendingLineSegmentSpawns.AsParallelWriter()
            };

            JobHandle handle = job.ScheduleParallel(state.Dependency);
            vfx.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(vfx.ValueRW.ProducerHandle, handle);
            state.Dependency = handle;
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct ProjectileMovementJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<LineSegmentVfxEvent>.ParallelWriter LineSegmentVfxPending;

            private void Execute(
                ref CombatKinematicsComponent kinematics,
                ref CombatCollisionComponent collision,
                ref ProjectileTrailVfxComponent trailVfx)
            {
                kinematics.Position += kinematics.Velocity * DeltaTime;
                CombatCollisionMath.ComputeWorldBounds(
                    kinematics.Position,
                    collision.Radius,
                    collision.HalfExtents,
                    collision.RotationRadians,
                    collision.ShapeType,
                    out collision.BoundsMin,
                    out collision.BoundsMax);

                if (trailVfx.TrailId <= 0)
                {
                    return;
                }

                float distanceSq = math.lengthsq(kinematics.Position - trailVfx.LastEmitPosition);
                float stepSq = trailVfx.StepDistance * trailVfx.StepDistance;
                if (distanceSq < stepSq)
                {
                    return;
                }

                VfxEmit.EnqueueLineSegment(
                    trailVfx.TrailId,
                    trailVfx.LastEmitPosition,
                    kinematics.Position,
                    trailVfx.Width,
                    LineSegmentVfxPending);
                trailVfx.LastEmitPosition = kinematics.Position;
            }
        }
    }
}

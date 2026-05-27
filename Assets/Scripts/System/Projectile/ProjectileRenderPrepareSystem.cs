using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct ProjectileRenderPrepareSystem : ISystem
    {
        private const float ProjectileRenderZ = -0.25f;

        public void OnUpdate(ref SystemState state)
        {
            new ProjectileRenderPrepareJob().ScheduleParallel(state.Dependency).Complete();
            state.Dependency = default;
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileRenderPrepareJob : IJobEntity
        {
            private void Execute(in ProjectileComponent projectile, ref ProjectileRenderComponent render)
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

                render.PreparedMatrix = new float4x4(
                    new float4(rightX, rightY, 0f, 0f),
                    new float4(upX, upY, 0f, 0f),
                    new float4(0f, 0f, scale, 0f),
                    new float4(projectile.Position.x, projectile.Position.y, ProjectileRenderZ, 1f));
            }
        }
    }
}

using PlayGround.System.Aoe;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Common
{
    // Reads the hit-spawn stream and appends AoE spawn requests to the scope buffer.
    // Projectile consequences (impact-projectile, burst) are now emitted directly by
    // collision systems into ProjectileSpawnExpansionSystem.EventQueue (Task 004).
    // This job is removed in Task 007 once AoE also emits typed events.
    [BurstCompile]
    public struct CombatSpawnConvertJob : IJob
    {
        private const int ImpactAoeIdSalt = 0x5F1A0E;

        public Entity Scope;
        public NativeStream PendingSpawns;
        public BufferLookup<AoeSpawnRequestElement> AoeRequests;

        public void Execute()
        {
            NativeStream.Reader reader = PendingSpawns.AsReader();
            for (int i = 0; i < reader.ForEachCount; i++)
            {
                int count = reader.BeginForEachIndex(i);
                for (int j = 0; j < count; j++)
                {
                    Convert(reader.Read<CombatPendingSpawn>());
                }

                reader.EndForEachIndex();
            }
        }

        private void Convert(in CombatPendingSpawn pending)
        {
            if (pending.Faction == CombatFaction.None)
            {
                return;
            }

            if (pending.ImpactAoe.Enabled)
            {
                AppendImpactAoe(in pending);
            }
        }

        private void AppendImpactAoe(in CombatPendingSpawn pending)
        {
            Entity dest = Scope;
            if (!AoeRequests.HasBuffer(dest))
            {
                return;
            }

            ProjectileImpactAoeSnapshot impact = pending.ImpactAoe;
            AoeSpawnGeometry geo = impact.Geometry;
            float2 position = pending.Position;
            float2 halfExtents = new float2(geo.HalfExtents.x, geo.HalfExtents.y);
            CombatCollisionMath.ComputeWorldBounds(
                position, geo.Radius, halfExtents, geo.RotationRadians, geo.ShapeType,
                out float2 boundsMin, out float2 boundsMax);

            CombatRenderComponent render = default;
            if (geo.VisualScale.x > 0f || geo.VisualScale.y > 0f)
            {
                render = new CombatRenderComponent
                {
                    IsRenderable = 1,
                    AlignToVelocity = 0,
                    VisualScale = new float2(geo.VisualScale.x, geo.VisualScale.y),
                    VisualRotationSin = geo.VisualRotationSin,
                    VisualRotationCos = geo.VisualRotationCos,
                    RenderZ = CombatRoot.AoeRenderZ
                };
            }

            AoeRequests[dest].Add(new AoeSpawnRequestElement
            {
                Faction = pending.Faction,
                AoeId = HashId(pending.SourceId, pending.TypeId, pending.TargetId, ImpactAoeIdSalt),
                TypeId = impact.TypeId,
                Lifetime = impact.LifetimeSeconds,
                RepeatHitCooldownSeconds = impact.TickIntervalSeconds,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = impact.DamageAmount,
                    CritChance = impact.CritChance,
                    CritMultiplier = impact.CritMultiplier,
                    DirectDamageEnabled = true,
                    SourceNodeId = pending.SourceNodeId,
                    StackEffect = default
                },
                AreaSize = geo.AreaSize,
                Radius = geo.Radius,
                RotationRadians = geo.RotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                ShapeType = geo.ShapeType,
                Render = render,
                ProjectileBurst = default
            });
        }

        private static int HashId(int a, int b, int c, int salt)
        {
            unchecked
            {
                int hash = salt;
                hash = (hash * 397) ^ a;
                hash = (hash * 397) ^ b;
                hash = (hash * 397) ^ c;
                hash &= int.MaxValue;
                return hash == 0 ? 1 : hash;
            }
        }
    }
}

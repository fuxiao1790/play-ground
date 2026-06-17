using PlayGround.System.Common;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Aoe
{
    // ECS Lifecycle: transient spawn intent; enqueued by producers into the expansion queue or appended to the scope submission buffer; consumed and discarded by AoeSpawnExpansionSystem.
    public struct AoeSpawnEvent : IBufferElementData
    {
        public CombatFaction Faction;
        public int AoeId;
        public int TypeId;
        public float Lifetime;
        public float RepeatHitCooldownSeconds;
        public CombatHitPayload HitPayload;
        public float AreaSize;
        public float Radius;
        public float RotationRadians;
        public float2 Position;
        public float2 HalfExtents;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public CombatShapeType ShapeType;
        public CombatRenderComponent Render;
        public AoeProjectileBurstSnapshot ProjectileBurst;
    }

    // ECS Lifecycle: resolved single-entity allocation intent; produced by expansion, consumed by apply; never carries multiplicity.
    public struct AoeSpawnCommandData
    {
        public CombatFaction Faction;
        public int AoeId;
        public int TypeId;
        public float Lifetime;
        public float RepeatHitCooldownSeconds;
        public CombatHitPayload HitPayload;
        public float AreaSize;
        public float Radius;
        public float RotationRadians;
        public float2 Position;
        public float2 HalfExtents;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public CombatShapeType ShapeType;
        public CombatRenderComponent Render;
        public AoeProjectileBurstSnapshot ProjectileBurst;
    }

    internal static class AoeSpawnPipeline
    {
        private const int ImpactAoeIdSalt = 0x5F1A0E;

        public static AoeSpawnEvent BuildImpactAoeEvent(in CombatPendingSpawn pending)
        {
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

            return new AoeSpawnEvent
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
            };
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

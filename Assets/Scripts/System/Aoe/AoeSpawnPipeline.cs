using PlayGround.Skills.Runtime;
using PlayGround.System.Common;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

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
        public int Count;
        public uint JitterSeed;
        public int DeterministicIdTickIndex;
        public CombatRenderComponent Render;
        public AoeProjectileBurstSnapshot ProjectileBurst;
        public RuntimeStackingDetonation ProjectileBurstStacking;
        public AoeOnHitSpawnSnapshot AoeSpawn;
        public int HasTimedSpawner;
        public TimedSpawnComponent TimedSpawn;
    }

    // ECS Lifecycle: resolved single-entity allocation intent; produced by expansion, consumed by apply; never carries multiplicity.
    public struct AoeSpawnCommand
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
        public RuntimeStackingDetonation ProjectileBurstStacking;
        public AoeOnHitSpawnSnapshot AoeSpawn;
        public int HasTimedSpawner;
        public TimedSpawnComponent TimedSpawn;
    }

    internal static class AoeSpawnPipeline
    {
        private const int ImpactAoeIdSalt = 0x5F1A0E;

        public static AoeSpawnEvent BuildImpactAoeEvent(
            CombatFaction faction, int sourceId, int typeId, int targetId,
            float2 position, EntityId sourceNodeId,
            in ProjectileImpactAoeSnapshot snapshot)
        {
            AoeSpawnGeometry geo = snapshot.Geometry;
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
                Faction = faction,
                AoeId = HashId(sourceId, typeId, targetId, ImpactAoeIdSalt),
                TypeId = snapshot.TypeId,
                Lifetime = snapshot.LifetimeSeconds,
                RepeatHitCooldownSeconds = snapshot.TickIntervalSeconds,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = snapshot.DamageAmount,
                    CritChance = snapshot.CritChance,
                    CritMultiplier = snapshot.CritMultiplier,
                    DirectDamageEnabled = true,
                    SourceNodeId = sourceNodeId,
                    StackEffect = snapshot.StackEffect
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
                ProjectileBurst = default,
                AoeSpawn = snapshot.AoeSpawn
            };
        }

        public static AoeSpawnEvent BuildOnHitAoeSpawnEvent(
            CombatFaction faction,
            int sourceId,
            int sourceTypeId,
            int targetId,
            float2 position,
            in AoeOnHitSpawnSnapshot snapshot)
        {
            AoeSpawnGeometry geo = snapshot.Geometry;
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
                Faction = faction,
                AoeId = HashId(sourceId, sourceTypeId, targetId, ImpactAoeIdSalt ^ 0x13579B),
                TypeId = snapshot.TypeId,
                Lifetime = snapshot.LifetimeSeconds,
                RepeatHitCooldownSeconds = snapshot.TickIntervalSeconds,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = snapshot.DamageAmount,
                    CritChance = snapshot.CritChance,
                    CritMultiplier = snapshot.CritMultiplier,
                    DirectDamageEnabled = snapshot.DirectDamageEnabled,
                    SourceNodeId = default,
                    StackEffect = snapshot.BuildStackEffect(faction)
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
                ProjectileBurst = default,
                AoeSpawn = default
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

using PlayGround.System.Common;
using Unity.Entities;
using Unity.Mathematics;
using EntityId = UnityEngine.EntityId;

namespace PlayGround.System.Aoe
{
    // ECS Lifecycle: transient spawn intent; enqueued by producers into the expansion queue
    // or appended to the scope submission buffer; consumed and discarded by AoeSpawnExpansionSystem.
    public struct AoeSpawnEvent : IBufferElementData
    {
        public IntervalChildKind Kind;
        public Unity.Entities.Hash128 TemplateKey;
        public float2 Position;
        public float2 AimDirection;
        public CombatFaction Faction;
        public int SourceId;
        public uint JitterSeed;
        public int DeterministicIdTickIndex;
        public int ContactGateSeedTargetId;
    }

    // ECS Lifecycle: resolved single-entity allocation intent; produced by expansion, consumed by apply.
    // Registry templates also use this command shape with per-instance fields default and volley fields populated.
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
        public int Count;
        public uint JitterSeed;
        public int DeterministicIdTickIndex;
        public CombatRenderComponent Render;
        public AoeProjectileBurstSnapshot ProjectileBurst;
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
            int aoeId = HashId(sourceId, typeId, targetId, ImpactAoeIdSalt);
            return new AoeSpawnEvent
            {
                Kind = IntervalChildKind.Aoe,
                Faction = faction,
                Position = position,
                SourceId = aoeId,
                JitterSeed = (uint)aoeId * 2654435761u,
                ContactGateSeedTargetId = targetId
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
            int aoeId = HashId(sourceId, sourceTypeId, targetId, ImpactAoeIdSalt ^ 0x13579B);
            return new AoeSpawnEvent
            {
                Kind = IntervalChildKind.Aoe,
                Faction = faction,
                Position = position,
                SourceId = aoeId,
                JitterSeed = (uint)aoeId * 2654435761u,
                ContactGateSeedTargetId = targetId
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

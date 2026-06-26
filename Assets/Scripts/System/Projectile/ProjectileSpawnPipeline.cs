using PlayGround.System.Common;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    // ECS Lifecycle: transient spawn intent; enqueued by producers into the expansion queue
    // or appended to the scope submission buffer; consumed and discarded by ProjectileSpawnExpansionSystem.
    public struct ProjectileSpawnEvent : IBufferElementData
    {
        public IntervalChildKind Kind;
        public Hash128 TemplateKey;
        public float2 Position;
        public float2 AimDirection;
        public CombatFaction Faction;
        public int SourceId;
        public uint JitterSeed;
        public int DeterministicIdTickIndex;
        public int ContactGateSeedTargetId;
    }

    // Resolved single-entity allocation intent; produced by expansion, consumed by apply.
    // Registry templates also use this command shape with per-instance fields default and volley fields populated.
    public struct ProjectileSpawnCommand
    {
        public CombatFaction Faction;
        public int ProjectileId;
        public int TypeId;
        public int HasTimedSpawner;
        public float2 BaseDirection;
        public float Speed;
        public int Count;
        public float SpreadDegrees;
        public float JitterDegrees;
        public uint JitterSeed;
        public ProjectileChildSpawnPatternType SpawnPatternType;
        public int DeterministicIdTickIndex;
        public int PierceRemaining;
        public float RepeatHitCooldownSeconds;
        public int SeedContactGateTargetId;
        public float Lifetime;
        public float Radius;
        public float RotationRadians;
        public float2 Position;
        public float2 Velocity;
        public float2 HalfExtents;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public CombatShapeType ShapeType;
        public ProjectileHitPayload HitPayload;
        public ProjectileTrackingComponent Tracking;
        public CombatRenderComponent Render;
        public TimedSpawnComponent TimedSpawn;
    }

    internal static class ProjectileSpawnPipeline
    {
        private const int ImpactProjectileIdSalt = 0x2C1297;
        private const int ProjectileBurstIdSalt = 0x7AB025;

        public static ProjectileSpawnEvent BuildImpactProjectileEvent(
            CombatFaction faction, int sourceId, int typeId, int targetId,
            float2 position, float2 targetPosition,
            OnHitSpawnRef spawnRef)
        {
            int baseId = HashId(sourceId, typeId, targetId, ImpactProjectileIdSalt);
            return new ProjectileSpawnEvent
            {
                Kind = spawnRef.Kind,
                TemplateKey = spawnRef.TemplateKey,
                Faction = faction,
                Position = position,
                AimDirection = DirectionFromTo(position, targetPosition, invert: true),
                SourceId = baseId,
                JitterSeed = (uint)baseId * 2654435761u,
                ContactGateSeedTargetId = targetId
            };
        }

        public static ProjectileSpawnEvent BuildBurstEvent(
            CombatFaction faction, int sourceId, int typeId, int targetId,
            float2 position, float2 targetPosition,
            OnHitSpawnRef spawnRef)
        {
            int baseId = HashId(sourceId, typeId, targetId, ProjectileBurstIdSalt);
            return new ProjectileSpawnEvent
            {
                Kind = spawnRef.Kind,
                TemplateKey = spawnRef.TemplateKey,
                Faction = faction,
                Position = position,
                AimDirection = DirectionFromTo(position, targetPosition, invert: false),
                SourceId = baseId,
                JitterSeed = (uint)baseId * 2654435761u,
                ContactGateSeedTargetId = targetId
            };
        }

        private static float2 DirectionFromTo(float2 from, float2 to, bool invert)
        {
            float2 toTarget = to - from;
            if (math.lengthsq(toTarget) <= 0.0001f)
            {
                return new float2(1f, 0f);
            }

            float2 dir = math.normalize(toTarget);
            return invert ? -dir : dir;
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

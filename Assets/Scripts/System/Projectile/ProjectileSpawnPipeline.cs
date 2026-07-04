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
        // Render identity, allocated from the unified render-resource id space (decoupled
        // from the behavior TypeId). Drives the CombatRenderKindId / registry lookup.
        public int RenderTypeId;
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

}

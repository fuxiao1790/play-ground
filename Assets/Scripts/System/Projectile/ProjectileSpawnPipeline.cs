using PlayGround.System.Common;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    // ECS Lifecycle: transient spawn intent; enqueued by producers into the expansion queue or appended to the scope submission buffer; consumed and discarded by ProjectileSpawnExpansionSystem.
    public struct ProjectileSpawnEvent : IBufferElementData
    {
        public CombatFaction Faction;
        public int TypeId;
        public int BaseProjectileId;
        public int HasChildSpawner;
        public int SeedContactGateTargetId;
        public float2 Position;
        public float2 BaseDirection;
        public float Speed;
        // multiplicity — consumed and DISCARDED by expansion, never reaches a command:
        public int Count;
        public float SpreadDegrees;
        public float JitterDegrees;
        public uint JitterSeed;
        // resolved per-entity template (everything apply needs except per-shot velocity/id/render-Z):
        public int PierceRemaining;
        public float RepeatHitCooldownSeconds;
        public float Lifetime;
        public float Radius;
        public float RotationRadians;
        public float2 HalfExtents;
        public CombatShapeType ShapeType;
        public ProjectileHitPayload HitPayload;
        public ProjectileTrackingComponent Tracking;
        public CombatRenderComponent Render;
        public ProjectileChildSpawnerComponent ChildSpawner;
        public AoeIntervalSpawnerComponent AoeSpawner;
        public ProjectileChildSpawnStateComponent ChildSpawnState;
    }

    // resolved single-entity allocation intent; produced by expansion, consumed by apply; never carries multiplicity.
    public struct ProjectileSpawnCommand
    {
        public CombatFaction Faction;
        public int ProjectileId;
        public int TypeId;
        public int HasChildSpawner;
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
        public ProjectileChildSpawnerComponent ChildSpawner;
        public AoeIntervalSpawnerComponent AoeSpawner;
        public ProjectileChildSpawnStateComponent ChildSpawnState;
    }

    internal static class ProjectileSpawnPipeline
    {
        private const int ImpactProjectileIdSalt = 0x2C1297;
        private const int ProjectileBurstIdSalt = 0x7AB025;

        public static ProjectileSpawnEvent BuildImpactProjectileEvent(
            CombatFaction faction, int sourceId, int typeId, int targetId,
            float2 position, float2 targetPosition,
            in ProjectileImpactProjectileSnapshot snapshot)
        {
            int baseId = HashId(sourceId, typeId, targetId, ImpactProjectileIdSalt);
            float2 baseDirection = DirectionFromTo(position, targetPosition, invert: true);
            var hitPayload = new ProjectileHitPayload(
                new CombatHitPayload
                {
                    DamageAmount = snapshot.Damage.Amount,
                    CritChance = 0f,
                    CritMultiplier = 1.5f,
                    DirectDamageEnabled = snapshot.DirectDamageEnabled,
                    SourceNodeId = default,
                    StackEffect = snapshot.StackEffect
                },
                snapshot.ImpactAoe,
                default);
            return new ProjectileSpawnEvent
            {
                Faction = faction,
                TypeId = snapshot.ProjectileTypeId,
                BaseProjectileId = baseId,
                HasChildSpawner = 0,
                SeedContactGateTargetId = targetId,
                Position = position,
                BaseDirection = baseDirection,
                Speed = snapshot.Speed,
                Count = snapshot.Count,
                SpreadDegrees = snapshot.SpreadDegrees,
                JitterDegrees = 0f,
                JitterSeed = (uint)baseId * 2654435761u,
                PierceRemaining = snapshot.PierceCount,
                RepeatHitCooldownSeconds = snapshot.RepeatHitCooldownSeconds,
                Lifetime = snapshot.LifetimeSeconds,
                Radius = snapshot.Radius,
                RotationRadians = snapshot.RotationRadians,
                HalfExtents = new float2(snapshot.HalfExtents.x, snapshot.HalfExtents.y),
                ShapeType = snapshot.ShapeType,
                HitPayload = hitPayload,
                Tracking = TrackingFor(snapshot.Tracking),
                Render = ProjectileRender(snapshot.VisualScale, snapshot.VisualRotationDegrees, baseId)
            };
        }

        public static ProjectileSpawnEvent BuildBurstEvent(
            CombatFaction faction, int sourceId, int typeId, int targetId,
            float2 position, float2 targetPosition,
            in AoeProjectileBurstSnapshot snapshot)
        {
            int baseId = HashId(sourceId, typeId, targetId, ProjectileBurstIdSalt);
            float2 baseDirection = DirectionFromTo(position, targetPosition, invert: false);
            var hitPayload = new ProjectileHitPayload(
                new CombatHitPayload
                {
                    DamageAmount = snapshot.Damage.Amount,
                    CritChance = 0f,
                    CritMultiplier = 1.5f,
                    DirectDamageEnabled = snapshot.DirectDamageEnabled,
                    SourceNodeId = default,
                    StackEffect = default
                },
                default,
                default);
            return new ProjectileSpawnEvent
            {
                Faction = faction,
                TypeId = snapshot.ProjectileTypeId,
                BaseProjectileId = baseId,
                HasChildSpawner = 0,
                SeedContactGateTargetId = 0,
                Position = position,
                BaseDirection = baseDirection,
                Speed = snapshot.Speed,
                Count = snapshot.Count,
                SpreadDegrees = snapshot.SpreadDegrees,
                JitterDegrees = 0f,
                JitterSeed = (uint)baseId * 2654435761u,
                PierceRemaining = snapshot.PierceCount,
                RepeatHitCooldownSeconds = snapshot.RepeatHitCooldownSeconds,
                Lifetime = snapshot.LifetimeSeconds,
                Radius = snapshot.Radius,
                RotationRadians = snapshot.RotationRadians,
                HalfExtents = new float2(snapshot.HalfExtents.x, snapshot.HalfExtents.y),
                ShapeType = snapshot.ShapeType,
                HitPayload = hitPayload,
                Tracking = default,
                Render = ProjectileRender(snapshot.VisualScale, snapshot.VisualRotationDegrees, baseId)
            };
        }

        private static ProjectileTrackingComponent TrackingFor(ProjectileTrackingConfig config)
        {
            return new ProjectileTrackingComponent
            {
                TrackingEnabled = config.Enabled,
                TrackingTurnSpeedRadians = math.radians(config.TurnSpeedDegrees),
                TrackingQueryCooldownRemaining = config.InitialQueryDelaySeconds,
                TrackingQueryIntervalSeconds = config.QueryIntervalSeconds,
                TrackedTargetId = 0,
                TrackedTargetIndex = -1,
                TrackedTargetPosition = default,
                TrackingRandomState = 0
            };
        }

        private static CombatRenderComponent ProjectileRender(float visualScale, float visualRotationDegrees, int projectileId)
        {
            if (visualScale <= 0f)
            {
                return default;
            }
            math.sincos(math.radians(visualRotationDegrees), out float sin, out float cos);
            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 1,
                VisualScale = new float2(visualScale, visualScale),
                VisualRotationSin = sin,
                VisualRotationCos = cos,
                RenderZ = CombatRoot.ProjectileRenderZ
                    - (projectileId % CombatRoot.ProjectileRenderZSlots) * CombatRoot.ProjectileRenderZStep
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

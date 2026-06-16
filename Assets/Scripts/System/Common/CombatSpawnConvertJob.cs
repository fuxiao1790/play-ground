using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Common
{
    // Replaces the spawn half of CombatHitFlushJob AND the managed CombatSpawnRouter
    // + root SpawnRequestFor logic. Reads the internal hit-spawn stream and appends
    // projectile/AOE spawn requests directly onto the producing scope's buffers,
    // fully in ECS so follow-up spawns materialize the same frame.
    //
    // There is one shared scope for the whole world; internal spawns always
    // target that same Scope and stamp the producing pending.Faction onto the
    // written element — no routing component needed.
    //
    // Single IJob (not parallel) so the BufferLookup writes are serial and safe,
    // matching the old CombatHitFlushJob contract.
    [BurstCompile]
    public struct CombatSpawnConvertJob : IJob
    {
        // Distinct salts keep the three deterministic id spaces from overlapping.
        private const int ImpactAoeIdSalt = 0x5F1A0E;
        private const int ImpactProjectileIdSalt = 0x2C1297;
        private const int ProjectileBurstIdSalt = 0x7AB025;

        public Entity Scope;
        public NativeStream PendingSpawns;
        public BufferLookup<ProjectileSpawnRequestElement> ProjectileRequests;
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

            if (pending.ImpactProjectile.Enabled)
            {
                AppendImpactProjectiles(in pending);
            }

            if (pending.ProjectileBurst.Enabled)
            {
                AppendProjectileBurst(in pending);
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

        private void AppendImpactProjectiles(in CombatPendingSpawn pending)
        {
            Entity dest = Scope;
            if (!ProjectileRequests.HasBuffer(dest))
            {
                return;
            }

            ProjectileImpactProjectileSnapshot burst = pending.ImpactProjectile;
            int baseId = HashId(pending.SourceId, pending.TypeId, pending.TargetId, ImpactProjectileIdSalt);
            // Impact projectiles fire back toward the source (mirror of CombatSpawnRouter).
            float2 baseDirection = DirectionFromTo(pending.Position, pending.TargetPosition, invert: true);

            var hitPayload = new ProjectileHitPayload(
                new CombatHitPayload
                {
                    DamageAmount = burst.Damage.Amount,
                    CritChance = 0f,
                    CritMultiplier = 1.5f,
                    DirectDamageEnabled = burst.DirectDamageEnabled,
                    SourceNodeId = default,
                    StackEffect = burst.StackEffect
                },
                burst.ImpactAoe,
                default);

            ProjectileSpawnRequestElement request = BuildProjectileRequest(
                pending.Faction, baseId, burst.ProjectileTypeId, pending.Position, baseDirection,
                burst.Speed, burst.LifetimeSeconds, burst.Radius,
                new float2(burst.HalfExtents.x, burst.HalfExtents.y), burst.RotationRadians, burst.ShapeType,
                burst.PierceCount, burst.RepeatHitCooldownSeconds, burst.Count, burst.SpreadDegrees,
                hitPayload, TrackingFor(burst.Tracking), burst.VisualScale, burst.VisualRotationDegrees,
                seedContactGateTargetId: pending.TargetId);

            ProjectileRequests[dest].Add(request);
        }

        private void AppendProjectileBurst(in CombatPendingSpawn pending)
        {
            Entity dest = Scope;
            if (!ProjectileRequests.HasBuffer(dest))
            {
                return;
            }

            AoeProjectileBurstSnapshot burst = pending.ProjectileBurst;
            int baseId = HashId(pending.SourceId, pending.TypeId, pending.TargetId, ProjectileBurstIdSalt);
            // AOE bursts fire toward the target (mirror of CombatSpawnRouter).
            float2 baseDirection = DirectionFromTo(pending.Position, pending.TargetPosition, invert: false);

            var hitPayload = new ProjectileHitPayload(
                new CombatHitPayload
                {
                    DamageAmount = burst.Damage.Amount,
                    CritChance = 0f,
                    CritMultiplier = 1.5f,
                    DirectDamageEnabled = burst.DirectDamageEnabled,
                    SourceNodeId = default,
                    StackEffect = default
                },
                default,
                default);

            ProjectileSpawnRequestElement request = BuildProjectileRequest(
                pending.Faction, baseId, burst.ProjectileTypeId, pending.Position, baseDirection,
                burst.Speed, burst.LifetimeSeconds, burst.Radius,
                new float2(burst.HalfExtents.x, burst.HalfExtents.y), burst.RotationRadians, burst.ShapeType,
                burst.PierceCount, burst.RepeatHitCooldownSeconds, burst.Count, burst.SpreadDegrees,
                hitPayload, default, burst.VisualScale, burst.VisualRotationDegrees,
                seedContactGateTargetId: 0);

            ProjectileRequests[dest].Add(request);
        }

        private static ProjectileSpawnRequestElement BuildProjectileRequest(
            CombatFaction faction, int baseId, int typeId, float2 position, float2 baseDirection,
            float speed, float lifetime, float radius, float2 halfExtents, float rotationRadians,
            CombatShapeType shapeType, int pierce, float repeatHitCooldownSeconds, int count, float spreadDegrees,
            ProjectileHitPayload hitPayload, ProjectileTrackingComponent tracking,
            float visualScale, float visualRotationDegrees, int seedContactGateTargetId)
        {
            CombatCollisionMath.ComputeWorldBounds(
                position, radius, halfExtents, rotationRadians, shapeType,
                out float2 boundsMin, out float2 boundsMax);

            var request = new ProjectileSpawnRequestElement
            {
                Faction = faction,
                ProjectileId = baseId,
                TypeId = typeId,
                PierceRemaining = pierce,
                HasChildSpawner = 0,
                SeedContactGateTargetId = seedContactGateTargetId,
                RepeatHitCooldownSeconds = repeatHitCooldownSeconds,
                Lifetime = lifetime,
                Radius = radius,
                RotationRadians = rotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                ShapeType = shapeType,
                HitPayload = hitPayload,
                Tracking = tracking,
                Render = ProjectileRender(visualScale, visualRotationDegrees, baseId),
                Count = count
            };

            if (count <= 1)
            {
                request.Velocity = baseDirection * speed;
            }
            else
            {
                request.BaseDirection = baseDirection;
                request.Speed = speed;
                request.SpreadDegrees = spreadDegrees;
                request.JitterDegrees = 0f;
                request.JitterSeed = (uint)baseId * 2654435761u;
            }

            return request;
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

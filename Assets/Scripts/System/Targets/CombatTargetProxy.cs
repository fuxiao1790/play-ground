using System.Collections.Generic;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace PlayGround.System.Combat.Targets
{
    public struct TargetProxyTag : IComponentData
    {
    }

    public struct TargetPosition : IComponentData
    {
        public float2 Value;
    }

    public enum TargetFactionFilterMode : byte
    {
        HostileOnly = 0,
        AllowedFactionOnly = 1
    }

    // ECS Lifecycle: present on every target proxy, stamped once at creation by
    // TargetProxyCreateApplySystem, immutable for the proxy's lifetime in this scope.
    public struct TargetFaction : IComponentData
    {
        public CombatFaction Value;
        public TargetFactionFilterMode FilterMode;
        public CombatFaction AllowedAttackerFaction;

        public static TargetFaction Hostile(CombatFaction ownFaction) => new()
        {
            Value = ownFaction,
            FilterMode = TargetFactionFilterMode.HostileOnly,
            AllowedAttackerFaction = CombatFaction.None
        };

        public static TargetFaction AllowedFrom(CombatFaction ownFaction, CombatFaction allowedAttacker) => new()
        {
            Value = ownFaction,
            FilterMode = TargetFactionFilterMode.AllowedFactionOnly,
            AllowedAttackerFaction = allowedAttacker
        };

        // Single eligibility predicate shared by every selection/collision path (acquisition,
        // projectile discrete/continuous collision, AOE collision, projectile tracking). Pure
        // value comparison so it stays Burst-safe and callable from job code.
        public static bool CanHit(CombatFaction attacker, in TargetFaction target)
        {
            if (attacker == CombatFaction.None)
            {
                return false;
            }

            switch (target.FilterMode)
            {
                case TargetFactionFilterMode.HostileOnly:
                    return attacker != target.Value;
                case TargetFactionFilterMode.AllowedFactionOnly:
                    return attacker == target.AllowedAttackerFaction;
                default:
                    return false;
            }
        }
    }

    // ECS Lifecycle: target-proxy stack buffer; added empty when the proxy is created, destroyed with the proxy. CombatApplyFinalizeSingleSystem accrues entries, then StatusProcessSystem fizzles or detonates them.
    [InternalBufferCapacity(8)]
    public struct TargetStackEntry : IBufferElementData
    {
        public int DebuffKey;
        public int Threshold;
        public int Count;
        public float SummedDamage;
        public int SummedProjectileCount;
        public float SummedArea;
        // Absolute expiry deadline in world ElapsedTime seconds. Written only on accrual
        // (CombatApplyFinalizeSingleSystem); StatusProcessSystem only reads it to test
        // expiry. Storing a deadline instead of a decrementing remaining-time means the
        // two systems no longer both write this field, so they need no shared frame token
        // and their execution order no longer matters for lifetime.
        public double ExpiryTime;
        public DetonationSnapshot Detonation;
    }

    public sealed class TargetCompanion : IComponentData
    {
        public ICombatTarget Target;
    }

    public static class CombatTargetProxy
    {
        private static readonly ProfilerMarker PushResolveMarker = new("CombatTargetProxy.Push.Resolve");
        private static readonly ProfilerMarker PushApplyMarker = new("CombatTargetProxy.Push.Apply");
        private static readonly ProfilerMarker TryGetEntityManagerMarker = new("CombatTargetProxy.TryGetEntityManager");
        private static readonly ProfilerMarker BuildPositionMarker = new("CombatTargetProxy.BuildPosition");
        private static readonly ProfilerMarker BuildShapeMarker = new("CombatTargetProxy.BuildShape");
        private static int nextCreateToken;
        private static readonly Dictionary<int, ICombatTarget> pendingCreates = new();
        private static readonly Dictionary<ICombatTarget, int> pendingTokenByTarget = new();
        private static World cachedWorld;
        private static EntityArchetype cachedArchetype;

        public static bool Create(EntityManager entityManager, ICombatTarget target, CombatFaction faction) =>
            Create(entityManager, target, TargetFaction.Hostile(faction));

        public static bool Create(EntityManager entityManager, ICombatTarget target, TargetFaction policy)
        {
            if (target == null || entityManager == default)
            {
                return false;
            }

            Entity existing = target.CombatTargetProxy;
            if (existing != Entity.Null)
            {
                return Push(entityManager, existing, target);
            }

            if (pendingTokenByTarget.ContainsKey(target) || !TryGetScopeEntity(entityManager, out Entity scopeEntity))
            {
                return false;
            }

            int token = ++nextCreateToken;
            pendingCreates[token] = target;
            pendingTokenByTarget[target] = token;

            TargetPosition position = BuildPosition(target);
            TargetCollisionShape shape = BuildShape(target, position.Value);
            float maxHealth = math.max(1f, target.CombatMaxHealth);
            float currentHealth = math.clamp(target.CombatCurrentHealth, 0f, maxHealth);
            float maxMana = math.max(0f, target.CombatMaxMana);
            float currentMana = math.clamp(target.CombatCurrentMana, 0f, maxMana);
            entityManager.GetBuffer<TargetProxyCreateEvent>(scopeEntity).Add(new TargetProxyCreateEvent
            {
                Token = token,
                FactionPolicy = policy,
                Position = position,
                Shape = shape,
                MaxHealth = maxHealth,
                CurrentHealth = currentHealth,
                HealthRegenPerSecond = math.max(0f, target.CombatHealthRegenPerSecond),
                MaxMana = maxMana,
                CurrentMana = currentMana,
                ManaRegenPerSecond = math.max(0f, target.CombatManaRegenPerSecond)
            });
            return true;
        }

        public static void Delete(EntityManager entityManager, Entity entity)
        {
            if (entity == Entity.Null || !TryGetScopeEntity(entityManager, out Entity scopeEntity))
            {
                return;
            }

            entityManager.GetBuffer<TargetProxyDeleteEvent>(scopeEntity).Add(new TargetProxyDeleteEvent
            {
                Proxy = entity
            });
        }

        public static void Delete(ICombatTarget target)
        {
            if (target == null)
            {
                return;
            }

            Entity entity = target.CombatTargetProxy;
            if (pendingTokenByTarget.TryGetValue(target, out int token))
            {
                pendingTokenByTarget.Remove(target);
                pendingCreates.Remove(token);
            }

            if (entity != Entity.Null && TryGetEntityManager(out EntityManager entityManager))
            {
                Delete(entityManager, entity);
            }

            target.CombatTargetProxy = Entity.Null;
        }

        public static bool Push(ICombatTarget target)
        {
            using (PushResolveMarker.Auto())
            {
                if (target == null || target.CombatTargetProxy == Entity.Null || !TryGetEntityManager(out EntityManager entityManager))
                {
                    return false;
                }

                return Push(entityManager, target.CombatTargetProxy, target);
            }
        }

        public static bool Push(EntityManager entityManager, Entity entity, ICombatTarget target)
        {
            using (PushApplyMarker.Auto())
            {
                if (target == null
                    || !target.IsCombatTargetActive
                    || entity == Entity.Null
                    || !TryGetScopeEntity(entityManager, out Entity scopeEntity))
                {
                    return false;
                }

                TargetPosition position;
                using (BuildPositionMarker.Auto())
                {
                    position = BuildPosition(target);
                }

                TargetCollisionShape shape;
                using (BuildShapeMarker.Auto())
                {
                    shape = BuildShape(target, position.Value);
                }

                entityManager.GetBuffer<TargetProxyUpdateEvent>(scopeEntity).Add(new TargetProxyUpdateEvent
                {
                    Kind = TargetProxyUpdateKind.Push,
                    Proxy = entity,
                    Position = position,
                    Shape = shape
                });

                return true;
            }
        }

        public static bool SetHealth(ICombatTarget target, float currentHealth)
        {
            if (target == null
                || target.CombatTargetProxy == Entity.Null
                || !TryGetEntityManager(out EntityManager entityManager)
                || !TryGetScopeEntity(entityManager, out Entity scopeEntity))
            {
                return false;
            }

            DynamicBuffer<TargetProxyUpdateEvent> updates = entityManager.GetBuffer<TargetProxyUpdateEvent>(scopeEntity);
            updates.Add(ResourceMaxEvent(target.CombatTargetProxy, target));
            updates.Add(new TargetProxyUpdateEvent
            {
                Kind = TargetProxyUpdateKind.SetHealth,
                Proxy = target.CombatTargetProxy,
                CurrentValue = currentHealth
            });
            return true;
        }

        public static bool SetMana(ICombatTarget target, float currentMana)
        {
            if (target == null
                || target.CombatTargetProxy == Entity.Null
                || !TryGetEntityManager(out EntityManager entityManager)
                || !TryGetScopeEntity(entityManager, out Entity scopeEntity))
            {
                return false;
            }

            DynamicBuffer<TargetProxyUpdateEvent> updates = entityManager.GetBuffer<TargetProxyUpdateEvent>(scopeEntity);
            updates.Add(ResourceMaxEvent(target.CombatTargetProxy, target));
            updates.Add(new TargetProxyUpdateEvent
            {
                Kind = TargetProxyUpdateKind.SetMana,
                Proxy = target.CombatTargetProxy,
                CurrentValue = currentMana
            });
            return true;
        }

        public static bool PushResourceMaxes(ICombatTarget target)
        {
            if (target == null
                || target.CombatTargetProxy == Entity.Null
                || !TryGetEntityManager(out EntityManager entityManager))
            {
                return false;
            }

            return PushResourceMaxes(entityManager, target.CombatTargetProxy, target);
        }

        public static bool PushResourceMaxes(EntityManager entityManager, Entity entity, ICombatTarget target)
        {
            if (target == null
                || entity == Entity.Null
                || !TryGetScopeEntity(entityManager, out Entity scopeEntity))
            {
                return false;
            }

            entityManager.GetBuffer<TargetProxyUpdateEvent>(scopeEntity).Add(ResourceMaxEvent(entity, target));
            return true;
        }

        internal static bool TryTakePendingCreate(int token, out ICombatTarget target)
        {
            if (!pendingCreates.TryGetValue(token, out target))
            {
                return false;
            }

            pendingCreates.Remove(token);
            if (pendingTokenByTarget.TryGetValue(target, out int pendingToken) && pendingToken == token)
            {
                pendingTokenByTarget.Remove(target);
            }

            return true;
        }

        internal static bool IsPendingCreateCancelled(int token) => !pendingCreates.ContainsKey(token);

        public static bool TryReadResources(ICombatTarget target, out Health health, out Mana mana)
        {
            health = default;
            mana = default;
            if (target == null
                || target.CombatTargetProxy == Entity.Null
                || !TryGetEntityManager(out EntityManager entityManager))
            {
                return false;
            }

            health = entityManager.GetComponentData<Health>(target.CombatTargetProxy);
            mana = entityManager.GetComponentData<Mana>(target.CombatTargetProxy);
            return true;
        }

        public static int TargetKey(Entity entity)
        {
            unchecked
            {
                int key = ((entity.Index + 1) * 397) ^ entity.Version;
                key &= 0x7fffffff;
                return key == 0 ? 1 : key;
            }
        }

        private static TargetPosition BuildPosition(ICombatTarget target) =>
            new()
            {
                Value = new float2(target.CombatTargetPosition.x, target.CombatTargetPosition.y)
            };

        private static TargetCollisionShape BuildShape(ICombatTarget target, float2 position)
        {
            float radius = target.CombatTargetRadius;
            Vector2 targetHalfExtents = target.CombatTargetHalfExtents;
            float2 halfExtents = new(targetHalfExtents.x, targetHalfExtents.y);
            float rotationRadians = target.CombatTargetRotationRadians;
            CombatShapeType shapeType = target.CombatTargetShapeType;
            int mask = target.CombatTargetMask;
            CombatCollisionMath.ComputeWorldBounds(
                position,
                radius,
                halfExtents,
                rotationRadians,
                shapeType,
                out float2 boundsMin,
                out float2 boundsMax);

            return new TargetCollisionShape
            {
                ShapeType = shapeType,
                Radius = radius,
                HalfExtents = halfExtents,
                RotationRadians = rotationRadians,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                Mask = mask
            };
        }

        private static TargetProxyUpdateEvent ResourceMaxEvent(Entity entity, ICombatTarget target)
        {
            return new TargetProxyUpdateEvent
            {
                Kind = TargetProxyUpdateKind.PushResourceMaxes,
                Proxy = entity,
                MaxHealth = math.max(1f, target.CombatMaxHealth),
                HealthRegenPerSecond = math.max(0f, target.CombatHealthRegenPerSecond),
                MaxMana = math.max(0f, target.CombatMaxMana),
                ManaRegenPerSecond = math.max(0f, target.CombatManaRegenPerSecond)
            };
        }

        internal static EntityArchetype Archetype(EntityManager entityManager)
        {
            World world = entityManager.World;
            if (cachedWorld == world && cachedArchetype.Valid)
            {
                return cachedArchetype;
            }

            cachedWorld = world;
            cachedArchetype = entityManager.CreateArchetype(
                typeof(TargetProxyTag),
                typeof(TargetPosition),
                typeof(TargetCollisionShape),
                typeof(TargetFaction),
                typeof(Health),
                typeof(Mana),
                typeof(TargetStackEntry));
            return cachedArchetype;
        }

        private static bool TryGetScopeEntity(EntityManager entityManager, out Entity scopeEntity)
        {
            if (entityManager == default)
            {
                scopeEntity = Entity.Null;
                return false;
            }

            if (CombatScopeOwner.TryGetScope(entityManager, out scopeEntity))
            {
                return true;
            }

            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadOnly<TargetProxyCreateEvent>(),
                ComponentType.ReadOnly<TargetProxyUpdateEvent>(),
                ComponentType.ReadOnly<TargetProxyDeleteEvent>());
            if (query.CalculateEntityCount() != 1)
            {
                return false;
            }

            scopeEntity = query.GetSingletonEntity();
            return true;
        }

        private static bool TryGetEntityManager(out EntityManager entityManager)
        {
            using (TryGetEntityManagerMarker.Auto())
            {
                World world = World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated)
                {
                    entityManager = default;
                    return false;
                }

                entityManager = world.EntityManager;
                return true;
            }
        }
    }
}

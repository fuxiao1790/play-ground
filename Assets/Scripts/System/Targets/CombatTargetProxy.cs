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

    public struct TargetFaction : IComponentData
    {
        public CombatFaction Value;
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
        private static readonly ProfilerMarker ExistsMarker = new("CombatTargetProxy.Exists");
        private static readonly ProfilerMarker BuildPositionMarker = new("CombatTargetProxy.BuildPosition");
        private static readonly ProfilerMarker BuildShapeMarker = new("CombatTargetProxy.BuildShape");
        private static readonly ProfilerMarker SetComponentDataMarker = new("CombatTargetProxy.SetComponentData");
        private static World cachedWorld;
        private static EntityArchetype cachedArchetype;

        public static Entity Create(EntityManager entityManager, ICombatTarget target, CombatFaction faction)
        {
            if (target == null || entityManager == default)
            {
                return Entity.Null;
            }

            Entity existing = target.CombatTargetProxy;
            if (Exists(entityManager, existing))
            {
                Push(entityManager, existing, target);
                return existing;
            }

            Entity entity = entityManager.CreateEntity(Archetype(entityManager));
            target.CombatTargetProxy = entity;
            entityManager.SetComponentData(entity, new TargetFaction { Value = faction });
            entityManager.SetComponentData(entity, new TargetCompanion { Target = target });
            float maxHealth = math.max(1f, target.CombatMaxHealth);
            float currentHealth = math.clamp(target.CombatCurrentHealth, 0f, maxHealth);
            entityManager.SetComponentData(entity, new Health
            {
                Current = currentHealth,
                Max = maxHealth,
                RegenPerSecond = math.max(0f, target.CombatHealthRegenPerSecond)
            });
            float maxMana = math.max(0f, target.CombatMaxMana);
            float currentMana = math.clamp(target.CombatCurrentMana, 0f, maxMana);
            entityManager.SetComponentData(entity, new Mana
            {
                Current = currentMana,
                Max = maxMana,
                RegenPerSecond = math.max(0f, target.CombatManaRegenPerSecond)
            });
            Push(entityManager, entity, target);
            return entity;
        }

        public static void Delete(EntityManager entityManager, Entity entity)
        {
            if (!Exists(entityManager, entity))
            {
                return;
            }

            entityManager.DestroyEntity(entity);
        }

        public static void Delete(ICombatTarget target)
        {
            if (target == null || target.CombatTargetProxy == Entity.Null)
            {
                return;
            }

            if (TryGetEntityManager(out EntityManager entityManager))
            {
                Delete(entityManager, target.CombatTargetProxy);
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
                if (target == null || !target.IsCombatTargetActive || !Exists(entityManager, entity))
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

                using (SetComponentDataMarker.Auto())
                {
                    entityManager.SetComponentData(entity, position);
                    entityManager.SetComponentData(entity, shape);
                }

                return true;
            }
        }

        public static bool SetHealth(ICombatTarget target, float currentHealth)
        {
            if (target == null
                || target.CombatTargetProxy == Entity.Null
                || !TryGetEntityManager(out EntityManager entityManager)
                || !Exists(entityManager, target.CombatTargetProxy))
            {
                return false;
            }

            PushResourceMaxes(entityManager, target.CombatTargetProxy, target);
            Health health = entityManager.GetComponentData<Health>(target.CombatTargetProxy);
            health.Current = math.clamp(currentHealth, 0f, health.Max);
            entityManager.SetComponentData(target.CombatTargetProxy, health);
            return true;
        }

        public static bool SetMana(ICombatTarget target, float currentMana)
        {
            if (target == null
                || target.CombatTargetProxy == Entity.Null
                || !TryGetEntityManager(out EntityManager entityManager)
                || !Exists(entityManager, target.CombatTargetProxy))
            {
                return false;
            }

            PushResourceMaxes(entityManager, target.CombatTargetProxy, target);
            Mana mana = entityManager.GetComponentData<Mana>(target.CombatTargetProxy);
            mana.Current = math.clamp(currentMana, 0f, mana.Max);
            entityManager.SetComponentData(target.CombatTargetProxy, mana);
            return true;
        }

        public static bool PushResourceMaxes(ICombatTarget target)
        {
            if (target == null
                || target.CombatTargetProxy == Entity.Null
                || !TryGetEntityManager(out EntityManager entityManager)
                || !Exists(entityManager, target.CombatTargetProxy))
            {
                return false;
            }

            return PushResourceMaxes(entityManager, target.CombatTargetProxy, target);
        }

        public static bool PushResourceMaxes(EntityManager entityManager, Entity entity, ICombatTarget target)
        {
            if (target == null
                || !Exists(entityManager, entity)
                || !entityManager.HasComponent<Health>(entity)
                || !entityManager.HasComponent<Mana>(entity))
            {
                return false;
            }

            Health health = entityManager.GetComponentData<Health>(entity);
            health.Max = math.max(1f, target.CombatMaxHealth);
            health.RegenPerSecond = math.max(0f, target.CombatHealthRegenPerSecond);
            health.Current = math.min(health.Current, health.Max);
            entityManager.SetComponentData(entity, health);

            Mana mana = entityManager.GetComponentData<Mana>(entity);
            mana.Max = math.max(0f, target.CombatMaxMana);
            mana.RegenPerSecond = math.max(0f, target.CombatManaRegenPerSecond);
            mana.Current = math.min(mana.Current, mana.Max);
            entityManager.SetComponentData(entity, mana);
            return true;
        }

        public static bool TryReadResources(ICombatTarget target, out Health health, out Mana mana)
        {
            health = default;
            mana = default;
            if (target == null
                || target.CombatTargetProxy == Entity.Null
                || !TryGetEntityManager(out EntityManager entityManager)
                || !Exists(entityManager, target.CombatTargetProxy)
                || !entityManager.HasComponent<Health>(target.CombatTargetProxy)
                || !entityManager.HasComponent<Mana>(target.CombatTargetProxy))
            {
                return false;
            }

            health = entityManager.GetComponentData<Health>(target.CombatTargetProxy);
            mana = entityManager.GetComponentData<Mana>(target.CombatTargetProxy);
            return true;
        }

        public static bool Exists(EntityManager entityManager, Entity entity)
        {
            using (ExistsMarker.Auto())
            {
                if (entity == Entity.Null || entityManager == default)
                {
                    return false;
                }

                try
                {
                    return entityManager.Exists(entity);
                }
                catch (global::System.InvalidOperationException)
                {
                    return false;
                }
                catch (global::System.NullReferenceException)
                {
                    return false;
                }
            }
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

        private static EntityArchetype Archetype(EntityManager entityManager)
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
                typeof(TargetStackEntry),
                typeof(TargetCompanion));
            return cachedArchetype;
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

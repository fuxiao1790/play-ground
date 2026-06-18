using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Common
{
    public struct TargetProxyTag : IComponentData
    {
    }

    public struct TargetPosition : IComponentData
    {
        public float2 Value;
    }

    public struct TargetCollisionShape : IComponentData
    {
        public CombatShapeType ShapeType;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public int Mask;
    }

    public struct TargetFaction : IComponentData
    {
        public CombatFaction Value;
    }

    public sealed class TargetCompanion : IComponentData
    {
        public ICombatTarget Target;
    }

    public static class CombatTargetProxy
    {
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
            if (target == null || target.CombatTargetProxy == Entity.Null || !TryGetEntityManager(out EntityManager entityManager))
            {
                return false;
            }

            return Push(entityManager, target.CombatTargetProxy, target);
        }

        public static bool Push(EntityManager entityManager, Entity entity, ICombatTarget target)
        {
            if (target == null || !target.IsCombatTargetActive || !Exists(entityManager, entity))
            {
                return false;
            }

            TargetPosition position = BuildPosition(target);
            TargetCollisionShape shape = BuildShape(target, position.Value);
            entityManager.SetComponentData(entity, position);
            entityManager.SetComponentData(entity, shape);
            return true;
        }

        public static bool Exists(EntityManager entityManager, Entity entity)
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
            float2 halfExtents = new(target.CombatTargetHalfExtents.x, target.CombatTargetHalfExtents.y);
            CombatCollisionMath.ComputeWorldBounds(
                position,
                target.CombatTargetRadius,
                halfExtents,
                target.CombatTargetRotationRadians,
                target.CombatTargetShapeType,
                out float2 boundsMin,
                out float2 boundsMax);

            return new TargetCollisionShape
            {
                ShapeType = target.CombatTargetShapeType,
                Radius = target.CombatTargetRadius,
                HalfExtents = halfExtents,
                RotationRadians = target.CombatTargetRotationRadians,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                Mask = target.CombatTargetMask
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
                typeof(TargetCompanion));
            return cachedArchetype;
        }

        private static bool TryGetEntityManager(out EntityManager entityManager)
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

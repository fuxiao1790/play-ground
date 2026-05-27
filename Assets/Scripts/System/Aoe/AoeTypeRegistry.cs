using System.Collections.Generic;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public sealed class AoeTypeRegistry
    {
        private readonly AoeWorld world;
        private readonly Dictionary<int, AoeTypeDefinition> definitionsById = new();

        public AoeTypeRegistry(AoeWorld world)
        {
            this.world = world;
        }

        public void Register(AoeTypeDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            AoeShape shape = BakeShape(definition);
            definitionsById[definition.TypeId] = definition;
            world.RegisterAoeType(definition.TypeId, shape);
        }

        public bool TryGetDefinition(int typeId, out AoeTypeDefinition definition)
        {
            return definitionsById.TryGetValue(typeId, out definition);
        }

        private static AoeShape BakeShape(AoeTypeDefinition definition)
        {
            Collider2D collider = definition.CollisionShape;
            if (collider == null && definition.VisualPrefab != null)
            {
                collider = definition.VisualPrefab.GetComponentInChildren<Collider2D>(true);
            }

            if (collider == null)
            {
                return new AoeShape(ProjectileShapeType.Circle, definition.FallbackRadius, Vector2.one * definition.FallbackRadius, 0f);
            }

            return new AoeShape(
                ProjectileTargetShapeUtility.ShapeType(collider),
                ProjectileTargetShapeUtility.Radius(collider, definition.FallbackRadius),
                ProjectileTargetShapeUtility.HalfExtents(collider, definition.FallbackRadius),
                ProjectileTargetShapeUtility.RotationRadians(collider));
        }
    }

    [global::System.Serializable]
    public sealed class AoeTypeDefinition
    {
        [SerializeField] private int typeId;
        [SerializeField] private GameObject visualPrefab;
        [SerializeField] private Collider2D collisionShape;
        [SerializeField, Min(0.01f)] private float fallbackRadius = 1f;
        [SerializeField, Min(0)] private int preloadCount;

        public int TypeId => typeId;
        public GameObject VisualPrefab => visualPrefab;
        public Collider2D CollisionShape => collisionShape;
        public float FallbackRadius => fallbackRadius;
        public int PreloadCount => preloadCount;
    }
}

using System.Collections.Generic;
using PlayGround.System.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;

namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AoeSimulationSystem))]
    [UpdateBefore(typeof(AoeCollisionSystem))]
    public partial class AoeSpawnSystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnFrameTimeProfilerMarker =
            new("Aoe.Spawn.FrameTime");

        private readonly Dictionary<AoePoolKey, List<Entity>> inactiveByKey = new();
        private EntityArchetype archetype;
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeScope>(),
                ComponentType.ReadWrite<AoeSpawnRequestElement>(),
                ComponentType.ReadWrite<AoeRecycleElement>());
            archetype = EntityManager.CreateArchetype(
                typeof(AoeTag),
                typeof(AoeIdentityComponent),
                typeof(AoeLifetimeComponent),
                typeof(AoeHitGateComponent),
                typeof(AoeHitSpawnComponent),
                typeof(AoeRenderComponent),
                typeof(AoeRenderElement),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatHitComponent),
                typeof(AoeActiveTag),
                typeof(AoeContactGateElement));
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            using var scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            bool hasSpawnRequests = HasSpawnRequests(scopes);
            bool hasRecycleEvents = HasRecycleEvents(scopes);
            if (!hasSpawnRequests && !hasRecycleEvents)
            {
                return;
            }

            using (SpawnFrameTimeProfilerMarker.Auto())
            {
                DrainRecycleBuffers(scopes);
                if (!hasSpawnRequests)
                {
                    return;
                }

                using var createEcb = new EntityCommandBuffer(Allocator.Temp);
                for (int i = 0; i < scopes.Length; i++)
                {
                    Entity scope = scopes[i];
                    DynamicBuffer<AoeSpawnRequestElement> requests =
                        EntityManager.GetBuffer<AoeSpawnRequestElement>(scope);
                    for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
                    {
                        Materialize(scope, requests[requestIndex], createEcb);
                    }

                    requests.Clear();
                }

                createEcb.Playback(EntityManager);
            }
        }

        private bool HasSpawnRequests(NativeArray<Entity> scopes)
        {
            for (int i = 0; i < scopes.Length; i++)
            {
                if (EntityManager.GetBuffer<AoeSpawnRequestElement>(scopes[i]).Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRecycleEvents(NativeArray<Entity> scopes)
        {
            for (int i = 0; i < scopes.Length; i++)
            {
                if (EntityManager.GetBuffer<AoeRecycleElement>(scopes[i]).Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrainRecycleBuffers(NativeArray<Entity> scopes)
        {
            for (int i = 0; i < scopes.Length; i++)
            {
                Entity scope = scopes[i];
                DynamicBuffer<AoeRecycleElement> recycled =
                    EntityManager.GetBuffer<AoeRecycleElement>(scope);
                for (int recycleIndex = 0; recycleIndex < recycled.Length; recycleIndex++)
                {
                    AoeRecycleElement recycle = recycled[recycleIndex];
                    if (!EntityManager.Exists(recycle.AoeEntity)
                        || EntityManager.IsComponentEnabled<AoeActiveTag>(recycle.AoeEntity))
                    {
                        continue;
                    }

                    AddInactive(new AoePoolKey(scope, recycle.TypeId), recycle.AoeEntity);
                }

                recycled.Clear();
            }
        }

        private void AddInactive(AoePoolKey key, Entity entity)
        {
            if (!inactiveByKey.TryGetValue(key, out List<Entity> inactive))
            {
                inactive = new List<Entity>();
                inactiveByKey.Add(key, inactive);
            }

            inactive.Add(entity);
        }

        private void Materialize(Entity scope, AoeSpawnRequestElement request, EntityCommandBuffer ecb)
        {
            var poolKey = new AoePoolKey(scope, request.TypeId);
            Entity entity = TakeInactive(poolKey);
            if (entity == Entity.Null)
            {
                entity = ecb.CreateEntity(archetype);
                ecb.AddSharedComponent(entity, new AoeRenderScope { Scope = scope });
                RecordAoeReset(ecb, entity, scope, request);
                return;
            }

            ResetAoeEntity(entity, scope, request);
        }

        private Entity TakeInactive(AoePoolKey key)
        {
            if (!inactiveByKey.TryGetValue(key, out List<Entity> inactive))
            {
                return Entity.Null;
            }

            while (inactive.Count > 0)
            {
                int index = inactive.Count - 1;
                Entity entity = inactive[index];
                inactive.RemoveAt(index);
                if (EntityManager.Exists(entity)
                    && !EntityManager.IsComponentEnabled<AoeActiveTag>(entity))
                {
                    return entity;
                }
            }

            return Entity.Null;
        }

        private void ResetAoeEntity(Entity entity, Entity scope, AoeSpawnRequestElement request)
        {
            EntityManager.SetComponentData(entity, IdentityFor(scope, request));
            EntityManager.SetComponentData(entity, KinematicsFor(request));
            EntityManager.SetComponentData(entity, CollisionFor(request));
            EntityManager.SetComponentData(entity, HitFor(request));
            EntityManager.SetComponentData(entity, LifetimeFor(request));
            EntityManager.SetComponentData(entity, HitGateFor(request));
            EntityManager.SetComponentData(entity, HitSpawnFor(request));
            EntityManager.SetComponentData(entity, request.Render);
            EntityManager.SetComponentData(entity, new AoeRenderElement());
            EntityManager.GetBuffer<AoeContactGateElement>(entity).Clear();
            EntityManager.SetComponentEnabled<AoeActiveTag>(entity, true);
        }

        private static void RecordAoeReset(EntityCommandBuffer ecb, Entity entity, Entity scope, AoeSpawnRequestElement request)
        {
            ecb.SetComponent(entity, IdentityFor(scope, request));
            ecb.SetComponent(entity, KinematicsFor(request));
            ecb.SetComponent(entity, CollisionFor(request));
            ecb.SetComponent(entity, HitFor(request));
            ecb.SetComponent(entity, LifetimeFor(request));
            ecb.SetComponent(entity, HitGateFor(request));
            ecb.SetComponent(entity, HitSpawnFor(request));
            ecb.SetComponent(entity, request.Render);
            ecb.SetComponent(entity, new AoeRenderElement());
            ecb.SetComponentEnabled<AoeActiveTag>(entity, true);
        }

        private static AoeIdentityComponent IdentityFor(Entity scope, AoeSpawnRequestElement request)
        {
            return new AoeIdentityComponent
            {
                Scope = scope,
                AoeId = request.AoeId,
                TypeId = request.TypeId
            };
        }

        private static CombatKinematicsComponent KinematicsFor(AoeSpawnRequestElement request)
        {
            return new CombatKinematicsComponent
            {
                Position = request.Position,
                Velocity = default
            };
        }

        private static CombatCollisionComponent CollisionFor(AoeSpawnRequestElement request)
        {
            return new CombatCollisionComponent
            {
                ShapeType = request.ShapeType,
                Radius = request.Radius,
                HalfExtents = request.HalfExtents,
                RotationRadians = request.RotationRadians,
                BoundsMin = request.BoundsMin,
                BoundsMax = request.BoundsMax
            };
        }

        private static CombatHitComponent HitFor(AoeSpawnRequestElement request)
        {
            return new CombatHitComponent
            {
                TargetMask = request.TargetMask,
                DamageAmount = request.DamageAmount,
                DirectDamageEnabled = true
            };
        }

        private static AoeLifetimeComponent LifetimeFor(AoeSpawnRequestElement request)
        {
            return new AoeLifetimeComponent
            {
                RemainingLifetime = request.Lifetime,
                IsPulse = 1
            };
        }

        private static AoeHitGateComponent HitGateFor(AoeSpawnRequestElement request)
        {
            return new AoeHitGateComponent
            {
                RepeatHitCooldownSeconds = request.RepeatHitCooldownSeconds
            };
        }

        private static AoeHitSpawnComponent HitSpawnFor(AoeSpawnRequestElement request)
        {
            return new AoeHitSpawnComponent
            {
                ProjectileBurst = request.ProjectileBurst
            };
        }

        private readonly struct AoePoolKey : global::System.IEquatable<AoePoolKey>
        {
            private readonly Entity scope;
            private readonly int typeId;

            public AoePoolKey(Entity scope, int typeId)
            {
                this.scope = scope;
                this.typeId = typeId;
            }

            public bool Equals(AoePoolKey other) =>
                scope == other.scope
                && typeId == other.typeId;

            public override bool Equals(object obj) =>
                obj is AoePoolKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (scope.GetHashCode() * 397) ^ typeId;
                }
            }
        }
    }
}

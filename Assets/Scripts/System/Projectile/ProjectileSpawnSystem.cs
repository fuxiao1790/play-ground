using System.Collections.Generic;
using PlayGround.System.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSimulationSystem))]
    [UpdateBefore(typeof(ProjectileTrackingSystem))]
    public partial class ProjectileSpawnSystem : SystemBase
    {
        private const int MaxStructuralRenderTypes = 16;
        private static readonly ProfilerMarker SpawnFrameTimeProfilerMarker =
            new("Projectile.Spawn.FrameTime");

        private readonly Dictionary<ProjectilePoolKey, List<Entity>> inactiveByKey = new();
        private readonly Dictionary<ProjectileArchetypeKey, EntityArchetype> archetypesByKey = new();

        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileScope>(),
                ComponentType.ReadWrite<ProjectileSpawnRequestElement>(),
                ComponentType.ReadWrite<ProjectileRecycleElement>());
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            using var scopes = scopeQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
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
                    DynamicBuffer<ProjectileSpawnRequestElement> requests =
                        EntityManager.GetBuffer<ProjectileSpawnRequestElement>(scope);
                    for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
                    {
                        Materialize(scope, requests[requestIndex], createEcb);
                    }

                    requests.Clear();
                }

                createEcb.Playback(EntityManager);
            }
        }

        private bool HasSpawnRequests(Unity.Collections.NativeArray<Entity> scopes)
        {
            for (int i = 0; i < scopes.Length; i++)
            {
                if (EntityManager.GetBuffer<ProjectileSpawnRequestElement>(scopes[i]).Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRecycleEvents(Unity.Collections.NativeArray<Entity> scopes)
        {
            for (int i = 0; i < scopes.Length; i++)
            {
                if (EntityManager.GetBuffer<ProjectileRecycleElement>(scopes[i]).Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrainRecycleBuffers(Unity.Collections.NativeArray<Entity> scopes)
        {
            for (int i = 0; i < scopes.Length; i++)
            {
                Entity scope = scopes[i];
                DynamicBuffer<ProjectileRecycleElement> recycled =
                    EntityManager.GetBuffer<ProjectileRecycleElement>(scope);
                for (int recycleIndex = 0; recycleIndex < recycled.Length; recycleIndex++)
                {
                    ProjectileRecycleElement recycle = recycled[recycleIndex];
                    if (!EntityManager.Exists(recycle.ProjectileEntity)
                        || EntityManager.IsComponentEnabled<ProjectileActiveTag>(recycle.ProjectileEntity))
                    {
                        continue;
                    }

                    AddInactive(new ProjectilePoolKey(
                        scope,
                        recycle.TypeId,
                        recycle.HasChildSpawner != 0), recycle.ProjectileEntity);
                }

                recycled.Clear();
            }
        }

        private void AddInactive(ProjectilePoolKey key, Entity entity)
        {
            if (!inactiveByKey.TryGetValue(key, out List<Entity> inactive))
            {
                inactive = new List<Entity>();
                inactiveByKey.Add(key, inactive);
            }

            inactive.Add(entity);
        }

        private void Materialize(
            Entity scope,
            ProjectileSpawnRequestElement request,
            EntityCommandBuffer createEcb)
        {
            bool hasChildSpawner = request.HasChildSpawner != 0;
            var poolKey = new ProjectilePoolKey(scope, request.TypeId, hasChildSpawner);
            Entity entity = TakeInactive(poolKey);
            if (entity == Entity.Null)
            {
                CreateProjectileEntity(scope, request, hasChildSpawner, createEcb);
                return;
            }

            ResetProjectileEntity(entity, scope, request, hasChildSpawner);
        }

        private Entity TakeInactive(ProjectilePoolKey key)
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
                    && !EntityManager.IsComponentEnabled<ProjectileActiveTag>(entity))
                {
                    return entity;
                }
            }

            return Entity.Null;
        }

        private void CreateProjectileEntity(
            Entity scope,
            ProjectileSpawnRequestElement request,
            bool hasChildSpawner,
            EntityCommandBuffer ecb)
        {
            Entity entity = ecb.CreateEntity(ArchetypeFor(request.TypeId, hasChildSpawner));
            ecb.AddSharedComponent(entity, new CombatRenderScope { Scope = scope });
            RecordProjectileReset(ecb, entity, scope, request, hasChildSpawner);
        }

        private void ResetProjectileEntity(
            Entity entity,
            Entity scope,
            ProjectileSpawnRequestElement request,
            bool hasChildSpawner)
        {
            EntityManager.SetComponentData(entity, new ProjectileIdentityComponent
            {
                Scope = scope,
                ProjectileId = request.ProjectileId,
                TypeId = request.TypeId
            });
            EntityManager.SetComponentData(entity, new CombatKinematicsComponent
            {
                Position = request.Position,
                Velocity = request.Velocity
            });
            EntityManager.SetComponentData(entity, new CombatCollisionComponent
            {
                ShapeType = request.ShapeType,
                Radius = request.Radius,
                HalfExtents = request.HalfExtents,
                RotationRadians = request.RotationRadians,
                BoundsMin = request.BoundsMin,
                BoundsMax = request.BoundsMax
            });
            EntityManager.SetComponentData(entity, new CombatHitComponent
            {
                TargetMask = request.TargetMask,
                DamageAmount = request.HitPayload.DamageAmount,
                DirectDamageEnabled = request.HitPayload.DirectDamageEnabled,
                SourceNodeId = request.HitPayload.SourceNodeId
            });
            EntityManager.SetComponentData(entity, new ProjectileLifetimeComponent
            {
                RemainingLifetime = request.Lifetime
            });
            EntityManager.SetComponentData(entity, new ProjectileHitComponent
            {
                PierceRemaining = request.PierceRemaining,
                RepeatHitCooldownSeconds = request.RepeatHitCooldownSeconds,
                HitPayload = request.HitPayload
            });
            EntityManager.SetComponentData(entity, request.Tracking);
            EntityManager.SetComponentData(entity, request.Render);
            EntityManager.SetComponentData(entity, new CombatRenderElement());
            EntityManager.GetBuffer<ProjectileContactGateElement>(entity).Clear();

            if (hasChildSpawner)
            {
                EntityManager.SetComponentData(entity, request.ChildSpawner);
                EntityManager.SetComponentData(entity, request.ChildSpawnState);
            }

            EntityManager.SetComponentEnabled<ProjectileActiveTag>(entity, true);
            EntityManager.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
        }

        private static void RecordProjectileReset(
            EntityCommandBuffer ecb,
            Entity entity,
            Entity scope,
            ProjectileSpawnRequestElement request,
            bool hasChildSpawner)
        {
            ecb.SetComponent(entity, new ProjectileIdentityComponent
            {
                Scope = scope,
                ProjectileId = request.ProjectileId,
                TypeId = request.TypeId
            });
            ecb.SetComponent(entity, new CombatKinematicsComponent
            {
                Position = request.Position,
                Velocity = request.Velocity
            });
            ecb.SetComponent(entity, new CombatCollisionComponent
            {
                ShapeType = request.ShapeType,
                Radius = request.Radius,
                HalfExtents = request.HalfExtents,
                RotationRadians = request.RotationRadians,
                BoundsMin = request.BoundsMin,
                BoundsMax = request.BoundsMax
            });
            ecb.SetComponent(entity, new CombatHitComponent
            {
                TargetMask = request.TargetMask,
                DamageAmount = request.HitPayload.DamageAmount,
                DirectDamageEnabled = request.HitPayload.DirectDamageEnabled,
                SourceNodeId = request.HitPayload.SourceNodeId
            });
            ecb.SetComponent(entity, new ProjectileLifetimeComponent
            {
                RemainingLifetime = request.Lifetime
            });
            ecb.SetComponent(entity, new ProjectileHitComponent
            {
                PierceRemaining = request.PierceRemaining,
                RepeatHitCooldownSeconds = request.RepeatHitCooldownSeconds,
                HitPayload = request.HitPayload
            });
            ecb.SetComponent(entity, request.Tracking);
            ecb.SetComponent(entity, request.Render);
            ecb.SetComponent(entity, new CombatRenderElement());

            if (hasChildSpawner)
            {
                ecb.SetComponent(entity, request.ChildSpawner);
                ecb.SetComponent(entity, request.ChildSpawnState);
            }

            ecb.SetComponentEnabled<ProjectileActiveTag>(entity, true);
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
        }

        private EntityArchetype ArchetypeFor(int typeId, bool hasChildSpawner)
        {
            var key = new ProjectileArchetypeKey(typeId, hasChildSpawner);
            if (archetypesByKey.TryGetValue(key, out EntityArchetype archetype))
            {
                return archetype;
            }

            archetype = hasChildSpawner
                ? EntityManager.CreateArchetype(
                    typeof(ProjectileTag),
                    typeof(ProjectileIdentityComponent),
                    typeof(CombatKinematicsComponent),
                    typeof(CombatCollisionComponent),
                    typeof(CombatHitComponent),
                    typeof(ProjectileLifetimeComponent),
                    typeof(ProjectileHitComponent),
                    typeof(ProjectileTrackingComponent),
                    typeof(CombatRenderComponent),
                    typeof(CombatRenderElement),
                    RenderTagTypeFor(typeId),
                    typeof(ProjectileActiveTag),
                    typeof(CombatRenderActiveTag),
                    typeof(ProjectileContactGateElement),
                    typeof(ProjectileChildSpawnerTag),
                    typeof(ProjectileChildSpawnerComponent),
                    typeof(ProjectileChildSpawnStateComponent))
                : EntityManager.CreateArchetype(
                    typeof(ProjectileTag),
                    typeof(ProjectileIdentityComponent),
                    typeof(CombatKinematicsComponent),
                    typeof(CombatCollisionComponent),
                    typeof(CombatHitComponent),
                    typeof(ProjectileLifetimeComponent),
                    typeof(ProjectileHitComponent),
                    typeof(ProjectileTrackingComponent),
                    typeof(CombatRenderComponent),
                    typeof(CombatRenderElement),
                    RenderTagTypeFor(typeId),
                    typeof(ProjectileActiveTag),
                    typeof(CombatRenderActiveTag),
                    typeof(ProjectileContactGateElement));

            archetypesByKey.Add(key, archetype);
            return archetype;
        }

        private static global::System.Type RenderTagTypeFor(int typeId)
        {
            return typeId switch
            {
                0 => typeof(ProjectileRenderType0Tag),
                1 => typeof(ProjectileRenderType1Tag),
                2 => typeof(ProjectileRenderType2Tag),
                3 => typeof(ProjectileRenderType3Tag),
                4 => typeof(ProjectileRenderType4Tag),
                5 => typeof(ProjectileRenderType5Tag),
                6 => typeof(ProjectileRenderType6Tag),
                7 => typeof(ProjectileRenderType7Tag),
                8 => typeof(ProjectileRenderType8Tag),
                9 => typeof(ProjectileRenderType9Tag),
                10 => typeof(ProjectileRenderType10Tag),
                11 => typeof(ProjectileRenderType11Tag),
                12 => typeof(ProjectileRenderType12Tag),
                13 => typeof(ProjectileRenderType13Tag),
                14 => typeof(ProjectileRenderType14Tag),
                15 => typeof(ProjectileRenderType15Tag),
                _ => throw new global::System.InvalidOperationException(
                    $"Projectile render type {typeId} is outside supported structural render type range 0-{MaxStructuralRenderTypes - 1}.")
            };
        }

        private readonly struct ProjectilePoolKey : global::System.IEquatable<ProjectilePoolKey>
        {
            private readonly Entity scope;
            private readonly int typeId;
            private readonly bool hasChildSpawner;

            public ProjectilePoolKey(Entity scope, int typeId, bool hasChildSpawner)
            {
                this.scope = scope;
                this.typeId = typeId;
                this.hasChildSpawner = hasChildSpawner;
            }

            public bool Equals(ProjectilePoolKey other) =>
                scope == other.scope
                && typeId == other.typeId
                && hasChildSpawner == other.hasChildSpawner;

            public override bool Equals(object obj) =>
                obj is ProjectilePoolKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = scope.GetHashCode();
                    hash = (hash * 397) ^ typeId;
                    hash = (hash * 397) ^ (hasChildSpawner ? 1 : 0);
                    return hash;
                }
            }
        }

        private readonly struct ProjectileArchetypeKey : global::System.IEquatable<ProjectileArchetypeKey>
        {
            private readonly int typeId;
            private readonly bool hasChildSpawner;

            public ProjectileArchetypeKey(int typeId, bool hasChildSpawner)
            {
                this.typeId = typeId;
                this.hasChildSpawner = hasChildSpawner;
            }

            public bool Equals(ProjectileArchetypeKey other) =>
                typeId == other.typeId
                && hasChildSpawner == other.hasChildSpawner;

            public override bool Equals(object obj) =>
                obj is ProjectileArchetypeKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (typeId * 397) ^ (hasChildSpawner ? 1 : 0);
                }
            }
        }
    }
}

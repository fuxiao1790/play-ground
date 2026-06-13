using System.Collections.Generic;
using PlayGround.System.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSimulationSystem))]
    [UpdateBefore(typeof(ProjectileTrackingSystem))]
    public partial class ProjectileSpawnSystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker = new("Projectile.Spawn");
        private static readonly ProfilerCounterValue<int> SpawnColdCreateCounter =
            new(ProfilerCategory.Scripts, "Projectile.Spawn.Cold", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "Projectile.Spawn.Reuse", ProfilerMarkerDataUnit.Count);

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

            DrainRecycleBuffers(scopes);
            if (!hasSpawnRequests)
            {
                return;
            }

            using (SpawnMarker.Auto())
            {
                using var createEcb = new EntityCommandBuffer(Allocator.Temp);
                var reuseResets = new NativeList<ProjectileReuseReset>(Allocator.TempJob);
                int totalSpawnRequests = 0;
                for (int i = 0; i < scopes.Length; i++)
                {
                    Entity scope = scopes[i];
                    DynamicBuffer<ProjectileSpawnRequestElement> requests =
                        EntityManager.GetBuffer<ProjectileSpawnRequestElement>(scope);
                    totalSpawnRequests += requests.Length;
                    for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
                    {
                        Materialize(scope, requests[requestIndex], createEcb, ref reuseResets);
                    }

                    requests.Clear();
                }

                int reuseCount = reuseResets.Length;
                createEcb.Playback(EntityManager);
                ScheduleReuseResetJob(reuseResets);
                SpawnReuseCounter.Value = reuseCount;
                SpawnColdCreateCounter.Value = totalSpawnRequests - reuseCount;
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
            EntityCommandBuffer createEcb,
            ref NativeList<ProjectileReuseReset> reuseResets)
        {
            bool hasChildSpawner = request.HasChildSpawner != 0;
            var poolKey = new ProjectilePoolKey(scope, request.TypeId, hasChildSpawner);
            Entity entity = TakeInactive(poolKey);
            if (entity == Entity.Null)
            {
                CreateProjectileEntity(scope, request, hasChildSpawner, createEcb);
                return;
            }

            reuseResets.Add(new ProjectileReuseReset
            {
                Entity = entity,
                Scope = scope,
                Request = request
            });
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
            Entity entity = ecb.CreateEntity(ArchetypeFor(hasChildSpawner));
            ecb.AddSharedComponent(entity, new CombatRenderScope { Scope = scope });
            ecb.AddSharedComponent(entity, new CombatRenderTypeId { TypeId = request.TypeId });
            RecordProjectileReset(ecb, entity, scope, request, hasChildSpawner);
        }

        private void ScheduleReuseResetJob(NativeList<ProjectileReuseReset> reuseResets)
        {
            if (reuseResets.Length <= 0)
            {
                reuseResets.Dispose();
                return;
            }

            var job = new ProjectileReuseResetJob
            {
                Resets = reuseResets.AsArray(),
                Identities = GetComponentLookup<ProjectileIdentityComponent>(),
                Kinematics = GetComponentLookup<CombatKinematicsComponent>(),
                Collisions = GetComponentLookup<CombatCollisionComponent>(),
                Lifetimes = GetComponentLookup<ProjectileLifetimeComponent>(),
                ProjectileHits = GetComponentLookup<ProjectileHitComponent>(),
                Tracking = GetComponentLookup<ProjectileTrackingComponent>(),
                Renders = GetComponentLookup<CombatRenderComponent>(),
                RenderElements = GetComponentLookup<CombatRenderElement>(),
                ContactGates = GetBufferLookup<ProjectileContactGateElement>(),
                ChildSpawners = GetComponentLookup<ProjectileChildSpawnerComponent>(),
                ChildSpawnStates = GetComponentLookup<ProjectileChildSpawnStateComponent>(),
                ActiveTags = GetComponentLookup<ProjectileActiveTag>(),
                CollisionActiveTags = GetComponentLookup<ProjectileCollisionActiveTag>(),
                RenderActiveTags = GetComponentLookup<CombatRenderActiveTag>()
            };

            JobHandle resetHandle = job.Schedule(reuseResets.Length, 64, Dependency);
            Dependency = reuseResets.Dispose(resetHandle);
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
            ecb.SetComponentEnabled<ProjectileTrackingComponent>(entity, request.Tracking.TrackingEnabled);
            ecb.SetComponent(entity, request.Render);
            ecb.SetComponent(entity, new CombatRenderElement());

            if (hasChildSpawner)
            {
                ecb.SetComponent(entity, request.ChildSpawner);
                ecb.SetComponent(entity, request.ChildSpawnState);
            }

            if (request.SeedContactGateTargetId > 0)
            {
                ecb.AppendToBuffer(entity, new ProjectileContactGateElement
                {
                    TargetId = request.SeedContactGateTargetId,
                    CooldownRemaining = math.max(0.1f, request.RepeatHitCooldownSeconds)
                });
            }

            ecb.SetComponentEnabled<ProjectileActiveTag>(entity, true);
            ecb.SetComponentEnabled<ProjectileCollisionActiveTag>(entity, NeedsCollision(request.HitPayload));
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
        }

        private static bool NeedsCollision(in ProjectileHitPayload payload) =>
            payload.DirectDamageEnabled
            || payload.StackEffect.Enabled;

        private static ProjectileIdentityComponent IdentityFor(Entity scope, ProjectileSpawnRequestElement request)
        {
            return new ProjectileIdentityComponent
            {
                Scope = scope,
                ProjectileId = request.ProjectileId,
                TypeId = request.TypeId
            };
        }

        private static CombatKinematicsComponent KinematicsFor(ProjectileSpawnRequestElement request)
        {
            return new CombatKinematicsComponent
            {
                Position = request.Position,
                Velocity = request.Velocity
            };
        }

        private static CombatCollisionComponent CollisionFor(ProjectileSpawnRequestElement request)
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

        private static ProjectileLifetimeComponent LifetimeFor(ProjectileSpawnRequestElement request)
        {
            return new ProjectileLifetimeComponent
            {
                RemainingLifetime = request.Lifetime
            };
        }

        private static ProjectileHitComponent ProjectileHitFor(ProjectileSpawnRequestElement request)
        {
            return new ProjectileHitComponent
            {
                PierceRemaining = request.PierceRemaining,
                RepeatHitCooldownSeconds = request.RepeatHitCooldownSeconds,
                HitPayload = request.HitPayload
            };
        }

        private EntityArchetype ArchetypeFor(bool hasChildSpawner)
        {
            var key = new ProjectileArchetypeKey(hasChildSpawner);
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

                    typeof(ProjectileLifetimeComponent),
                    typeof(ProjectileHitComponent),
                    typeof(ProjectileTrackingComponent),
                    typeof(CombatRenderComponent),
                    typeof(CombatRenderElement),
                    typeof(ProjectileActiveTag),
                    typeof(ProjectileCollisionActiveTag),
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

                    typeof(ProjectileLifetimeComponent),
                    typeof(ProjectileHitComponent),
                    typeof(ProjectileTrackingComponent),
                    typeof(CombatRenderComponent),
                    typeof(CombatRenderElement),
                    typeof(ProjectileActiveTag),
                    typeof(ProjectileCollisionActiveTag),
                    typeof(CombatRenderActiveTag),
                    typeof(ProjectileContactGateElement));

            archetypesByKey.Add(key, archetype);
            return archetype;
        }

        private struct ProjectileReuseReset
        {
            public Entity Entity;
            public Entity Scope;
            public ProjectileSpawnRequestElement Request;
        }

        [BurstCompile]
        private struct ProjectileReuseResetJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ProjectileReuseReset> Resets;
            [NativeDisableParallelForRestriction] public ComponentLookup<ProjectileIdentityComponent> Identities;
            [NativeDisableParallelForRestriction] public ComponentLookup<CombatKinematicsComponent> Kinematics;
            [NativeDisableParallelForRestriction] public ComponentLookup<CombatCollisionComponent> Collisions;
            [NativeDisableParallelForRestriction] public ComponentLookup<ProjectileLifetimeComponent> Lifetimes;
            [NativeDisableParallelForRestriction] public ComponentLookup<ProjectileHitComponent> ProjectileHits;
            [NativeDisableParallelForRestriction] public ComponentLookup<ProjectileTrackingComponent> Tracking;
            [NativeDisableParallelForRestriction] public ComponentLookup<CombatRenderComponent> Renders;
            [NativeDisableParallelForRestriction] public ComponentLookup<CombatRenderElement> RenderElements;
            [NativeDisableParallelForRestriction] public BufferLookup<ProjectileContactGateElement> ContactGates;
            [NativeDisableParallelForRestriction] public ComponentLookup<ProjectileChildSpawnerComponent> ChildSpawners;
            [NativeDisableParallelForRestriction] public ComponentLookup<ProjectileChildSpawnStateComponent> ChildSpawnStates;
            [NativeDisableParallelForRestriction] public ComponentLookup<ProjectileActiveTag> ActiveTags;
            [NativeDisableParallelForRestriction] public ComponentLookup<ProjectileCollisionActiveTag> CollisionActiveTags;
            [NativeDisableParallelForRestriction] public ComponentLookup<CombatRenderActiveTag> RenderActiveTags;

            public void Execute(int index)
            {
                ProjectileReuseReset reset = Resets[index];
                Entity entity = reset.Entity;
                ProjectileSpawnRequestElement request = reset.Request;

                Identities[entity] = IdentityFor(reset.Scope, request);
                Kinematics[entity] = KinematicsFor(request);
                Collisions[entity] = CollisionFor(request);
                Lifetimes[entity] = LifetimeFor(request);
                ProjectileHits[entity] = ProjectileHitFor(request);
                Tracking[entity] = request.Tracking;
                Tracking.SetComponentEnabled(entity, request.Tracking.TrackingEnabled);
                Renders[entity] = request.Render;
                RenderElements[entity] = new CombatRenderElement();
                ContactGates[entity].Clear();
                if (request.SeedContactGateTargetId > 0)
                {
                    ContactGates[entity].Add(new ProjectileContactGateElement
                    {
                        TargetId = request.SeedContactGateTargetId,
                        CooldownRemaining = math.max(0.1f, request.RepeatHitCooldownSeconds)
                    });
                }

                if (request.HasChildSpawner != 0)
                {
                    ChildSpawners[entity] = request.ChildSpawner;
                    ChildSpawnStates[entity] = request.ChildSpawnState;
                }

                ActiveTags.SetComponentEnabled(entity, true);
                CollisionActiveTags.SetComponentEnabled(entity, NeedsCollision(request.HitPayload));
                RenderActiveTags.SetComponentEnabled(entity, true);
            }
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
            private readonly bool hasChildSpawner;

            public ProjectileArchetypeKey(bool hasChildSpawner)
            {
                this.hasChildSpawner = hasChildSpawner;
            }

            public bool Equals(ProjectileArchetypeKey other) =>
                hasChildSpawner == other.hasChildSpawner;

            public override bool Equals(object obj) =>
                obj is ProjectileArchetypeKey other && Equals(other);

            public override int GetHashCode() => hasChildSpawner ? 1 : 0;
        }
    }
}

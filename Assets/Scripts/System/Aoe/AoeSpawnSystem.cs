using System.Collections.Generic;
using PlayGround.System.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;

namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AoeSimulationSystem))]
    [UpdateBefore(typeof(AoeCollisionSystem))]
    [UpdateBefore(typeof(PlayGround.System.Common.CombatRenderPrepareSystem))]
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
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatHitComponent),
                typeof(AoeActiveTag),
                typeof(CombatRenderActiveTag),
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
                var reuseResets = new NativeList<AoeReuseReset>(Allocator.TempJob);
                for (int i = 0; i < scopes.Length; i++)
                {
                    Entity scope = scopes[i];
                    DynamicBuffer<AoeSpawnRequestElement> requests =
                        EntityManager.GetBuffer<AoeSpawnRequestElement>(scope);
                    for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
                    {
                        Materialize(scope, requests[requestIndex], createEcb, ref reuseResets);
                    }

                    requests.Clear();
                }

                createEcb.Playback(EntityManager);
                ScheduleReuseResetJob(reuseResets);
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

        private void Materialize(
            Entity scope,
            AoeSpawnRequestElement request,
            EntityCommandBuffer ecb,
            ref NativeList<AoeReuseReset> reuseResets)
        {
            var poolKey = new AoePoolKey(scope, request.TypeId);
            Entity entity = TakeInactive(poolKey);
            if (entity == Entity.Null)
            {
                entity = ecb.CreateEntity(archetype);
                ecb.AddSharedComponent(entity, new CombatRenderScope { Scope = scope });
                RecordAoeReset(ecb, entity, scope, request);
                return;
            }

            reuseResets.Add(new AoeReuseReset
            {
                Entity = entity,
                Scope = scope,
                Request = request
            });
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

        private void ScheduleReuseResetJob(NativeList<AoeReuseReset> reuseResets)
        {
            if (reuseResets.Length <= 0)
            {
                reuseResets.Dispose();
                return;
            }

            var job = new AoeReuseResetJob
            {
                Resets = reuseResets.AsArray(),
                Identities = GetComponentLookup<AoeIdentityComponent>(),
                Kinematics = GetComponentLookup<CombatKinematicsComponent>(),
                Collisions = GetComponentLookup<CombatCollisionComponent>(),
                Hits = GetComponentLookup<CombatHitComponent>(),
                Lifetimes = GetComponentLookup<AoeLifetimeComponent>(),
                HitGates = GetComponentLookup<AoeHitGateComponent>(),
                HitSpawns = GetComponentLookup<AoeHitSpawnComponent>(),
                Renders = GetComponentLookup<CombatRenderComponent>(),
                RenderElements = GetComponentLookup<CombatRenderElement>(),
                ContactGates = GetBufferLookup<AoeContactGateElement>(),
                ActiveTags = GetComponentLookup<AoeActiveTag>(),
                RenderActiveTags = GetComponentLookup<CombatRenderActiveTag>()
            };

            JobHandle resetHandle = job.Schedule(reuseResets.Length, 64, Dependency);
            Dependency = reuseResets.Dispose(resetHandle);
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
            ecb.SetComponent(entity, new CombatRenderElement());
            ecb.SetComponentEnabled<AoeActiveTag>(entity, true);
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
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
                IsPulse = request.Lifetime <= 0f ? 1 : 0
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

        private struct AoeReuseReset
        {
            public Entity Entity;
            public Entity Scope;
            public AoeSpawnRequestElement Request;
        }

        [BurstCompile]
        private struct AoeReuseResetJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<AoeReuseReset> Resets;
            [NativeDisableParallelForRestriction] public ComponentLookup<AoeIdentityComponent> Identities;
            [NativeDisableParallelForRestriction] public ComponentLookup<CombatKinematicsComponent> Kinematics;
            [NativeDisableParallelForRestriction] public ComponentLookup<CombatCollisionComponent> Collisions;
            [NativeDisableParallelForRestriction] public ComponentLookup<CombatHitComponent> Hits;
            [NativeDisableParallelForRestriction] public ComponentLookup<AoeLifetimeComponent> Lifetimes;
            [NativeDisableParallelForRestriction] public ComponentLookup<AoeHitGateComponent> HitGates;
            [NativeDisableParallelForRestriction] public ComponentLookup<AoeHitSpawnComponent> HitSpawns;
            [NativeDisableParallelForRestriction] public ComponentLookup<CombatRenderComponent> Renders;
            [NativeDisableParallelForRestriction] public ComponentLookup<CombatRenderElement> RenderElements;
            [NativeDisableParallelForRestriction] public BufferLookup<AoeContactGateElement> ContactGates;
            [NativeDisableParallelForRestriction] public ComponentLookup<AoeActiveTag> ActiveTags;
            [NativeDisableParallelForRestriction] public ComponentLookup<CombatRenderActiveTag> RenderActiveTags;

            public void Execute(int index)
            {
                AoeReuseReset reset = Resets[index];
                Entity entity = reset.Entity;
                AoeSpawnRequestElement request = reset.Request;

                Identities[entity] = IdentityFor(reset.Scope, request);
                Kinematics[entity] = KinematicsFor(request);
                Collisions[entity] = CollisionFor(request);
                Hits[entity] = HitFor(request);
                Lifetimes[entity] = LifetimeFor(request);
                HitGates[entity] = HitGateFor(request);
                HitSpawns[entity] = HitSpawnFor(request);
                Renders[entity] = request.Render;
                RenderElements[entity] = new CombatRenderElement();
                ContactGates[entity].Clear();
                ActiveTags.SetComponentEnabled(entity, true);
                RenderActiveTags.SetComponentEnabled(entity, true);
            }
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

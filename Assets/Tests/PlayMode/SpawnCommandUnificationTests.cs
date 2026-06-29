using NUnit.Framework;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Hash128 = Unity.Entities.Hash128;
using Unity.Mathematics;

namespace PlayGround.Tests.PlayMode
{
    /// <summary>
    /// Covers the cross-cutting invariants of the unified spawn-command-registry model:
    /// deduplication, frozen registry, and deterministic per-instance id stamping.
    /// </summary>
    public sealed class SpawnCommandUnificationTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private SimulationSystemGroup simGroup;
        private AoeSpawnExpansionSystem aoeExpansion;
        private ProjectileSpawnExpansionSystem projectileExpansion;
        private Entity scopeEntity;
        private Entity aoeTemplateEntity;
        private Entity projectileTemplateEntity;
        private double elapsedTime;
        private int nextAoeId;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("SpawnCommandUnificationTest");
            entityManager = testWorld.EntityManager;
            simGroup = testWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();
            aoeExpansion = testWorld.GetOrCreateSystemManaged<AoeSpawnExpansionSystem>();
            projectileExpansion = testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>();
            simGroup.AddSystemToUpdateList(aoeExpansion);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<AoeSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatApplyFinalizeSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<StatusProcessSystem>());
            simGroup.AddSystemToUpdateList(projectileExpansion);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<BasicProjectileSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ChildSpawnerProjectileSpawnApplySystem>());
            simGroup.SortSystems();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<AoeSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<ProjectileSpawnEvent>(scopeEntity);

            aoeTemplateEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(aoeTemplateEntity, new AoeSpawnTemplate
            {
                Map = new NativeHashMap<Hash128, AoeSpawnCommand>(16, Allocator.Persistent)
            });

            projectileTemplateEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(projectileTemplateEntity, new ProjectileSpawnTemplate
            {
                Map = new NativeHashMap<Hash128, ProjectileSpawnCommand>(16, Allocator.Persistent)
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld.IsCreated)
            {
                if (entityManager.Exists(aoeTemplateEntity))
                {
                    AoeSpawnTemplate t = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);
                    if (t.Map.IsCreated) t.Map.Dispose();
                }
                if (entityManager.Exists(projectileTemplateEntity))
                {
                    ProjectileSpawnTemplate t = entityManager.GetComponentData<ProjectileSpawnTemplate>(projectileTemplateEntity);
                    if (t.Map.IsCreated) t.Map.Dispose();
                }
                testWorld.Dispose();
            }
        }

        // ---- Deduplication ----

        [Test]
        public void SpawnTemplateHash_IdenticalAoeCommandsProduceSameKey()
        {
            AoeSpawnCommand a = MakeAoeTemplate(radius: 2f, typeId: 1, count: 3);
            AoeSpawnCommand b = MakeAoeTemplate(radius: 2f, typeId: 1, count: 3);

            Hash128 keyA = SpawnTemplateHash.Of(in a);
            Hash128 keyB = SpawnTemplateHash.Of(in b);

            Assert.That(keyA, Is.EqualTo(keyB));
        }

        [Test]
        public void SpawnTemplateHash_DifferentCountProducesDistinctKey()
        {
            AoeSpawnCommand a = MakeAoeTemplate(radius: 2f, typeId: 1, count: 3);
            AoeSpawnCommand b = MakeAoeTemplate(radius: 2f, typeId: 1, count: 5);

            Hash128 keyA = SpawnTemplateHash.Of(in a);
            Hash128 keyB = SpawnTemplateHash.Of(in b);

            Assert.That(keyA, Is.Not.EqualTo(keyB));
        }

        [Test]
        public void SpawnTemplateHash_PriorCountReusesPriorKey()
        {
            AoeSpawnCommand original = MakeAoeTemplate(radius: 2f, typeId: 1, count: 3);
            AoeSpawnCommand changed = MakeAoeTemplate(radius: 2f, typeId: 1, count: 5);
            AoeSpawnCommand reverted = MakeAoeTemplate(radius: 2f, typeId: 1, count: 3);

            Hash128 keyOriginal = SpawnTemplateHash.Of(in original);
            Hash128 keyChanged = SpawnTemplateHash.Of(in changed);
            Hash128 keyReverted = SpawnTemplateHash.Of(in reverted);

            Assert.That(keyChanged, Is.Not.EqualTo(keyOriginal));
            Assert.That(keyReverted, Is.EqualTo(keyOriginal));
        }

        [Test]
        public void RegistryContainsOnlyOneEntryForIdenticalTemplates()
        {
            AoeSpawnTemplate registry = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);

            AoeSpawnCommand template = MakeAoeTemplate(radius: 1f, typeId: 7, count: 2);
            Hash128 key = SpawnTemplateHash.Of(in template);

            registry.Map.TryAdd(key, template);
            registry.Map.TryAdd(key, template); // second add is a no-op

            Assert.That(registry.Map.Count, Is.EqualTo(1));
        }

        // ---- Frozen registry ----

        [Test]
        public void RegistryCountIsUnchangedAfterSimulationTick()
        {
            AoeSpawnTemplate registry = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);

            AoeSpawnCommand t1 = MakeAoeTemplate(radius: 1f, typeId: 1, count: 1);
            AoeSpawnCommand t2 = MakeAoeTemplate(radius: 2f, typeId: 2, count: 2);
            AoeSpawnCommand t3 = MakeAoeTemplate(radius: 3f, typeId: 3, count: 3);
            registry.Map.TryAdd(SpawnTemplateHash.Of(in t1), t1);
            registry.Map.TryAdd(SpawnTemplateHash.Of(in t2), t2);
            registry.Map.TryAdd(SpawnTemplateHash.Of(in t3), t3);
            int countBefore = registry.Map.Count;

            Tick(0.01f); // no entities — all systems do nothing

            // Re-read after the tick (component is a struct; the map reference is stable).
            registry = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);
            int countAfter = registry.Map.Count;

            Assert.That(countAfter, Is.EqualTo(countBefore),
                "No system may write the spawn-template registry during a simulation tick.");
        }

        // ---- Per-instance determinism ----

        [Test]
        public void DeterministicIds_SequentialAoeIdsMatchSourceIdBase()
        {
            // When DeterministicIdTickIndex == 0, expansion assigns sequential ids:
            // AoeId_i = SourceId + i. Verifies the deterministic stamping contract.
            var template = new AoeSpawnCommand
            {
                TypeId = 1,
                Count = 3,
                Radius = 1f,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new CombatHitPayload { DamageAmount = 1f, DirectDamageEnabled = true }
            };
            Hash128 key = SpawnTemplateHash.Of(in template);
            AoeSpawnTemplate registry = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);
            registry.Map.TryAdd(key, template);

            entityManager.GetBuffer<AoeSpawnEvent>(scopeEntity).Add(new AoeSpawnEvent
            {
                Kind = IntervalChildKind.Aoe,
                TemplateKey = key,
                Position = float2.zero,
                Faction = CombatFaction.Player,
                SourceId = 100,
                JitterSeed = 0,
                DeterministicIdTickIndex = 0
            });

            Tick(0.01f);

            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AoeIdentityComponent>());
            using NativeArray<AoeIdentityComponent> identities =
                q.ToComponentDataArray<AoeIdentityComponent>(Allocator.Temp);

            Assert.That(identities.Length, Is.EqualTo(3));
            var ids = new int[3];
            for (int i = 0; i < identities.Length; i++)
                ids[i] = identities[i].AoeId;
            global::System.Array.Sort(ids);
            Assert.That(ids[0], Is.EqualTo(100));
            Assert.That(ids[1], Is.EqualTo(101));
            Assert.That(ids[2], Is.EqualTo(102));
        }

        // ---- Helpers ----

        private void Tick(float dt)
        {
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            simGroup.Update();
        }

        private static AoeSpawnCommand MakeAoeTemplate(float radius, int typeId, int count)
        {
            return new AoeSpawnCommand
            {
                TypeId = typeId,
                Count = count,
                Radius = radius,
                AreaSize = radius,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new CombatHitPayload { DamageAmount = 1f, DirectDamageEnabled = true }
            };
        }
    }
}

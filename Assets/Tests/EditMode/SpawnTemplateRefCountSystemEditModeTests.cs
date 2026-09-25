using NUnit.Framework;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Targeted;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.Tests.EditMode
{
    public sealed class SpawnTemplateRefCountSystemEditModeTests
    {
        private World _world;
        private SpawnTemplateRefCountSystem _system;
        private NativeHashMap<Hash128, ProjectileSpawnCommand> _projectileTemplates;
        private NativeHashMap<Hash128, AoeSpawnCommand> _aoeTemplates;
        private NativeHashMap<Hash128, TargetedSpawnCommand> _targetedTemplates;
        private NativeHashMap<Hash128, SpawnTemplateRefCount> _projectileCounts;
        private NativeHashMap<Hash128, SpawnTemplateRefCount> _aoeCounts;
        private NativeHashMap<Hash128, SpawnTemplateRefCount> _targetedCounts;
        private NativeQueue<SpawnTemplateRefDelta> _deltas;

        [SetUp]
        public void SetUp()
        {
            _world = new World("SpawnTemplateRefCountSystemEditModeTest");
            _system = _world.GetOrCreateSystemManaged<SpawnTemplateRefCountSystem>();
            _projectileTemplates = new NativeHashMap<Hash128, ProjectileSpawnCommand>(4, Allocator.Persistent);
            _aoeTemplates = new NativeHashMap<Hash128, AoeSpawnCommand>(4, Allocator.Persistent);
            _targetedTemplates = new NativeHashMap<Hash128, TargetedSpawnCommand>(4, Allocator.Persistent);
            _projectileCounts = new NativeHashMap<Hash128, SpawnTemplateRefCount>(4, Allocator.Persistent);
            _aoeCounts = new NativeHashMap<Hash128, SpawnTemplateRefCount>(4, Allocator.Persistent);
            _targetedCounts = new NativeHashMap<Hash128, SpawnTemplateRefCount>(4, Allocator.Persistent);
            _deltas = new NativeQueue<SpawnTemplateRefDelta>(Allocator.Persistent);

            Entity scope = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(scope, new ProjectileSpawnTemplate
            {
                Map = _projectileTemplates
            });
            _world.EntityManager.AddComponentData(scope, new AoeSpawnTemplate
            {
                Map = _aoeTemplates
            });
            _world.EntityManager.AddComponentData(scope, new TargetedSpawnTemplate
            {
                Map = _targetedTemplates
            });
            _world.EntityManager.AddComponentData(scope, new SpawnTemplateRegistryState
            {
                ProjectileCounts = _projectileCounts,
                AoeCounts = _aoeCounts,
                TargetedCounts = _targetedCounts,
                Deltas = _deltas
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated)
            {
                _world.Dispose();
            }

            _projectileTemplates.Dispose();
            _aoeTemplates.Dispose();
            _targetedTemplates.Dispose();
            _projectileCounts.Dispose();
            _aoeCounts.Dispose();
            _targetedCounts.Dispose();
            _deltas.Dispose();
        }

        [Test]
        public void Update_AggregatesUnorderedAcquireAndReleaseBeforeApplyingCount()
        {
            var key = new Hash128(1u, 2u, 3u, 4u);
            _projectileTemplates.Add(key, default);
            _projectileCounts.Add(key, new SpawnTemplateRefCount { OwnerCount = 1 });

            // Parallel writers do not guarantee queue order. A same-tick release may be
            // observed before its matching acquire even though the net change is zero.
            _deltas.Enqueue(new SpawnTemplateRefDelta
            {
                Kind = IntervalChildKind.Projectile,
                Key = key,
                Delta = -1
            });
            _deltas.Enqueue(new SpawnTemplateRefDelta
            {
                Kind = IntervalChildKind.Projectile,
                Key = key,
                Delta = 1
            });

            _system.Update();

            Assert.That(_projectileCounts[key].InstanceCount, Is.Zero);
            Assert.That(_projectileTemplates.ContainsKey(key), Is.True);
        }
    }
}

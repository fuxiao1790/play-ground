using System;
using System.Reflection;
using NUnit.Framework;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Vfx;
using Unity.Entities;

namespace PlayGround.Tests.EditMode
{
    public sealed class TargetedContractsEditModeTests
    {
        [Test]
        public void ChildKindFor_ZeroIsTargeted_AndPositiveIsLingeringTargeted()
        {
            Assert.That(TargetedVariant.ChildKindFor(0f), Is.EqualTo(IntervalChildKind.Targeted));
            Assert.That(TargetedVariant.ChildKindFor(0.1f), Is.EqualTo(IntervalChildKind.LingeringTargeted));
        }

        [Test]
        public void TimingFor_SingleHit_ReturnsZeroDurationAndInterval()
        {
            VfxTimingData timing = TargetedVfxUtility.TimingFor(new TargetedSpawnCommand());

            Assert.That(timing.Duration, Is.Zero);
            Assert.That(timing.TickInterval, Is.Zero);
        }

        [Test]
        public void TimingFor_Lingering_ReturnsAuthoredDurationAndInterval()
        {
            TargetedSpawnCommand command = new()
            {
                LifetimeSeconds = 2.5f,
                TickIntervalSeconds = 0.75f
            };

            VfxTimingData timing = TargetedVfxUtility.TimingFor(command);

            Assert.That(timing.Duration, Is.EqualTo(2.5f));
            Assert.That(timing.TickInterval, Is.EqualTo(0.75f));
        }

        [Test]
        public void TargetedVfxContract_UsesIntOnlyIdsAndDedicatedVisualSizeComponent()
        {
            FieldInfo vfxIds = typeof(TargetedSpawnCommand).GetField(nameof(TargetedSpawnCommand.VfxIds));
            FieldInfo vfxSize = typeof(TargetedSpawnCommand).GetField(nameof(TargetedSpawnCommand.VfxSize));
            FieldInfo[] idFields = typeof(TargetedVfxIds).GetFields(BindingFlags.Instance | BindingFlags.Public);
            FieldInfo[] sizeFields = typeof(TargetedVfxSizeComponent).GetFields(BindingFlags.Instance | BindingFlags.Public);

            Assert.That(vfxIds.FieldType, Is.EqualTo(typeof(TargetedVfxIds)));
            Assert.That(vfxSize.FieldType, Is.EqualTo(typeof(TargetedVfxSizeComponent)));
            Assert.That(idFields, Has.Length.EqualTo(5));
            Assert.That(
                Array.ConvertAll(idFields, field => field.Name),
                Is.EqualTo(new[] { "SpawnId", "HitId", "ExpireId", "LinkId", "ArmingId" }));
            Assert.That(Array.TrueForAll(idFields, field => field.FieldType == typeof(int)), Is.True);
            Assert.That(
                Array.ConvertAll(sizeFields, field => field.Name),
                Is.EqualTo(new[] { "EffectSize", "LinkWidth" }));
            Assert.That(Array.TrueForAll(sizeFields, field => field.FieldType == typeof(float)), Is.True);
        }

        [Test]
        public void TargetedSpawnCommand_UsesNestedResolveAndDeterministicInstanceFrame()
        {
            FieldInfo[] commandFields = typeof(TargetedSpawnCommand).GetFields(BindingFlags.Instance | BindingFlags.Public);
            TargetedSpawnCommand command = new()
            {
                JitterSeed = 17u,
                DeterministicIdTickIndex = 3,
                Resolve = new TargetedResolveConfig
                {
                    AcquireRadius = 10f,
                    ChainRadius = 8f,
                    ChainDamageFalloff = 0.25f,
                    ChainDelaySeconds = 0.5f,
                    MaxTargets = 4
                }
            };

            Assert.That(command.JitterSeed, Is.EqualTo(17u));
            Assert.That(command.DeterministicIdTickIndex, Is.EqualTo(3));
            Assert.That(command.Resolve.AcquireRadius, Is.EqualTo(10f));
            Assert.That(command.Resolve.ChainRadius, Is.EqualTo(8f));
            Assert.That(command.Resolve.ChainDamageFalloff, Is.EqualTo(0.25f));
            Assert.That(command.Resolve.ChainDelaySeconds, Is.EqualTo(0.5f));
            Assert.That(command.Resolve.MaxTargets, Is.EqualTo(4));
            Assert.That(
                Array.ConvertAll(commandFields, field => field.Name),
                Is.EqualTo(new[]
                {
                    "Faction", "TargetedId", "TypeId", "RenderTypeId", "InstanceIndex",
                    "JitterSeed", "DeterministicIdTickIndex", "Origin", "AcquireAnchor", "Count",
                    "LifetimeSeconds", "TickIntervalSeconds", "ArmSeconds", "HitPayload", "Resolve",
                    "VfxIds", "VfxSize", "Render", "Authoring", "OnHitSpawn", "HasTimedSpawner",
                    "TimedSpawn"
                }));
            Assert.That(typeof(TargetedSpawnCommand).GetField("AcquireRadius"), Is.Null);
            Assert.That(typeof(TargetedSpawnCommand).GetField("AimDirection"), Is.Null);
            Assert.That(typeof(TargetedSpawnCommand).GetField("ContactGateSeedTargetId"), Is.Null);
        }

        [Test]
        public void CombatScopeOwner_AcquireRelease_CreatesAndDisposesTargetedTemplateMap()
        {
            using World world = new("TargetedScopeRegistryTest");
            EntityManager entityManager = world.EntityManager;
            Type scopeOwnerType = typeof(CombatScope).Assembly.GetType(
                "PlayGround.System.Combat.Core.CombatScopeOwner");
            MethodInfo acquire = scopeOwnerType.GetMethod("Acquire", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo release = scopeOwnerType.GetMethod("Release", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Entity scope = (Entity)acquire.Invoke(null, new object[] { entityManager });
            TargetedSpawnTemplate registry = entityManager.GetComponentData<TargetedSpawnTemplate>(scope);

            Assert.That(entityManager.HasBuffer<TargetedSpawnEvent>(scope), Is.True);
            Assert.That(entityManager.HasBuffer<LingeringTargetedSpawnEvent>(scope), Is.True);
            Assert.That(registry.Map.IsCreated, Is.True);

            release.Invoke(null, new object[] { entityManager, scope });

            Assert.That(registry.Map.IsCreated, Is.False);
            Assert.That(entityManager.Exists(scope), Is.False);
        }
    }
}

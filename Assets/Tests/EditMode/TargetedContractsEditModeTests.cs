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
        public void IntervalChildKind_HasOneTargetedValue()
        {
            // The targeted domain has a single variant: a chain lives exactly as long as its
            // walk, so there is nothing for a second child kind to discriminate.
            string[] names = Enum.GetNames(typeof(IntervalChildKind));

            Assert.That(names, Is.EqualTo(new[]
            {
                "Projectile", "ImpactAoe", "LingeringAoe", "Targeted"
            }));
        }

        [Test]
        public void TimingFor_UsesComputedLifetimeAndNeverTicks()
        {
            TargetedSpawnCommand command = new() { LifetimeSeconds = 2.5f };

            VfxTimingData timing = TargetedVfxUtility.TimingFor(command);

            Assert.That(timing.Duration, Is.EqualTo(2.5f));
            Assert.That(timing.TickInterval, Is.Zero);
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
        public void TargetedResolveConfig_CarriesOneDistanceAndOneDelay()
        {
            // One distance governs every hop including link 0, and one delay paces the walk.
            // No acquire radius, no tick interval: those were the confusing duplicates.
            FieldInfo[] resolveFields = typeof(TargetedResolveConfig).GetFields(BindingFlags.Instance | BindingFlags.Public);

            Assert.That(
                Array.ConvertAll(resolveFields, field => field.Name),
                Is.EqualTo(new[] { "ChainDistance", "ChainDamageFalloff", "ChainDelay", "ChainCount" }));
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
                    ChainDistance = 8f,
                    ChainDamageFalloff = 0.25f,
                    ChainDelay = 0.5f,
                    ChainCount = 4
                }
            };

            Assert.That(command.JitterSeed, Is.EqualTo(17u));
            Assert.That(command.DeterministicIdTickIndex, Is.EqualTo(3));
            Assert.That(command.Resolve.ChainDistance, Is.EqualTo(8f));
            Assert.That(command.Resolve.ChainDamageFalloff, Is.EqualTo(0.25f));
            Assert.That(command.Resolve.ChainDelay, Is.EqualTo(0.5f));
            Assert.That(command.Resolve.ChainCount, Is.EqualTo(4));
            Assert.That(
                Array.ConvertAll(commandFields, field => field.Name),
                Is.EqualTo(new[]
                {
                    "Faction", "TargetedId", "TypeId", "RenderTypeId", "InstanceIndex",
                    "JitterSeed", "DeterministicIdTickIndex", "Origin", "AcquireAnchor", "HasAcquiredTarget", "EchoCount",
                    "LifetimeSeconds", "ArmSeconds", "HitPayload", "Resolve",
                    "VfxIds", "VfxSize", "Render", "Authoring"
                }));
            Assert.That(typeof(TargetedSpawnCommand).GetField("TickIntervalSeconds"), Is.Null);
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
            Assert.That(registry.Map.IsCreated, Is.True);

            release.Invoke(null, new object[] { entityManager, scope });

            Assert.That(registry.Map.IsCreated, Is.False);
            Assert.That(entityManager.Exists(scope), Is.False);
        }
    }
}

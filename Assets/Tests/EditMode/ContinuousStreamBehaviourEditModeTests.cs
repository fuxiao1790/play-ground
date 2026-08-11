using System.Reflection;
using NUnit.Framework;
using PlayGround.Spawn;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class ContinuousStreamBehaviourEditModeTests
    {
        private ContinuousStreamBehaviour behaviour;

        [SetUp]
        public void SetUp()
        {
            behaviour = ScriptableObject.CreateInstance<ContinuousStreamBehaviour>();
            SetField(behaviour, "spawnsPerSecond", 2f);
            SetField(behaviour, "maxSpawnsPerTick", 8);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(behaviour);
        }

        [Test]
        public void SaturatedAccumulator_DoesNotSpawnAtZeroDelta_AndDrainsWithPositiveDelta()
        {
            SpawnBehaviourRuntime runtime = behaviour.CreateRuntime();
            var sink = new TestSpawnSink { CanSpawn = false };

            runtime.Tick(sink, 1f);
            sink.CanSpawn = true;

            runtime.Tick(sink, 0f);

            Assert.That(sink.SpawnCount, Is.Zero);

            runtime.Tick(sink, 0.5f);

            Assert.That(sink.SpawnCount, Is.EqualTo(2));
        }

        [Test]
        public void PositiveDelta_SpawnsAtConfiguredRate()
        {
            SpawnBehaviourRuntime runtime = behaviour.CreateRuntime();
            var sink = new TestSpawnSink { CanSpawn = true };

            runtime.Tick(sink, 0.5f);

            Assert.That(sink.SpawnCount, Is.EqualTo(1));
        }

        private static void SetField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private sealed class TestSpawnSink : ISpawnSink
        {
            public int ActiveCount => SpawnCount;
            public int Cap => int.MaxValue;
            public bool CanSpawn { get; set; }
            public int SpawnCount { get; private set; }

            public void Spawn()
            {
                SpawnCount++;
            }
        }
    }
}

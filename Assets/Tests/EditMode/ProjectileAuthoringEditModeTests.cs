using System.Collections.Generic;
using NUnit.Framework;
using PlayGround.Attack;
using PlayGround.Common;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class ProjectileAuthoringEditModeTests
    {
        [Test]
        public void VolleyBuilderCreatesExpectedCountAndDirections()
        {
            var commands = new List<ProjectileSpawnCommand>();

            int count = ProjectileVolleyBuilder.Build(
                commands,
                Vector2.zero,
                Vector2.right,
                3,
                20f,
                0f,
                10f,
                1f,
                0.2f,
                new DamageSnapshot(1f),
                CombatShapeType.Circle);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(commands[0].Direction.y, Is.LessThan(0f));
            Assert.That(commands[1].Direction, Is.EqualTo(Vector2.right).Using(Vector2Comparer.Instance));
            Assert.That(commands[2].Direction.y, Is.GreaterThan(0f));
        }

        [Test]
        public void SpawnCommandNormalizesDirectionAndPreservesDamage()
        {
            var command = new ProjectileSpawnCommand(
                Vector2.zero,
                new Vector2(10f, 0f),
                3f,
                1f,
                0.25f,
                new DamageSnapshot(7f),
                CombatShapeType.Circle);

            Assert.That(command.Direction, Is.EqualTo(Vector2.right).Using(Vector2Comparer.Instance));
            Assert.That(command.Damage.Amount, Is.EqualTo(7f));
        }

        [Test]
        public void SideSprayPatternAlternatesLeftAndRight()
        {
            var pattern = ScriptableObject.CreateInstance<ProjectileSideSpraySpawnPattern>();
            var requests = new List<ProjectileVolleyBuilder.SpawnRequest>();

            pattern.Build(requests, Vector2.zero, Vector2.right, 2, 5f, 1);

            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[0].Velocity.y, Is.LessThan(0f));
            Assert.That(requests[1].Velocity.y, Is.GreaterThan(0f));
            Object.DestroyImmediate(pattern);
        }

        [Test]
        public void SpawnCommandPreservesDirectDamageToggle()
        {
            var command = new ProjectileSpawnCommand(
                Vector2.zero,
                Vector2.right,
                3f,
                1f,
                0.25f,
                new Vector2(0.25f, 0.25f),
                0f,
                new DamageSnapshot(7f),
                CombatShapeType.Circle,
                directDamageEnabled: false);

            Assert.That(command.DirectDamageEnabled, Is.False);
        }

        [Test]
        public void BasicAttackPrefabBakesSpriteAndHurtboxShape()
        {
            GameObject attackObject = new("BasicAttackPrefabTest");
            attackObject.SetActive(false);
            GameObject visualObject = new("Visual");
            visualObject.transform.SetParent(attackObject.transform, false);
            visualObject.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            visualObject.transform.localScale = Vector3.one * 2f;
            SpriteRenderer renderer = visualObject.AddComponent<SpriteRenderer>();
            renderer.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 8f, 8f), Vector2.one * 0.5f);
            GameObject hurtboxObject = new("Hurtbox");
            hurtboxObject.transform.SetParent(attackObject.transform, false);
            BoxCollider2D hurtbox = hurtboxObject.AddComponent<BoxCollider2D>();
            hurtbox.size = new Vector2(2f, 4f);
            BasicAttackPrefab basicPrefab = attackObject.AddComponent<BasicAttackPrefab>();
            basicPrefab.Configure(renderer, hurtbox);
            attackObject.SetActive(true);

            Assert.That(basicPrefab.Sprite, Is.EqualTo(renderer.sprite));
            Assert.That(basicPrefab.VisualScale, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(Mathf.DeltaAngle(basicPrefab.VisualRotationDegrees, 270f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(basicPrefab.ShapeType, Is.EqualTo(CombatShapeType.Rectangle));
            Assert.That(basicPrefab.HalfExtents, Is.EqualTo(new Vector2(1f, 2f)).Using(Vector2Comparer.Instance));
            Object.DestroyImmediate(attackObject);
        }

        private sealed class Vector2Comparer : IEqualityComparer<Vector2>
        {
            public static readonly Vector2Comparer Instance = new();

            public bool Equals(Vector2 x, Vector2 y)
            {
                return Vector2.Distance(x, y) < 0.0001f;
            }

            public int GetHashCode(Vector2 obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using PlayGround.Skills;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class ProjectileAuthoringEditModeTests
    {
        [Test]
        public void SpawnRequestNormalizesDirectionAndPreservesDamage()
        {
            var command = new ProjectileSpawnRequest(
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
        public void SpawnRequestPreservesDirectDamageToggle()
        {
            var command = new ProjectileSpawnRequest(
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

        [Test]
        public void SpawnTemplateEvents_AreBlittableAndContentHashed()
        {
            Assert.That(UnsafeUtility.IsBlittable<ProjectileSpawnEvent>(), Is.True);
            Assert.That(UnsafeUtility.IsBlittable<AoeSpawnEvent>(), Is.True);
            Assert.That(UnsafeUtility.SizeOf<AoeSpawnCommand>(), Is.LessThan(4096));

            var projA = new ProjectileSpawnCommand
            {
                TypeId = 7,
                Count = 2,
                Speed = 12f,
                Lifetime = 0.75f,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    DamageAmount = 4f,
                    DirectDamageEnabled = true
                })
            };
            ProjectileSpawnCommand projB = projA;
            ProjectileSpawnCommand projC = projA;
            projC.Count = 3;

            Assert.That(SpawnTemplateHash.Of(in projB), Is.EqualTo(SpawnTemplateHash.Of(in projA)));
            Assert.That(SpawnTemplateHash.Of(in projC), Is.Not.EqualTo(SpawnTemplateHash.Of(in projA)));

            var aoeA = new AoeSpawnCommand
            {
                TypeId = 9,
                Lifetime = 1.5f,
                RepeatHitCooldownSeconds = 0.2f,
                ShapeType = CombatShapeType.Circle,
                EchoCount = 1,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = 5f,
                    CritChance = 0.1f,
                    CritMultiplier = 2f,
                    DirectDamageEnabled = true
                }
            };
            AoeSpawnCommand aoeB = aoeA;
            AoeSpawnCommand aoeC = aoeA;
            aoeC.EchoCount = 2;

            Assert.That(SpawnTemplateHash.Of(in aoeB), Is.EqualTo(SpawnTemplateHash.Of(in aoeA)));
            Assert.That(SpawnTemplateHash.Of(in aoeC), Is.Not.EqualTo(SpawnTemplateHash.Of(in aoeA)));
        }

        [Test]
        public void CombatRenderComponent_PacksRenderIdAndAlignFlag()
        {
            var component = new CombatRenderComponent();

            component.RenderTypeId = 42;
            Assert.That(component.RenderMeta, Is.EqualTo(42));
            Assert.That(component.AlignToVelocity, Is.EqualTo(0));
            Assert.That(component.RenderTypeId, Is.EqualTo(42));
            Assert.That(UnsafeUtility.SizeOf<CombatRenderComponent>(), Is.EqualTo(32));
            Assert.That(UnsafeUtility.SizeOf<CombatRenderAuthoring>(), Is.EqualTo(16));

            component.AlignToVelocity = 1;
            Assert.That(component.RenderMeta, Is.EqualTo(unchecked((int)0x8000002A)));
            Assert.That(component.RenderTypeId, Is.EqualTo(42));
            Assert.That(component.AlignToVelocity, Is.EqualTo(1));

            component.RenderTypeId = 17;
            Assert.That(component.RenderMeta, Is.EqualTo(unchecked((int)0x80000011)));
            Assert.That(component.RenderTypeId, Is.EqualTo(17));
            Assert.That(component.AlignToVelocity, Is.EqualTo(1));

            component.AlignToVelocity = 0;
            Assert.That(component.RenderMeta, Is.EqualTo(17));
            Assert.That(component.RenderTypeId, Is.EqualTo(17));
            Assert.That(component.AlignToVelocity, Is.EqualTo(0));
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

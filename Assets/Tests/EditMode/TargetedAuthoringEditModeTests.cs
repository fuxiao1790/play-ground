using NUnit.Framework;
using PlayGround.Skills;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class TargetedAuthoringEditModeTests
    {
        [Test]
        public void VfxOnlyPrefab_ValidatesAndHurtboxIsRejected()
        {
            var root = new GameObject("Targeted");
            var prefab = root.AddComponent<TargetedPrefab>();
            Assert.That(prefab.IsValidTemplate(out _), Is.True);

            new GameObject("Hurtbox").transform.SetParent(root.transform);
            Assert.That(prefab.IsValidTemplate(out string reason), Is.False);
            Assert.That(reason, Does.Contain("Hurtbox"));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void VisualSpriteRenderer_RequiresVisualChildAndInstancedMaterial()
        {
            Texture2D texture = new Texture2D(2, 2);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            Shader shader = Shader.Find("Sprites/Default");
            Assert.That(shader, Is.Not.Null);

            GameObject validRoot = new GameObject("TargetedValid");
            GameObject wrongRoot = new GameObject("TargetedWrong");
            GameObject nonInstancedRoot = new GameObject("TargetedNonInstanced");
            Material validMaterial = new Material(shader) { mainTexture = texture, enableInstancing = true };
            Material nonInstancedMaterial = new Material(shader) { mainTexture = texture, enableInstancing = false };

            try
            {
                TargetedPrefab validPrefab = validRoot.AddComponent<TargetedPrefab>();
                SpriteRenderer validRenderer = CreateRenderer(validRoot, "Visual", sprite, validMaterial);
                validPrefab.Configure(validRenderer);
                Assert.That(validPrefab.IsValidTemplate(out _), Is.True);

                TargetedPrefab wrongPrefab = wrongRoot.AddComponent<TargetedPrefab>();
                SpriteRenderer wrongRenderer = CreateRenderer(wrongRoot, "NotVisual", sprite, validMaterial);
                wrongPrefab.Configure(wrongRenderer);
                Assert.That(wrongPrefab.IsValidTemplate(out string wrongReason), Is.False);
                Assert.That(wrongReason, Does.Contain("Visual"));

                TargetedPrefab nonInstancedPrefab = nonInstancedRoot.AddComponent<TargetedPrefab>();
                SpriteRenderer nonInstancedRenderer = CreateRenderer(nonInstancedRoot, "Visual", sprite, nonInstancedMaterial);
                nonInstancedPrefab.Configure(nonInstancedRenderer);
                Assert.That(nonInstancedPrefab.IsValidTemplate(out string nonInstancedReason), Is.False);
                Assert.That(nonInstancedReason, Does.Contain("GPU instancing"));
            }
            finally
            {
                Object.DestroyImmediate(validRoot);
                Object.DestroyImmediate(wrongRoot);
                Object.DestroyImmediate(nonInstancedRoot);
                Object.DestroyImmediate(validMaterial);
                Object.DestroyImmediate(nonInstancedMaterial);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TargetedDefinition_DeepCopiesAndSkillUsesTargetedTag()
        {
            var definition = new TargetedDefinition { damage = 3f, chainDelay = 0.25f };
            var copy = (TargetedDefinition)definition.DeepCopy();
            copy.damage = 9f;
            copy.chainDelay = 0.5f;
            Assert.That(definition.damage, Is.EqualTo(3f));
            Assert.That(definition.chainDelay, Is.EqualTo(0.25f));

            TargetedSkill skill = ScriptableObject.CreateInstance<TargetedSkill>();
            Assert.That(skill.Tags, Is.EqualTo(SkillDefinitionTags.Targeted));
            Object.DestroyImmediate(skill);
        }

        [Test]
        public void TargetedDefinition_ExposesNoLifetimeOrTickInterval()
        {
            // The walk owns the instance's life. Re-adding either field would put back the
            // three-timer confusion this authoring surface was collapsed to remove.
            global::System.Type type = typeof(TargetedDefinition);

            Assert.That(type.GetField("lifetimeSeconds"), Is.Null);
            Assert.That(type.GetField("tickIntervalSeconds"), Is.Null);
            Assert.That(type.GetField("acquireRadius"), Is.Null);
            Assert.That(type.GetField("chainDistance"), Is.Not.Null);
            Assert.That(type.GetField("chainCount"), Is.Not.Null);
            Assert.That(type.GetField("chainDelay"), Is.Not.Null);
            Assert.That(type.GetField("echoCount"), Is.Not.Null);
        }

        private static SpriteRenderer CreateRenderer(GameObject root, string childName, Sprite sprite, Material material)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(root.transform);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            return renderer;
        }
    }
}

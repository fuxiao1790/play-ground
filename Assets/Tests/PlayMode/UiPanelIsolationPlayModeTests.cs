using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Mob;
using PlayGround.System.Combat.Core;
using PlayGround.Ui;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PlayGround.Tests.PlayMode
{
    public sealed class UiPanelIsolationPlayModeTests
    {
        private const string BenchmarkScenePath = "Assets/Scenes/BenchmarkLarge.unity";

        private Scene loadedScene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync(BenchmarkScenePath, LoadSceneMode.Additive);
            loadedScene = SceneManager.GetSceneByPath(BenchmarkScenePath);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (loadedScene.IsValid())
                yield return SceneManager.UnloadSceneAsync(loadedScene);
        }

        [UnityTest]
        public IEnumerator HudAndWorldLabelsUseDistinctRuntimePanels()
        {
            UIDocument hudDocument = FindComponentInScene<UIDocument>("GameUI");
            UIDocument worldLabelsDocument = FindComponentInScene<UIDocument>("WorldLabelsUI");
            Assert.That(hudDocument, Is.Not.Null, "Missing UIDocument on 'GameUI'.");
            Assert.That(worldLabelsDocument, Is.Not.Null,
                "Missing UIDocument on 'WorldLabelsUI'. Task 002 (wire WorldLabelsUI through the Unity Editor) must run first.");
            yield return null;

            Assert.That(hudDocument.rootVisualElement.panel, Is.Not.Null);
            Assert.That(worldLabelsDocument.rootVisualElement.panel, Is.Not.Null);
            Assert.That(worldLabelsDocument.rootVisualElement.panel, Is.Not.SameAs(hudDocument.rootVisualElement.panel));
        }

        [UnityTest]
        public IEnumerator WorldLabelsAllowPointerInputToReachGameplaySurface()
        {
            UIDocument hudDocument = FindComponentInScene<UIDocument>("GameUI");
            UIDocument worldLabelsDocument = FindComponentInScene<UIDocument>("WorldLabelsUI");
            Assert.That(hudDocument, Is.Not.Null, "Missing UIDocument on 'GameUI'.");
            Assert.That(worldLabelsDocument, Is.Not.Null,
                "Missing UIDocument on 'WorldLabelsUI'. Task 002 must run first.");
            yield return null;

            VisualElement clickToFireLayer = hudDocument.rootVisualElement.Q<VisualElement>("click-to-fire-layer");
            Assert.That(clickToFireLayer, Is.Not.Null);
            Assert.That(clickToFireLayer.pickingMode, Is.EqualTo(PickingMode.Position));

            VisualElement labelsLayer = worldLabelsDocument.rootVisualElement.Q<VisualElement>("labels-layer");
            Assert.That(labelsLayer, Is.Not.Null);
            AssertPickingIgnoreRecursive(labelsLayer);
        }

        [UnityTest]
        public IEnumerator PauseBackgroundRemainsAboveWorldLabels()
        {
            UIDocument hudDocument = FindComponentInScene<UIDocument>("GameUI");
            UIDocument worldLabelsDocument = FindComponentInScene<UIDocument>("WorldLabelsUI");
            Assert.That(hudDocument, Is.Not.Null, "Missing UIDocument on 'GameUI'.");
            Assert.That(worldLabelsDocument, Is.Not.Null,
                "Missing UIDocument on 'WorldLabelsUI'. Task 002 must run first.");
            yield return null;

            Assert.That(hudDocument.panelSettings, Is.Not.Null);
            Assert.That(worldLabelsDocument.panelSettings, Is.Not.Null);
            Assert.That(
                hudDocument.panelSettings.sortingOrder,
                Is.GreaterThan(worldLabelsDocument.panelSettings.sortingOrder),
                "HudPanel must sort above WorldLabelsPanel so pause background (owned by HudPanel) dims/shields world labels.");

            VisualElement pauseBackground = hudDocument.rootVisualElement.Q<VisualElement>("pause-background");
            Assert.That(pauseBackground, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator RegisteredMobCreatesBarOnlyInWorldLabelsPanel()
        {
            UIDocument hudDocument = FindComponentInScene<UIDocument>("GameUI");
            GameObject worldLabelsObject = FindRootObject(loadedScene, "WorldLabelsUI");
            Assert.That(hudDocument, Is.Not.Null, "Missing UIDocument on 'GameUI'.");
            Assert.That(worldLabelsObject, Is.Not.Null, "Missing 'WorldLabelsUI'. Task 002 must run first.");

            UIDocument worldLabelsDocument = worldLabelsObject.GetComponent<UIDocument>();
            MobResourceBarUi barUi = worldLabelsObject.GetComponent<MobResourceBarUi>();
            Assert.That(worldLabelsDocument, Is.Not.Null, "'WorldLabelsUI' is missing its UIDocument.");
            Assert.That(barUi, Is.Not.Null, "'WorldLabelsUI' is missing MobResourceBarUi. Task 002 must move it there.");
            yield return null;

            CombatRoot combatRoot = GetPrivateField<CombatRoot>(barUi, "combatRoot");
            Assert.That(combatRoot, Is.Not.Null, "MobResourceBarUi.combatRoot must be assigned.");

            VisualElement labelsLayer = worldLabelsDocument.rootVisualElement.Q<VisualElement>("labels-layer");
            Assert.That(labelsLayer, Is.Not.Null);
            int labelsCountBefore = CountDescendants(labelsLayer);
            int hudCountBefore = CountDescendants(hudDocument.rootVisualElement);

            GameObject mobObject = CreateTestMob();
            mobObject.SetActive(true);
            mobObject.GetComponent<MobRoot>().Register(combatRoot.TargetRegistry);

            yield return null;

            Assert.That(CountDescendants(labelsLayer), Is.EqualTo(labelsCountBefore + 1),
                "Registering a mob must add exactly one resource-bar marker under WorldLabelsPanel's '#labels-layer'.");
            Assert.That(CountDescendants(hudDocument.rootVisualElement), Is.EqualTo(hudCountBefore),
                "Registering a mob must not add any element to HudPanel.");

            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileAndAoePopulationDoesNotAddHudVisualElements()
        {
            UIDocument hudDocument = FindComponentInScene<UIDocument>("GameUI");
            Assert.That(hudDocument, Is.Not.Null, "Missing UIDocument on 'GameUI'.");
            yield return null;

            int before = CountDescendants(hudDocument.rootVisualElement);

            // Structural claim, not a timing claim: no production HUD code path adds an
            // element per combat entity, so HudPanel's element count must stay flat across
            // frames regardless of whatever ambient projectile/AOE activity this scene runs.
            for (int i = 0; i < 30; i++)
                yield return null;

            int after = CountDescendants(hudDocument.rootVisualElement);
            Assert.That(after, Is.EqualTo(before),
                "HudPanel element count must not change as combat entities spawn/despawn during normal play.");
        }

        private static GameObject CreateTestMob()
        {
            GameObject mobObject = new("UiIsolationTestMob");
            mobObject.SetActive(false);
            Rigidbody2D body = mobObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            CircleCollider2D hurtbox = mobObject.AddComponent<CircleCollider2D>();
            hurtbox.isTrigger = true;
            SpriteRenderer renderer = mobObject.AddComponent<SpriteRenderer>();
            MobRoot mob = mobObject.AddComponent<MobRoot>();
            mob.Configure(body, hurtbox, hurtbox, renderer, null);
            mob.ConfigureAuthoring(10f, 0f, 0.5f);
            return mobObject;
        }

        private T FindComponentInScene<T>(string rootName) where T : Component
        {
            GameObject root = FindRootObject(loadedScene, rootName);
            return root == null ? null : root.GetComponent<T>();
        }

        private static GameObject FindRootObject(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root;
            }

            return null;
        }

        private static T GetPrivateField<T>(object target, string fieldName) where T : class
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}.");
            return field.GetValue(target) as T;
        }

        private static int CountDescendants(VisualElement element)
        {
            int count = 0;
            for (int i = 0; i < element.hierarchy.childCount; i++)
            {
                count++;
                count += CountDescendants(element.hierarchy[i]);
            }

            return count;
        }

        private static void AssertPickingIgnoreRecursive(VisualElement element)
        {
            Assert.That(element.pickingMode, Is.EqualTo(PickingMode.Ignore), $"'{element.name}' must be PickingMode.Ignore.");
            for (int i = 0; i < element.hierarchy.childCount; i++)
                AssertPickingIgnoreRecursive(element.hierarchy[i]);
        }
    }
}

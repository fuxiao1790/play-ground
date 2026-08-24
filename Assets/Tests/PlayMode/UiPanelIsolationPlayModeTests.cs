using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Game;
using PlayGround.Mob;
using PlayGround.Skills;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Targets;
using PlayGround.Ui;
using Unity.Entities;
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
        public IEnumerator PauseMenuIsCentered()
        {
            UIDocument hudDocument = FindComponentInScene<UIDocument>("GameUI");
            PauseMenuUi pauseMenuUi = FindComponentInScene<PauseMenuUi>("GameUI");
            Assert.That(hudDocument, Is.Not.Null, "Missing UIDocument on 'GameUI'.");
            Assert.That(pauseMenuUi, Is.Not.Null, "Missing PauseMenuUi on 'GameUI'.");

            PauseController pauseController = GetPrivateField<PauseController>(pauseMenuUi, "pauseController");
            pauseController.SetPaused(true);
            yield return null;

            VisualElement pauseMenu = hudDocument.rootVisualElement.Q<VisualElement>("pause-menu");
            Assert.That(pauseMenu, Is.Not.Null);
            Assert.That(pauseMenu.resolvedStyle.alignItems, Is.EqualTo(Align.Center));
            Assert.That(pauseMenu.resolvedStyle.justifyContent, Is.EqualTo(Justify.Center));

            pauseController.SetPaused(false);
        }

        [UnityTest]
        public IEnumerator OptionsPopupConsumesCancelBeforePauseToggle()
        {
            UIDocument hudDocument = FindComponentInScene<UIDocument>("GameUI");
            PauseMenuUi pauseMenuUi = FindComponentInScene<PauseMenuUi>("GameUI");
            Assert.That(hudDocument, Is.Not.Null, "Missing UIDocument on 'GameUI'.");
            Assert.That(pauseMenuUi, Is.Not.Null, "Missing PauseMenuUi on 'GameUI'.");

            PauseController pauseController = GetPrivateField<PauseController>(pauseMenuUi, "pauseController");
            pauseController.SetPaused(true);
            yield return null;

            VisualElement optionsPopup = hudDocument.rootVisualElement.Q<VisualElement>("options-popup");
            Assert.That(optionsPopup, Is.Not.Null);
            Button optionsButton = hudDocument.rootVisualElement.Q<Button>("options");
            Assert.That(optionsButton, Is.Not.Null);
            Submit(optionsButton);
            Assert.That(optionsPopup.ClassListContains("options-popup--hidden"), Is.False);

            pauseController.HandleCancel();

            Assert.That(pauseController.IsPaused, Is.True,
                "Cancel must close active options popup without resuming gameplay.");
            Assert.That(optionsPopup.ClassListContains("options-popup--hidden"), Is.True);

            pauseController.HandleCancel();
            Assert.That(pauseController.IsPaused, Is.False,
                "Cancel must resume gameplay after options popup has closed.");
        }

        [UnityTest]
        public IEnumerator SkillPickerConsumesCancelBeforePauseToggle()
        {
            UIDocument hudDocument = FindComponentInScene<UIDocument>("GameUI");
            PauseMenuUi pauseMenuUi = FindComponentInScene<PauseMenuUi>("GameUI");
            SkillLoadoutUi skillLoadoutUi = FindComponentInScene<SkillLoadoutUi>("GameUI");
            Assert.That(hudDocument, Is.Not.Null, "Missing UIDocument on 'GameUI'.");
            Assert.That(pauseMenuUi, Is.Not.Null, "Missing PauseMenuUi on 'GameUI'.");
            Assert.That(skillLoadoutUi, Is.Not.Null, "Missing SkillLoadoutUi on 'GameUI'.");

            PauseController pauseController = GetPrivateField<PauseController>(pauseMenuUi, "pauseController");
            Button skillButton = hudDocument.rootVisualElement.Q<Button>(className: "skill-button");
            Assert.That(skillButton, Is.Not.Null);
            Submit(skillButton);
            yield return null;

            Assert.That(hudDocument.rootVisualElement.Q<VisualElement>("picker"), Is.Not.Null,
                "Submitting a skill button must open skill picker.");

            pauseController.HandleCancel();

            Assert.That(hudDocument.rootVisualElement.Q<VisualElement>("picker"), Is.Null,
                "Cancel must close active skill picker.");
            Assert.That(pauseController.IsPaused, Is.False,
                "Cancel consumed by skill picker must not pause gameplay.");
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
            int markerCountBefore = labelsLayer.Query<VisualElement>("marker").ToList().Count;
            int hudCountBefore = CountDescendants(hudDocument.rootVisualElement);

            GameObject mobObject = CreateTestMob();
            try
            {
                mobObject.SetActive(true);
                mobObject.GetComponent<MobRoot>().Register(combatRoot.TargetRegistry);

                yield return null;

                Assert.That(labelsLayer.Query<VisualElement>("marker").ToList().Count, Is.EqualTo(markerCountBefore + 1),
                    "Registering a mob must add exactly one resource-bar marker under WorldLabelsPanel's '#labels-layer'.");
                Assert.That(CountDescendants(hudDocument.rootVisualElement), Is.EqualTo(hudCountBefore),
                    "Registering a mob must not add any element to HudPanel.");
            }
            finally
            {
                Object.Destroy(mobObject);
            }
        }

        [UnityTest]
        public IEnumerator RegisteredMobBarUsesDynamicUiToolkitTransforms()
        {
            GameObject worldLabelsObject = FindRootObject(loadedScene, "WorldLabelsUI");
            Assert.That(worldLabelsObject, Is.Not.Null, "Missing 'WorldLabelsUI'. Task 002 must run first.");

            UIDocument worldLabelsDocument = worldLabelsObject.GetComponent<UIDocument>();
            MobResourceBarUi barUi = worldLabelsObject.GetComponent<MobResourceBarUi>();
            Assert.That(worldLabelsDocument, Is.Not.Null, "'WorldLabelsUI' is missing its UIDocument.");
            Assert.That(barUi, Is.Not.Null, "'WorldLabelsUI' is missing MobResourceBarUi.");
            yield return null;

            CombatRoot combatRoot = GetPrivateField<CombatRoot>(barUi, "combatRoot");
            Camera gameplayCamera = GetPrivateField<Camera>(barUi, "gameplayCamera");
            Assert.That(combatRoot, Is.Not.Null, "MobResourceBarUi.combatRoot must be assigned.");
            Assert.That(gameplayCamera, Is.Not.Null, "MobResourceBarUi.gameplayCamera must be assigned.");

            VisualElement labelsLayer = worldLabelsDocument.rootVisualElement.Q<VisualElement>("labels-layer");
            Assert.That(labelsLayer, Is.Not.Null);
            List<VisualElement> markersBefore = labelsLayer.Query<VisualElement>("marker").ToList();

            GameObject mobObject = CreateTestMob();
            try
            {
                mobObject.transform.position = gameplayCamera.transform.position + gameplayCamera.transform.forward * 5f;
                mobObject.SetActive(true);
                MobRoot mob = mobObject.GetComponent<MobRoot>();
                mob.Register(combatRoot.TargetRegistry);

                VisualElement marker = null;
                for (int frame = 0; frame < 10 && marker == null; frame++)
                {
                    yield return null;
                    List<VisualElement> markersAfter = labelsLayer.Query<VisualElement>("marker").ToList();
                    for (int i = 0; i < markersAfter.Count; i++)
                    {
                        if (!markersBefore.Contains(markersAfter[i]))
                        {
                            marker = markersAfter[i];
                            break;
                        }
                    }
                }

                Assert.That(marker, Is.Not.Null,
                    "Registering a mob did not add a new '#marker' element under '#labels-layer' within 10 frames (timeout).");

                VisualElement fill = marker.Q<VisualElement>("fill");
                Assert.That(fill, Is.Not.Null, "Marker must contain a '#fill' element.");

                Assert.That(marker.usageHints.HasFlag(UsageHints.DynamicTransform), Is.True,
                    "Marker must use UsageHints.DynamicTransform for GPU-backed position updates.");
                Assert.That(fill.usageHints.HasFlag(UsageHints.DynamicTransform), Is.True,
                    "Fill must use UsageHints.DynamicTransform for GPU-backed scale updates.");
                AssertPickingIgnoreRecursive(marker);

                Translate translateBefore = marker.style.translate.value;
                mobObject.transform.position += new Vector3(1.5f, 0.5f, 0f);

                bool moved = false;
                Translate translateAfter = translateBefore;
                for (int frame = 0; frame < 10 && !moved; frame++)
                {
                    yield return null;
                    translateAfter = marker.style.translate.value;
                    moved = !Mathf.Approximately(translateAfter.x.value, translateBefore.x.value)
                        || !Mathf.Approximately(translateAfter.y.value, translateBefore.y.value);
                }

                Assert.That(moved, Is.True,
                    $"Marker style.translate did not change after moving the mob's world position within 10 frames (timeout). " +
                    $"Before: ({translateBefore.x.value}, {translateBefore.y.value}), after: ({translateAfter.x.value}, {translateAfter.y.value}).");

                Entity proxy = Entity.Null;
                for (int frame = 0; frame < 30 && proxy == Entity.Null; frame++)
                {
                    yield return null;
                    proxy = mob.CombatTargetProxy;
                }

                Assert.That(proxy, Is.Not.EqualTo(Entity.Null),
                    "Mob's CombatTargetProxy entity was not created within 30 frames (timeout).");

                const float targetRatio = 0.5f;
                bool setHealthQueued = CombatTargetProxy.SetHealth(mob, mob.MaxHealth * targetRatio);
                Assert.That(setHealthQueued, Is.True, "CombatTargetProxy.SetHealth failed to queue a health update.");

                bool healthPropagated = false;
                Vector3 fillScale = fill.style.scale.value.value;
                for (int frame = 0; frame < 30 && !healthPropagated; frame++)
                {
                    yield return null;
                    fillScale = fill.style.scale.value.value;
                    healthPropagated = Mathf.Approximately(fillScale.x, targetRatio);
                }

                Assert.That(healthPropagated, Is.True,
                    $"Fill X scale did not reach expected ratio {targetRatio} within 30 frames (timeout). " +
                    $"Last fill scale: {fillScale}, CurrentHealth={mob.CurrentHealth}, MaxHealth={mob.MaxHealth}.");
                Assert.That(fillScale.y, Is.EqualTo(1f).Within(0.001f),
                    "Fill Y scale must remain 1 while only the health ratio changes.");
            }
            finally
            {
                Object.Destroy(mobObject);
            }
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

        private static void Submit(Button button)
        {
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
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

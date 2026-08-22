using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PlayGround.Tests.EditMode
{
    public sealed class UiPanelIsolationEditModeTests
    {
        private const string HudUxmlPath = "Assets/Scripts/Ui/Hud/SkillLoadoutUi.uxml";
        private const string WorldLabelsUxmlPath = "Assets/Scripts/Ui/WorldLabels/WorldLabelsUi.uxml";
        private const string MobResourceBarUxmlPath = "Assets/Scripts/Ui/WorldLabels/MobResourceBar.uxml";
        private const string HudPanelAssetPath = "Assets/UI/HudPanel.asset";
        private const string WorldLabelsPanelAssetPath = "Assets/UI/WorldLabelsPanel.asset";
        private const string BenchmarkScenePath = "Assets/Scenes/BenchmarkLarge.unity";

        [Test]
        public void HudRootDoesNotContainLabelsLayer()
        {
            VisualTreeAsset hudAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudUxmlPath);
            Assert.That(hudAsset, Is.Not.Null, $"Missing HUD UXML at '{HudUxmlPath}'.");

            VisualElement hudRoot = hudAsset.Instantiate();
            Assert.That(
                hudRoot.Q<VisualElement>("labels-layer"),
                Is.Null,
                "SkillLoadoutUi.uxml must not contain '#labels-layer'; world labels belong exclusively in WorldLabelsUi.uxml.");
        }

        [Test]
        public void WorldLabelsRootContainsExactlyOneLabelsLayer()
        {
            VisualTreeAsset worldLabelsAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WorldLabelsUxmlPath);
            Assert.That(worldLabelsAsset, Is.Not.Null, $"Missing world-label UXML at '{WorldLabelsUxmlPath}'.");

            VisualElement worldLabelsRoot = worldLabelsAsset.Instantiate();
            List<VisualElement> labelLayers = worldLabelsRoot.Query<VisualElement>("labels-layer").ToList();
            Assert.That(labelLayers.Count, Is.EqualTo(1));
        }

        [Test]
        public void WorldLabelsPanelIsDistinctAndSortsBelowHudPanel()
        {
            PanelSettings hudPanel = AssetDatabase.LoadAssetAtPath<PanelSettings>(HudPanelAssetPath);
            PanelSettings worldLabelsPanel = AssetDatabase.LoadAssetAtPath<PanelSettings>(WorldLabelsPanelAssetPath);

            Assert.That(hudPanel, Is.Not.Null,
                $"Missing '{HudPanelAssetPath}'. Task 002 (author HudPanel/WorldLabelsPanel through the Unity Editor) must run first.");
            Assert.That(worldLabelsPanel, Is.Not.Null,
                $"Missing '{WorldLabelsPanelAssetPath}'. Task 002 (author HudPanel/WorldLabelsPanel through the Unity Editor) must run first.");

            Assert.That(worldLabelsPanel, Is.Not.SameAs(hudPanel));
            Assert.That(worldLabelsPanel.sortingOrder, Is.LessThan(hudPanel.sortingOrder));
        }

        [Test]
        public void WorldLabelElementsArePickingTransparent()
        {
            VisualTreeAsset worldLabelsAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WorldLabelsUxmlPath);
            Assert.That(worldLabelsAsset, Is.Not.Null, $"Missing world-label UXML at '{WorldLabelsUxmlPath}'.");
            VisualElement labelsLayer = worldLabelsAsset.Instantiate().Q<VisualElement>("labels-layer");
            Assert.That(labelsLayer, Is.Not.Null);
            Assert.That(labelsLayer.pickingMode, Is.EqualTo(PickingMode.Ignore));

            VisualTreeAsset markerAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MobResourceBarUxmlPath);
            Assert.That(markerAsset, Is.Not.Null, $"Missing mob resource-bar template at '{MobResourceBarUxmlPath}'.");
            VisualElement marker = markerAsset.Instantiate().Q<VisualElement>("marker");
            Assert.That(marker, Is.Not.Null);
            AssertPickingIgnoreRecursive(marker);
        }

        [Test]
        public void HudAndWorldLabelDocumentsUseDifferentPanelSettings()
        {
            Scene scene = EditorSceneManager.OpenScene(BenchmarkScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject hudObject = FindRootObject(scene, "GameUI");
                GameObject worldLabelsObject = FindRootObject(scene, "WorldLabelsUI");
                Assert.That(hudObject, Is.Not.Null, $"Missing 'GameUI' root object in '{BenchmarkScenePath}'.");
                Assert.That(worldLabelsObject, Is.Not.Null,
                    $"Missing 'WorldLabelsUI' root object in '{BenchmarkScenePath}'. Task 002 (wire WorldLabelsUI scene object through the Unity Editor) must run first.");

                UIDocument hudDocument = hudObject.GetComponent<UIDocument>();
                UIDocument worldLabelsDocument = worldLabelsObject.GetComponent<UIDocument>();
                Assert.That(hudDocument, Is.Not.Null, "'GameUI' is missing its UIDocument.");
                Assert.That(worldLabelsDocument, Is.Not.Null, "'WorldLabelsUI' is missing its UIDocument.");
                Assert.That(hudDocument.panelSettings, Is.Not.Null);
                Assert.That(worldLabelsDocument.panelSettings, Is.Not.Null);
                Assert.That(worldLabelsDocument.panelSettings, Is.Not.SameAs(hudDocument.panelSettings));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
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

        private static void AssertPickingIgnoreRecursive(VisualElement element)
        {
            Assert.That(element.pickingMode, Is.EqualTo(PickingMode.Ignore), $"'{element.name}' must be PickingMode.Ignore.");
            for (int i = 0; i < element.hierarchy.childCount; i++)
                AssertPickingIgnoreRecursive(element.hierarchy[i]);
        }
    }
}

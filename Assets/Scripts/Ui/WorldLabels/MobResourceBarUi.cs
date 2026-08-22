using System;
using System.Collections.Generic;
using PlayGround.Mob;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Targets;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace PlayGround.Ui
{
    // Runs after GameplayCamera.LateUpdate (default execution order 0) so the
    // projected position reads the camera's final transform for the frame.
    [DefaultExecutionOrder(500)]
    public sealed class MobResourceBarUi : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private CombatRoot combatRoot;
        [SerializeField] private Camera gameplayCamera;
        [FormerlySerializedAs("healthBarTemplate")]
        [SerializeField] private VisualTreeAsset resourceBarTemplate;
        [FormerlySerializedAs("healthBarStyleSheet")]
        [SerializeField] private StyleSheet resourceBarStyleSheet;

        private VisualElement labelsLayer;
        private readonly Dictionary<MobRoot, ResourceBarEntry> entriesByMob = new();
        private readonly Stack<ResourceBarEntry> pooledEntries = new();
        private readonly List<MobRoot> staleKeys = new();
        private int frameToken;

        private sealed class ResourceBarEntry
        {
            public VisualElement Marker;
            public VisualElement Fill;
            public bool Visible;
            public float FillRatio;
            public int LastSeenFrame;
        }

        private void Awake()
        {
            ValidateSetup();
        }

        private void OnEnable()
        {
            VisualElement root = document.rootVisualElement;
            labelsLayer = root.Q<VisualElement>("labels-layer");
            if (labelsLayer == null)
                throw new InvalidOperationException(
                    $"{nameof(MobResourceBarUi)} could not find the '#labels-layer' element. Assign SkillLoadoutUi.uxml as the UIDocument Source Asset.");
        }

        private void OnDisable()
        {
            foreach (KeyValuePair<MobRoot, ResourceBarEntry> pair in entriesByMob)
                ReleaseEntry(pair.Value);

            entriesByMob.Clear();
            labelsLayer = null;
        }

        private void LateUpdate()
        {
            frameToken++;
            IReadOnlyList<ICombatTarget> targets = combatRoot.TargetRegistry.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] is not MobRoot mob || mob == null || !mob.isActiveAndEnabled)
                    continue;

                if (!entriesByMob.TryGetValue(mob, out ResourceBarEntry entry))
                {
                    entry = AcquireEntry();
                    entriesByMob.Add(mob, entry);
                }

                entry.LastSeenFrame = frameToken;
                Project(mob, entry);
            }

            ReleaseStaleEntries();
        }

        private void ValidateSetup()
        {
            if (document == null)
                throw new InvalidOperationException($"{nameof(MobResourceBarUi)} needs a {nameof(document)} reference.");
            if (combatRoot == null)
                throw new InvalidOperationException($"{nameof(MobResourceBarUi)} needs a {nameof(combatRoot)} reference.");
            if (gameplayCamera == null)
                throw new InvalidOperationException($"{nameof(MobResourceBarUi)} needs a {nameof(gameplayCamera)} reference.");
            if (resourceBarTemplate == null)
                throw new InvalidOperationException($"{nameof(MobResourceBarUi)} needs a {nameof(resourceBarTemplate)} reference.");
            if (resourceBarStyleSheet == null)
                throw new InvalidOperationException($"{nameof(MobResourceBarUi)} needs a {nameof(resourceBarStyleSheet)} reference.");
        }

        private void Project(MobRoot mob, ResourceBarEntry entry)
        {
            Vector3 worldAnchor = mob.ResourceBarAnchorPosition;
            Vector3 viewport = gameplayCamera.WorldToViewportPoint(worldAnchor);
            bool visible = viewport.z > 0f && viewport.x is >= 0f and <= 1f && viewport.y is >= 0f and <= 1f;

            if (!visible)
            {
                SetVisible(entry, false);
                return;
            }

            SetVisible(entry, true);
            Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(labelsLayer.panel, worldAnchor, gameplayCamera);
            entry.Marker.transform.position = new Vector3(panelPosition.x, panelPosition.y, 0f);

            float ratio = mob.MaxHealth > 0f ? Mathf.Clamp01(mob.CurrentHealth / mob.MaxHealth) : 0f;
            if (!Mathf.Approximately(ratio, entry.FillRatio))
            {
                entry.FillRatio = ratio;
                entry.Fill.style.width = Length.Percent(ratio * 100f);
            }
        }

        private static void SetVisible(ResourceBarEntry entry, bool visible)
        {
            if (entry.Visible == visible)
                return;

            entry.Visible = visible;
            entry.Marker.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ReleaseStaleEntries()
        {
            staleKeys.Clear();
            foreach (KeyValuePair<MobRoot, ResourceBarEntry> pair in entriesByMob)
            {
                if (pair.Value.LastSeenFrame != frameToken)
                    staleKeys.Add(pair.Key);
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                MobRoot key = staleKeys[i];
                ReleaseEntry(entriesByMob[key]);
                entriesByMob.Remove(key);
            }
        }

        private ResourceBarEntry AcquireEntry()
        {
            ResourceBarEntry entry = pooledEntries.Count > 0 ? pooledEntries.Pop() : CreateEntry();
            entry.Visible = false;
            entry.FillRatio = -1f;
            entry.Fill.style.width = Length.Percent(100f);
            entry.Marker.style.display = DisplayStyle.None;
            labelsLayer.Add(entry.Marker);
            return entry;
        }

        private ResourceBarEntry CreateEntry()
        {
            VisualElement marker = resourceBarTemplate.Instantiate().Q<VisualElement>("marker");
            if (marker == null)
                throw new InvalidOperationException(
                    $"{nameof(MobResourceBarUi)} could not find the '#marker' element in {nameof(resourceBarTemplate)}.");

            VisualElement fill = marker.Q<VisualElement>("fill");
            if (fill == null)
                throw new InvalidOperationException(
                    $"{nameof(MobResourceBarUi)} could not find the '#fill' element in {nameof(resourceBarTemplate)}.");

            marker.styleSheets.Add(resourceBarStyleSheet);
            SetPickingModeToIgnore(marker);
            return new ResourceBarEntry { Marker = marker, Fill = fill };
        }

        private void ReleaseEntry(ResourceBarEntry entry)
        {
            entry.Marker.RemoveFromHierarchy();
            entry.Visible = false;
            pooledEntries.Push(entry);
        }

        private static void SetPickingModeToIgnore(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < element.hierarchy.childCount; i++)
                SetPickingModeToIgnore(element.hierarchy[i]);
        }
    }
}

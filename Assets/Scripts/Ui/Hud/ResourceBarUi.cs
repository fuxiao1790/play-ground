using System;
using PlayGround.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlayGround.SkillUi
{
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ResourceBarUi : MonoBehaviour
    {
        [SerializeField] private PlayerRoot playerRoot;

        private UIDocument document;
        private VisualElement root;
        private VisualElement healthBar;
        private VisualElement manaBar;
        private VisualElement healthFill;
        private VisualElement manaFill;
        private Label healthTitle;
        private Label manaTitle;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            if (playerRoot == null) playerRoot = FindAnyObjectByType<PlayerRoot>();
            ValidateSetup();
        }

        private void OnEnable()
        {
            root = document.rootVisualElement;
            healthBar = root.Q<VisualElement>("health-bar");
            manaBar = root.Q<VisualElement>("mana-bar");
            healthFill = root.Q<VisualElement>("health-fill");
            manaFill = root.Q<VisualElement>("mana-fill");
            healthTitle = root.Q<Label>("health-title");
            manaTitle = root.Q<Label>("mana-title");
            if (healthBar == null || manaBar == null || healthFill == null || manaFill == null || healthTitle == null || manaTitle == null)
                throw new InvalidOperationException(
                    $"{nameof(ResourceBarUi)} could not find the required resource-orb elements. Assign SkillLoadoutUi.uxml as the UIDocument Source Asset.");

            SetPickingModeToIgnore(healthBar);
            SetPickingModeToIgnore(manaBar);
            healthTitle.pickingMode = PickingMode.Ignore;
            manaTitle.pickingMode = PickingMode.Ignore;
        }

        private void Update()
        {
            UpdateBar(healthFill, healthTitle, playerRoot.CurrentHealth, playerRoot.CombatMaxHealth);
            UpdateBar(manaFill, manaTitle, playerRoot.CurrentMana, playerRoot.CombatMaxMana);
        }

        private void ValidateSetup()
        {
            if (document == null)
                throw new InvalidOperationException($"{nameof(ResourceBarUi)} requires a {nameof(UIDocument)} component.");

            if (playerRoot == null)
                throw new InvalidOperationException($"{nameof(ResourceBarUi)} could not resolve a {nameof(PlayerRoot)}.");

        }

        private static void UpdateBar(VisualElement fill, Label title, float current, float max)
        {
            float percent = max > 0f ? Mathf.Clamp01(current / max) * 100f : 0f;
            fill.style.height = Length.Percent(percent);
            title.text = $"{Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}";
        }

        private static void SetPickingModeToIgnore(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < element.hierarchy.childCount; i++)
                SetPickingModeToIgnore(element.hierarchy[i]);
        }
    }
}

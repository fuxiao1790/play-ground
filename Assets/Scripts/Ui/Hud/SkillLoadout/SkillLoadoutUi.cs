using System;
using System.Collections.Generic;
using PlayGround.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlayGround.Skills
{
    // Runs after UIDocument so document.rootVisualElement is already populated
    // from the source UXML when OnEnable queries #bar.
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class SkillLoadoutUi : MonoBehaviour
    {
        [SerializeField] private SkillDriver skillDriver;
        [SerializeField] private SkillUiCatalog skillCatalog;
        [SerializeField] private SkillUiSupportCatalog supportCatalog;
        [SerializeField] private SkillUiTriggerCatalog triggerCatalog;
        [SerializeField] private PlayerRoot playerRoot;
        [SerializeField, Min(1)] private int initialNodeCount = 3;

        [Header("UXML Templates")]
        [SerializeField] private VisualTreeAsset nodeColumnTemplate;
        [SerializeField] private VisualTreeAsset supportButtonTemplate;
        [SerializeField] private VisualTreeAsset triggerButtonTemplate;
        [SerializeField] private VisualTreeAsset pickerTemplate;
        [SerializeField] private VisualTreeAsset pickerChoiceTemplate;

        private UIDocument document;
        private VisualElement root;
        private VisualElement bar;
        private VisualElement modal;
        private Label[] cooldownLabels;
        private Button[] skillButtons;
        private Button[] increaseButtons;
        private Button[] decreaseButtons;
        private List<Button>[] supportButtonsByNode;
        private PickerTarget pickerTarget;

        private enum PickerKind { Skill, Support, Trigger }

        private readonly struct PickerTarget
        {
            public PickerTarget(PickerKind kind, int nodeIndex, int supportIndex = -1)
            {
                Kind = kind;
                NodeIndex = nodeIndex;
                SupportIndex = supportIndex;
            }

            public PickerKind Kind { get; }
            public int NodeIndex { get; }
            public int SupportIndex { get; }
        }

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            if (skillDriver == null) skillDriver = FindAnyObjectByType<SkillDriver>();
            if (playerRoot == null) playerRoot = FindAnyObjectByType<PlayerRoot>();
            ValidateSetup();
            cooldownLabels = new Label[initialNodeCount];
            skillButtons = new Button[initialNodeCount];
            increaseButtons = new Button[initialNodeCount];
            decreaseButtons = new Button[initialNodeCount];
            supportButtonsByNode = new List<Button>[initialNodeCount];
            skillDriver.ConfigureInitialRuntimeNodeCount(initialNodeCount);
        }

        private void OnEnable()
        {
            root = document.rootVisualElement;
            bar = root.Q<VisualElement>("bar");
            if (bar == null)
                throw new InvalidOperationException(
                    $"{nameof(SkillLoadoutUi)} could not find the '#bar' element. Assign SkillLoadoutUi.uxml as the UIDocument Source Asset.");

            ConfigureHudInputLayering();
            RefreshBar();
            skillDriver.LoadoutChanged += OnLoadoutChanged;
            skillDriver.EditResolved += OnEditResolved;
        }

        private void OnDisable()
        {
            if (skillDriver != null)
            {
                skillDriver.LoadoutChanged -= OnLoadoutChanged;
                skillDriver.EditResolved -= OnEditResolved;
            }

            ClosePicker();
        }

        private void Update()
        {
            for (int i = 0; i < initialNodeCount; i++)
            {
                if (cooldownLabels[i] == null) continue;
                float progress = skillDriver.GetCooldownProgressForNode(i);
                cooldownLabels[i].text = progress > 0f && progress < 1f ? $"{Mathf.CeilToInt((1f - progress) * 10f) / 10f:0.0}" : string.Empty;

                // Only a skill swap is gated by cooldown; adding/removing supports (and cap
                // changes) never bypasses an active cast, so those controls stay enabled.
                bool locked = skillDriver.IsRootOnCooldown(i);
                skillButtons[i]?.SetEnabled(!locked);
                increaseButtons[i]?.SetEnabled(CanIncreaseSupportCap(i));
                decreaseButtons[i]?.SetEnabled(CanDecreaseSupportCap(i));

                List<Button> supports = supportButtonsByNode[i];
                if (supports == null) continue;
                for (int s = 0; s < supports.Count; s++)
                    supports[s].SetEnabled(true);
            }
        }

        private void ValidateSetup()
        {
            if (document == null)
                throw new InvalidOperationException($"{nameof(SkillLoadoutUi)} requires a {nameof(UIDocument)} component.");
            if (skillDriver == null)
                throw new InvalidOperationException($"{nameof(SkillLoadoutUi)} could not resolve a {nameof(SkillDriver)}.");
            if (skillCatalog == null)
                throw new InvalidOperationException($"{nameof(SkillLoadoutUi)} needs a {nameof(SkillUiCatalog)}.");
            if (supportCatalog == null)
                throw new InvalidOperationException($"{nameof(SkillLoadoutUi)} needs a {nameof(SkillUiSupportCatalog)}.");
            if (triggerCatalog == null)
                throw new InvalidOperationException($"{nameof(SkillLoadoutUi)} needs a {nameof(SkillUiTriggerCatalog)}.");
            if (nodeColumnTemplate == null || supportButtonTemplate == null || triggerButtonTemplate == null
                || pickerTemplate == null || pickerChoiceTemplate == null)
                throw new InvalidOperationException($"{nameof(SkillLoadoutUi)} is missing one or more UXML template references.");
        }

        private void ConfigureHudInputLayering()
        {
            VisualElement hudContainer = root.Q<VisualElement>("hud-container");
            VisualElement targetHud = root.Q<VisualElement>("target-hud");
            VisualElement bottomHud = root.Q<VisualElement>("bottom-hud");
            VisualElement healthSlot = root.Q<VisualElement>("health-resource-slot");
            VisualElement manaSlot = root.Q<VisualElement>("mana-resource-slot");
            if (hudContainer == null || targetHud == null || bottomHud == null || healthSlot == null || manaSlot == null)
                throw new InvalidOperationException(
                    $"{nameof(SkillLoadoutUi)} could not find the required HUD layout elements. Assign SkillLoadoutUi.uxml as the UIDocument Source Asset.");

            hudContainer.pickingMode = PickingMode.Ignore;
            targetHud.pickingMode = PickingMode.Ignore;
            bottomHud.pickingMode = PickingMode.Ignore;
            healthSlot.pickingMode = PickingMode.Ignore;
            manaSlot.pickingMode = PickingMode.Ignore;
            bar.pickingMode = PickingMode.Ignore;
        }

        private void RefreshBar()
        {
            ClosePicker();
            bar.Clear();
            for (int nodeIndex = 0; nodeIndex < initialNodeCount; nodeIndex++)
            {
                AddNodeColumn(nodeIndex);
                if (nodeIndex < initialNodeCount - 1) AddTriggerButton(nodeIndex);
            }
        }

        private void AddNodeColumn(int nodeIndex)
        {
            var column = nodeColumnTemplate.Instantiate().Q<VisualElement>("column");
            var supports = column.Q<VisualElement>("supports");
            var decrease = column.Q<Button>("decrease");
            var increase = column.Q<Button>("increase");
            var skill = column.Q<Button>("skill");
            var cooldown = column.Q<Label>("cooldown");

            SkillSet skillSet = GetSkillSet(nodeIndex);
            if (skillSet != null)
            {
                decrease.SetEnabled(CanDecreaseSupportCap(nodeIndex));
                decrease.clicked += () => QueueCapEdit(SkillLoadoutEditKind.DecreaseSupportCap, nodeIndex);
                increase.SetEnabled(CanIncreaseSupportCap(nodeIndex));
                increase.clicked += () => QueueCapEdit(SkillLoadoutEditKind.IncreaseSupportCap, nodeIndex);

                int supportSlotCount = skillSet.SupportSlotCount;
                increaseButtons[nodeIndex] = increase;
                decreaseButtons[nodeIndex] = decrease;
                supportButtonsByNode[nodeIndex] = new List<Button>(supportSlotCount);
                for (int supportIndex = 0; supportIndex < supportSlotCount; supportIndex++)
                {
                    int captured = supportIndex;
                    var support = supportButtonTemplate.Instantiate().Q<Button>("support");
                    support.text = SupportLabel(nodeIndex, supportIndex);
                    support.clicked += () => OpenPicker(new PickerTarget(PickerKind.Support, nodeIndex, captured));
                    supportButtonsByNode[nodeIndex].Add(support);
                    supports.Insert(supports.IndexOf(increase), support);
                }
            }
            else
            {
                decrease.AddToClassList("hidden");
                increase.AddToClassList("hidden");
                increaseButtons[nodeIndex] = null;
                decreaseButtons[nodeIndex] = null;
                supportButtonsByNode[nodeIndex] = null;
            }

            skill.text = SkillLabel(nodeIndex);
            skill.clicked += () => OpenPicker(new PickerTarget(PickerKind.Skill, nodeIndex));
            if (IsTriggered(nodeIndex)) skill.AddToClassList("skill-button--triggered");

            cooldownLabels[nodeIndex] = cooldown;
            skillButtons[nodeIndex] = skill;
            bar.Add(column);
        }

        private void AddTriggerButton(int nodeIndex)
        {
            var trigger = triggerButtonTemplate.Instantiate().Q<Button>("trigger");
            trigger.text = TriggerDisplayName(nodeIndex);
            trigger.clicked += () => OpenPicker(new PickerTarget(PickerKind.Trigger, nodeIndex));
            bar.Add(trigger);
        }

        private void QueueCapEdit(SkillLoadoutEditKind kind, int nodeIndex)
        {
            skillDriver.TryQueueEdit(new SkillLoadoutEditCommand(skillDriver.Revision, kind, nodeIndex), out _);
        }

        private SkillSet GetSkillSet(int nodeIndex)
        {
            var nodes = skillDriver.RuntimeNodes;
            return nodes != null && nodeIndex < nodes.Count ? nodes[nodeIndex]?.SkillSet : null;
        }

        private bool CanIncreaseSupportCap(int nodeIndex)
        {
            SkillSet set = GetSkillSet(nodeIndex);
            return set != null && set.SupportSlotCount < set.MaxSupportCount;
        }

        private bool CanDecreaseSupportCap(int nodeIndex)
        {
            SkillSet set = GetSkillSet(nodeIndex);
            return set != null && set.SupportSlotCount > 0;
        }

        private bool IsTriggered(int nodeIndex)
        {
            var nodes = skillDriver.RuntimeNodes;
            return nodeIndex > 0
                && nodes != null
                && nodeIndex - 1 < nodes.Count
                && nodes[nodeIndex - 1]?.TriggerToNext != null;
        }

        private string SkillLabel(int index) => skillDriver.RuntimeNodes != null && index < skillDriver.RuntimeNodes.Count && skillDriver.RuntimeNodes[index]?.SkillSet?.Skill != null
            ? skillDriver.RuntimeNodes[index].SkillSet.Skill.DisplayName : "+";
        private string SupportLabel(int node, int support) => skillDriver.RuntimeNodes != null && node < skillDriver.RuntimeNodes.Count && skillDriver.RuntimeNodes[node]?.SkillSet?.Supports.Length > support && skillDriver.RuntimeNodes[node].SkillSet.Supports[support] != null
            ? skillDriver.RuntimeNodes[node].SkillSet.Supports[support].name.Substring(0, 1) : "+";
        private string TriggerLabel(int index) => skillDriver.RuntimeNodes != null && index < skillDriver.RuntimeNodes.Count && skillDriver.RuntimeNodes[index]?.TriggerToNext != null
            ? "→" : "+";

        private string TriggerDisplayName(int index)
        {
            if (skillDriver.RuntimeNodes == null || index >= skillDriver.RuntimeNodes.Count)
                return "+";

            TriggerLink trigger = skillDriver.RuntimeNodes[index]?.TriggerToNext;
            if (trigger == null)
                return "+";

            return trigger.DisplayName;
        }

        private void OpenPicker(PickerTarget target)
        {
            pickerTarget = target;
            ClosePicker();
            modal = pickerTemplate.Instantiate().Q<VisualElement>("picker");
            modal.Q<Label>("title").text = $"Select {target.Kind}";
            var choices = modal.Q<VisualElement>("choices");
            AddClear(choices);
            if (target.Kind == PickerKind.Skill)
            {
                foreach (var skill in skillCatalog.Skills)
                {
                    AddChoice(choices, skill.DisplayName,
                        new SkillLoadoutEditCommand(skillDriver.Revision, SkillLoadoutEditKind.SetSkill,
                            target.NodeIndex, skill: skill));
                }
            }
            else if (target.Kind == PickerKind.Support)
            {
                SkillDefinitionTags skillTags = GetSkillSet(target.NodeIndex)?.Skill?.Tags ?? SkillDefinitionTags.None;
                foreach (var support in supportCatalog.Supports)
                {
                    if (!SkillDefinitionTagUtility.HasAny(skillTags, support.SupportedSkillTags))
                        continue;

                    AddChoice(choices, support.DisplayName,
                        new SkillLoadoutEditCommand(skillDriver.Revision, SkillLoadoutEditKind.SetSupport,
                            target.NodeIndex, target.SupportIndex, support: support));
                }
            }
            else
            {
                foreach (var trigger in triggerCatalog.Triggers)
                {
                    AddChoice(choices, trigger.DisplayName,
                        new SkillLoadoutEditCommand(skillDriver.Revision, SkillLoadoutEditKind.SetTrigger,
                            target.NodeIndex, trigger: trigger));
                }
            }
            modal.Q<Button>("cancel").clicked += ClosePicker;
            root.Add(modal);
            playerRoot?.SetGameplayInputSuspended(true);
        }

        private void AddClear(VisualElement choices)
        {
            SkillLoadoutEditKind kind = pickerTarget.Kind == PickerKind.Skill ? SkillLoadoutEditKind.ClearSkill : pickerTarget.Kind == PickerKind.Support ? SkillLoadoutEditKind.ClearSupport : SkillLoadoutEditKind.ClearTrigger;
            AddChoice(choices, "Clear", new SkillLoadoutEditCommand(skillDriver.Revision, kind, pickerTarget.NodeIndex, pickerTarget.SupportIndex));
        }

        private void AddChoice(VisualElement parent, string label, SkillLoadoutEditCommand command)
        {
            var choice = pickerChoiceTemplate.Instantiate().Q<Button>("choice");
            choice.text = string.IsNullOrWhiteSpace(label) ? "Unnamed" : label;
            choice.clicked += () => SubmitChoice(command);
            parent.Add(choice);
        }

        private void SubmitChoice(SkillLoadoutEditCommand command)
        {
            if (!skillDriver.TryQueueEdit(command, out string rejectionReason))
            {
                ShowPickerStatus(rejectionReason);
                return;
            }

            SetPickerPending(true);
        }

        private void SetPickerPending(bool pending)
        {
            modal?.SetEnabled(!pending);
        }

        private void ShowPickerStatus(string message)
        {
            Label title = modal?.Q<Label>("title");
            if (title != null) title.text = message;
        }

        private void ClosePicker()
        {
            modal?.RemoveFromHierarchy(); modal = null;
            playerRoot?.SetGameplayInputSuspended(false);
        }

        private void OnLoadoutChanged(ulong _) => RefreshBar();
        private void OnEditResolved(SkillLoadoutEditResult result)
        {
            if (result.Accepted) return;
            SetPickerPending(false);
            ShowPickerStatus(result.RejectionReason);
        }
    }
}

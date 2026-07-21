using PlayGround.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlayGround.Skills
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class SkillLoadoutUi : MonoBehaviour
    {
        [SerializeField] private SkillDriver skillDriver;
        [SerializeField] private SkillUiCatalog catalog;
        [SerializeField] private PlayerRoot playerRoot;
        [SerializeField, Min(1)] private int initialNodeCount = 3;

        private UIDocument document;
        private VisualElement root;
        private VisualElement bar;
        private VisualElement modal;
        private Label[] cooldownLabels;
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
            cooldownLabels = new Label[initialNodeCount];
            skillDriver?.ConfigureInitialRuntimeNodeCount(initialNodeCount);
        }

        private void OnEnable()
        {
            root = document.rootVisualElement;
            root.Clear();
            BuildBar();
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
            }
        }

        private void BuildBar()
        {
            bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.bottom = 24;
            bar.style.left = new StyleLength(new Length(50, LengthUnit.Percent));
            bar.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.FlexEnd;
            bar.style.backgroundColor = new Color(0.03f, 0.04f, 0.08f, 0.88f);
            bar.style.paddingLeft = 12;
            bar.style.paddingRight = 12;
            bar.style.paddingTop = 10;
            bar.style.paddingBottom = 10;
            root.Add(bar);

            for (int nodeIndex = 0; nodeIndex < initialNodeCount; nodeIndex++)
            {
                AddNodeColumn(nodeIndex);
                if (nodeIndex < initialNodeCount - 1) AddTriggerButton(nodeIndex);
            }
        }

        private void AddNodeColumn(int nodeIndex)
        {
            var column = new VisualElement { style = { flexDirection = FlexDirection.Column, alignItems = Align.Center } };
            var supports = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var runtimeNodes = skillDriver.RuntimeNodes;
            int maxSupportCount = runtimeNodes != null && nodeIndex < runtimeNodes.Count
                ? runtimeNodes[nodeIndex]?.SkillSet?.MaxSupportCount ?? 0
                : 0;
            for (int supportIndex = 0; supportIndex < maxSupportCount; supportIndex++)
            {
                int captured = supportIndex;
                var support = new Button(() => OpenPicker(new PickerTarget(PickerKind.Support, nodeIndex, captured))) { text = SupportLabel(nodeIndex, supportIndex) };
                support.style.width = 34; support.style.height = 28; support.style.fontSize = 10;
                supports.Add(support);
            }

            var skill = new Button(() => OpenPicker(new PickerTarget(PickerKind.Skill, nodeIndex))) { text = SkillLabel(nodeIndex) };
            skill.style.width = 112; skill.style.height = 70; skill.style.whiteSpace = WhiteSpace.Normal;
            var nodes = skillDriver.RuntimeNodes;
            bool triggered = nodeIndex > 0
                && nodes != null
                && nodeIndex - 1 < nodes.Count
                && nodes[nodeIndex - 1]?.TriggerToNext != null;
            if (triggered) skill.style.opacity = 0.45f;
            cooldownLabels[nodeIndex] = new Label { style = { position = Position.Absolute, right = 8, bottom = 4, color = Color.white } };
            skill.Add(cooldownLabels[nodeIndex]);
            column.Add(supports);
            column.Add(skill);
            bar.Add(column);
        }

        private void AddTriggerButton(int nodeIndex)
        {
            var trigger = new Button(() => OpenPicker(new PickerTarget(PickerKind.Trigger, nodeIndex))) { text = TriggerDisplayName(nodeIndex) };
            trigger.style.width = 112; trigger.style.height = 38; trigger.style.marginBottom = 15;
            trigger.style.whiteSpace = WhiteSpace.Normal;
            trigger.style.fontSize = 10;
            bar.Add(trigger);
        }

        private string SkillLabel(int index) => skillDriver.RuntimeNodes != null && index < skillDriver.RuntimeNodes.Count && skillDriver.RuntimeNodes[index]?.SkillSet?.Skill != null
            ? skillDriver.RuntimeNodes[index].SkillSet.Skill.name : "+";
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

            if (catalog != null)
            {
                foreach (var entry in catalog.Triggers)
                {
                    if (entry.Definition == trigger && !string.IsNullOrWhiteSpace(entry.DisplayName))
                        return entry.DisplayName;
                }
            }

            return trigger.name;
        }

        private void OpenPicker(PickerTarget target)
        {
            pickerTarget = target;
            ClosePicker();
            modal = new VisualElement();
            modal.style.position = Position.Absolute; modal.style.top = 24;
            modal.style.left = new StyleLength(new Length(50, LengthUnit.Percent));
            modal.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            modal.style.width = 560; modal.style.backgroundColor = new Color(0.04f, 0.05f, 0.1f, 0.96f);
            modal.style.paddingLeft = 16; modal.style.paddingRight = 16; modal.style.paddingTop = 12; modal.style.paddingBottom = 12;
            modal.Add(new Label($"Select {target.Kind}"));
            var choices = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            modal.Add(choices);
            AddClear(choices);
            if (catalog != null)
            {
                if (target.Kind == PickerKind.Skill) foreach (var entry in catalog.Skills) AddChoice(choices, entry.DisplayName, new SkillLoadoutEditCommand(skillDriver.Revision, SkillLoadoutEditKind.SetSkill, target.NodeIndex, skill: entry.Definition));
                if (target.Kind == PickerKind.Support) foreach (var entry in catalog.Supports) AddChoice(choices, entry.DisplayName, new SkillLoadoutEditCommand(skillDriver.Revision, SkillLoadoutEditKind.SetSupport, target.NodeIndex, target.SupportIndex, support: entry.Definition));
                if (target.Kind == PickerKind.Trigger) foreach (var entry in catalog.Triggers) AddChoice(choices, entry.DisplayName, new SkillLoadoutEditCommand(skillDriver.Revision, SkillLoadoutEditKind.SetTrigger, target.NodeIndex, trigger: entry.Definition));
            }
            var cancel = new Button(ClosePicker) { text = "Cancel" }; modal.Add(cancel);
            root.Add(modal);
            playerRoot?.SetGameplayInputGate(true, true);
        }

        private void AddClear(VisualElement choices)
        {
            SkillLoadoutEditKind kind = pickerTarget.Kind == PickerKind.Skill ? SkillLoadoutEditKind.ClearSkill : pickerTarget.Kind == PickerKind.Support ? SkillLoadoutEditKind.ClearSupport : SkillLoadoutEditKind.ClearTrigger;
            AddChoice(choices, "Clear", new SkillLoadoutEditCommand(skillDriver.Revision, kind, pickerTarget.NodeIndex, pickerTarget.SupportIndex));
        }

        private void AddChoice(VisualElement parent, string label, SkillLoadoutEditCommand command)
        {
            var choice = new Button(() => { if (skillDriver.TryQueueEdit(command, out _)) ClosePicker(); }) { text = string.IsNullOrWhiteSpace(label) ? "Unnamed" : label };
            choice.style.marginRight = 6; choice.style.marginBottom = 6; parent.Add(choice);
        }

        private void ClosePicker()
        {
            modal?.RemoveFromHierarchy(); modal = null;
            playerRoot?.SetGameplayInputGate(false, false);
        }

        private void OnLoadoutChanged(ulong _) { root.Clear(); BuildBar(); }
        private void OnEditResolved(SkillLoadoutEditResult result) { if (!result.Accepted) ClosePicker(); }
    }
}

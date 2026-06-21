namespace PlayGround.Skills
{
    public enum SkillValidationWarningCode
    {
        MissingSkillSet,
        MissingSkill,
        UnsupportedSupportForSkill,
        DanglingTriggerLink,
        UnsupportedTriggerLink,
        UnsupportedTriggerSource,
        UnsupportedTriggerTarget,
        UnsupportedStackingDetonation,
    }

    public readonly struct SkillValidationWarning
    {
        public SkillValidationWarning(
            SkillValidationWarningCode code,
            int slotIndex,
            string message)
        {
            Code = code;
            SlotIndex = slotIndex;
            Message = message;
        }

        public SkillValidationWarningCode Code { get; }
        public int SlotIndex { get; }
        public string Message { get; }

        public override string ToString() => Message;
    }
}

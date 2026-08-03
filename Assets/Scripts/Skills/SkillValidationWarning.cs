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
        SpawnChainDepthExceeded,
        SweptProjectileCannotTrack,
        TrackingProjectileMayTunnel,
    }

    public enum SkillValidationSeverity
    {
        Warning,
        Error,
    }

    public readonly struct SkillValidationWarning
    {
        public SkillValidationWarning(
            SkillValidationWarningCode code,
            int slotIndex,
            string message,
            SkillValidationSeverity severity = SkillValidationSeverity.Warning)
        {
            Code = code;
            SlotIndex = slotIndex;
            Message = message;
            Severity = severity;
        }

        public SkillValidationWarningCode Code { get; }
        public int SlotIndex { get; }
        public string Message { get; }
        public SkillValidationSeverity Severity { get; }

        public override string ToString() => Message;
    }
}

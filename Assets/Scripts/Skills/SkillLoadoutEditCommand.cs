using System;

namespace PlayGround.Skills
{
    public enum SkillLoadoutEditKind
    {
        SetSkill,
        ClearSkill,
        SetSupport,
        ClearSupport,
        IncreaseSupportCap,
        DecreaseSupportCap,
        SetTrigger,
        ClearTrigger,
    }

    public readonly struct SkillLoadoutEditCommand
    {
        public SkillLoadoutEditCommand(
            ulong expectedRevision,
            SkillLoadoutEditKind kind,
            int nodeIndex,
            int supportIndex = -1,
            Skill skill = null,
            SkillSupport support = null,
            TriggerLink trigger = null)
        {
            ExpectedRevision = expectedRevision;
            Kind = kind;
            NodeIndex = nodeIndex;
            SupportIndex = supportIndex;
            Skill = skill;
            Support = support;
            Trigger = trigger;
        }

        public ulong ExpectedRevision { get; }
        public SkillLoadoutEditKind Kind { get; }
        public int NodeIndex { get; }
        public int SupportIndex { get; }
        public Skill Skill { get; }
        public SkillSupport Support { get; }
        public TriggerLink Trigger { get; }
    }

    public readonly struct SkillLoadoutEditResult
    {
        public SkillLoadoutEditResult(bool accepted, ulong revision, string rejectionReason)
        {
            Accepted = accepted;
            Revision = revision;
            RejectionReason = rejectionReason;
        }

        public bool Accepted { get; }
        public ulong Revision { get; }
        public string RejectionReason { get; }
    }
}

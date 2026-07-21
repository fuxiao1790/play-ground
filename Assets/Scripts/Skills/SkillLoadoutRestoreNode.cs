using System;
using System.Collections.Generic;

namespace PlayGround.Skills
{
    public sealed class SkillLoadoutRestoreNode
    {
        public SkillLoadoutRestoreNode(
            Skill skill,
            IReadOnlyList<SkillSupport> supports,
            TriggerLink triggerToNext,
            int supportSlotCount = -1)
        {
            Skill = skill;
            Supports = supports ?? Array.Empty<SkillSupport>();
            TriggerToNext = triggerToNext;
            SupportSlotCount = supportSlotCount;
        }

        public Skill Skill { get; }
        public IReadOnlyList<SkillSupport> Supports { get; }
        public TriggerLink TriggerToNext { get; }
        public int SupportSlotCount { get; }
    }
}

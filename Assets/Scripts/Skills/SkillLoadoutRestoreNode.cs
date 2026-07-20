using System;
using System.Collections.Generic;

namespace PlayGround.Skills
{
    public sealed class SkillLoadoutRestoreNode
    {
        public SkillLoadoutRestoreNode(
            Skill skill,
            IReadOnlyList<SkillSupport> supports,
            TriggerLink triggerToNext)
        {
            Skill = skill;
            Supports = supports ?? Array.Empty<SkillSupport>();
            TriggerToNext = triggerToNext;
        }

        public Skill Skill { get; }
        public IReadOnlyList<SkillSupport> Supports { get; }
        public TriggerLink TriggerToNext { get; }
    }
}

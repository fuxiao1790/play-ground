using System;

namespace PlayGround.Skills
{
    [Flags]
    public enum SkillDefinitionTags
    {
        None = 0,
        Projectile = 1 << 0,
        Aoe = 1 << 1,
        Targeted = 1 << 2,
        Any = Projectile | Aoe | Targeted,
    }

    public static class SkillDefinitionTagUtility
    {
        public static bool HasAny(SkillDefinitionTags value, SkillDefinitionTags mask) =>
            (value & mask) != SkillDefinitionTags.None;

        public static string Format(SkillDefinitionTags tags)
        {
            if (tags == SkillDefinitionTags.None) return "none";
            if (tags == SkillDefinitionTags.Any) return "projectile, AOE, or targeted";
            return tags.ToString();
        }
    }
}

using System;

namespace PlayGround.Player
{
    [Flags]
    public enum GameplayInputBlock
    {
        None = 0,
        Paused = 1 << 0,
        SkillPicker = 1 << 1,
    }
}

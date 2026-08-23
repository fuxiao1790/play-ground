using System;

namespace PlayGround.Persistence
{
    [Serializable]
    public sealed class GameSettingsData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public bool displayMobHealthBars = true;

        public bool IsValid() => version == CurrentVersion;
    }
}

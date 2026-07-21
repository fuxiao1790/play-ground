using System;
using System.Collections.Generic;

namespace PlayGround.Persistence
{
    [Serializable]
    public sealed class PlayerSaveData
    {
        public const int CurrentVersion = 1;
        public const int MaxLoadoutNodes = 256;
        public const int MaxSupportsPerNode = 64;

        public int version = CurrentVersion;
        public PlayerStateSaveData player = new();
        public PlayerSkillLoadoutSaveData skillLoadout = new();

        public bool IsValid()
        {
            if (version != CurrentVersion || player == null || !player.IsValid())
            {
                return false;
            }

            return skillLoadout == null || skillLoadout.IsValid();
        }
    }

    [Serializable]
    public sealed class PlayerStateSaveData
    {
        public float positionX;
        public float positionY;
        public float currentHealth;

        public bool IsValid() =>
            IsFinite(positionX)
            && IsFinite(positionY)
            && IsFinite(currentHealth)
            && currentHealth >= 0f;

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [Serializable]
    public sealed class PlayerSkillLoadoutSaveData
    {
        public List<PlayerSkillNodeSaveData> nodes = new();

        public bool IsValid()
        {
            if (nodes == null || nodes.Count > PlayerSaveData.MaxLoadoutNodes)
            {
                return false;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] == null || !nodes[i].IsValid())
                {
                    return false;
                }
            }

            return true;
        }
    }

    [Serializable]
    public sealed class PlayerSkillNodeSaveData
    {
        public string skillAssetGuid;
        public List<string> supportAssetGuids = new();
        public string triggerToNextAssetGuid;
        public int supportSlotCountPlusOne;

        public bool IsValid()
        {
            if (!IsGuidOrEmpty(skillAssetGuid)
                || !IsGuidOrEmpty(triggerToNextAssetGuid)
                || supportAssetGuids == null
                || supportAssetGuids.Count > PlayerSaveData.MaxSupportsPerNode
                || supportSlotCountPlusOne < 0
                || supportSlotCountPlusOne > PlayerSaveData.MaxSupportsPerNode + 1
                || (supportSlotCountPlusOne > 0 && supportAssetGuids.Count > supportSlotCountPlusOne - 1))
            {
                return false;
            }

            for (int i = 0; i < supportAssetGuids.Count; i++)
            {
                if (!IsGuidOrEmpty(supportAssetGuids[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsGuidOrEmpty(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            if (value.Length != 32)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool isHex = character is >= '0' and <= '9'
                    or >= 'a' and <= 'f'
                    or >= 'A' and <= 'F';
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

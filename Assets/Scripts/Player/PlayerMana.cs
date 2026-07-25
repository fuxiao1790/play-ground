using UnityEngine;

namespace PlayGround.Player
{
    public sealed class PlayerMana
    {
        public PlayerMana(float maxMana)
        {
            MaxMana = Mathf.Max(0f, maxMana);
            CurrentMana = MaxMana;
        }

        public float MaxMana { get; }
        public float CurrentMana { get; private set; }

        public void MirrorCombatMana(float currentMana)
        {
            CurrentMana = Mathf.Clamp(currentMana, 0f, MaxMana);
        }
    }
}

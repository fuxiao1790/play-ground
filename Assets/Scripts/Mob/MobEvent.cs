using UnityEngine;

namespace PlayGround.Mob
{
    public readonly struct MobEvent
    {
        public MobEvent(MobEventType type, Object source = null, float amount = 0f)
        {
            Type = type;
            Source = source;
            Amount = amount;
        }

        public MobEventType Type { get; }
        public Object Source { get; }
        public float Amount { get; }
    }

    public enum MobEventType
    {
        Tick,
        TargetSeen,
        TargetLost,
        Damaged,
        Recovered,
        Died
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Mob
{
    public sealed class MobEventQueue
    {
        private readonly List<MobEvent> events = new();

        public void Push(MobEvent mobEvent)
        {
            events.Add(mobEvent);
        }

        public void PushType(MobEventType type, Object source = null, float amount = 0f)
        {
            events.Add(new MobEvent(type, source, amount));
        }

        public List<MobEvent> Drain()
        {
            List<MobEvent> drained = new(events);
            events.Clear();
            return drained;
        }
    }
}

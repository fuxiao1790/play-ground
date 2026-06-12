using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Mob
{
    public sealed class MobEventQueue
    {
        private readonly List<MobEvent> events = new();
        private readonly List<MobEvent> drainBuffer = new();

        public IReadOnlyList<MobEvent> Events => events;

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
            drainBuffer.Clear();
            drainBuffer.AddRange(events);
            events.Clear();
            return drainBuffer;
        }

        public void Clear()
        {
            events.Clear();
        }
    }
}

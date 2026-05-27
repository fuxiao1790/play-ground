using Godot;
using System.Collections.Generic;

namespace PlayGround.Mob;

public sealed class MobEventQueue
{
    private readonly List<MobEvent> _events = new();

    public void Push(MobEvent mobEvent)
    {
        _events.Add(mobEvent);
    }

    public void PushType(MobEvent.EventType eventType, GodotObject? source = null, int amount = 0, Node2D? target = null)
    {
        Push(new MobEvent(eventType, source, amount, target));
    }

    public List<MobEvent> Drain()
    {
        var drained = new List<MobEvent>(_events);
        _events.Clear();
        return drained;
    }

    public bool IsEmpty() => _events.Count == 0;
}

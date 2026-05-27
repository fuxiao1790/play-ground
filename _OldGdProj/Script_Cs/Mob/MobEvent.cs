using Godot;

namespace PlayGround.Mob;

public sealed class MobEvent
{
    public enum EventType
    {
        Tick,
        TargetSeen,
        TargetLost,
        Damaged,
        Recovered,
        Died,
    }

    public EventType Type { get; }
    public GodotObject? Source { get; }
    public int Amount { get; }
    public Node2D? Target { get; }

    public MobEvent(EventType eventType, GodotObject? source = null, int amount = 0, Node2D? target = null)
    {
        Type = eventType;
        Source = source;
        Amount = amount;
        Target = target;
    }
}

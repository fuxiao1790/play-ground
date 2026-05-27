using Godot;
using PlayGround.Common;

namespace PlayGround.Mob.Triggers;

[GlobalClass]
public partial class TargetSensorTrigger : MobTrigger
{
    [Export] public float detection_radius = 140.0f;
    [Export] public float lose_radius = 180.0f;

    public override void Update(double delta, Mob mob, MobBlackboard blackboard, MobEventQueue events)
    {
        Node2D? target = blackboard.HasValidTarget() ? blackboard.target : FindTarget(mob);
        if (!DamageableState.IsAlive(target))
        {
            if (blackboard.target_visible)
            {
                blackboard.ClearTarget();
                events.PushType(MobEvent.EventType.TargetLost, this);
            }
            return;
        }

        float distance = mob.GlobalPosition.DistanceTo(target!.GlobalPosition);
        if (!blackboard.target_visible && distance <= detection_radius)
        {
            blackboard.target = target;
            blackboard.target_visible = true;
            events.PushType(MobEvent.EventType.TargetSeen, this, target: target);
        }
        else if (blackboard.target_visible && distance > lose_radius)
        {
            blackboard.ClearTarget();
            events.PushType(MobEvent.EventType.TargetLost, this);
        }
        else if (blackboard.target_visible)
        {
            blackboard.RequestTrigger("on_target_seen", 0);
        }
    }

    private static Node2D? FindTarget(Mob mob)
    {
        Node2D? target = mob.GetTree().GetFirstNodeInGroup("player") as Node2D;
        return DamageableState.IsAlive(target) ? target : null;
    }
}

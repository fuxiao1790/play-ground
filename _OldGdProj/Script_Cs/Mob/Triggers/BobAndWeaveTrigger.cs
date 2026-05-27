using Godot;

namespace PlayGround.Mob.Triggers;

[GlobalClass]
public partial class BobAndWeaveTrigger : MobTrigger
{
    [Export] public float min_distance = 64.0f;
    [Export] public float max_distance = 130.0f;

    public override void Update(double delta, Mob mob, MobBlackboard blackboard, MobEventQueue events)
    {
        if (!blackboard.target_visible || !blackboard.HasValidTarget())
        {
            return;
        }

        float distance = mob.GlobalPosition.DistanceTo(blackboard.target!.GlobalPosition);
        if (distance >= min_distance && distance <= max_distance)
        {
            blackboard.RequestTrigger("on_weave_range", 10);
        }
    }
}

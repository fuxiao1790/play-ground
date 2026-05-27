using Godot;

namespace PlayGround.Mob.Triggers;

[GlobalClass]
public partial class PanicTrigger : MobTrigger
{
    [Export] public float panic_health_ratio = 0.35f;

    public override void Update(double delta, Mob mob, MobBlackboard blackboard, MobEventQueue events)
    {
        if (blackboard.HealthRatio() <= panic_health_ratio)
        {
            blackboard.RequestTrigger("on_low_hp", 100);
        }
    }
}

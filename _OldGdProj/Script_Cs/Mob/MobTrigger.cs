using Godot;

namespace PlayGround.Mob;

[GlobalClass]
public partial class MobTrigger : Resource
{
    public virtual void Update(double delta, Mob mob, MobBlackboard blackboard, MobEventQueue events)
    {
    }
}

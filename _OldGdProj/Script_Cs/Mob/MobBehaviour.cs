using Godot;

namespace PlayGround.Mob;

[GlobalClass]
public partial class MobBehaviour : Resource
{
    public virtual StringName Key => "base";

    public virtual bool SupportsState(MobBehaviourState state)
    {
        return true;
    }

    public virtual Vector2 Update(double delta, Mob mob, MobBlackboard blackboard, MobBehaviourState state)
    {
        return Vector2.Zero;
    }
}

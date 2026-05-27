using Godot;

namespace PlayGround.Mob.Triggers;

[GlobalClass]
public partial class HurtRecoveryTrigger : MobTrigger
{
    [Export] public float hurt_duration = 0.35f;

    private float _remaining;
    private bool _inHurt;
    private bool _recoverySent;

    public override void Update(double delta, Mob mob, MobBlackboard blackboard, MobEventQueue events)
    {
        if ((MobBehaviourState)blackboard.behaviour_state != MobBehaviourState.Hurt)
        {
            _inHurt = false;
            _recoverySent = false;
            return;
        }

        if (!_inHurt)
        {
            _inHurt = true;
            _recoverySent = false;
            _remaining = hurt_duration;
            return;
        }

        if (_recoverySent)
        {
            return;
        }

        _remaining -= (float)delta;
        if (_remaining <= 0.0f)
        {
            _recoverySent = true;
            events.PushType(MobEvent.EventType.Recovered, this);
        }
    }
}

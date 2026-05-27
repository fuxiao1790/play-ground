using PlayGround.Common;
using System.Collections.Generic;

namespace PlayGround.Mob;

public sealed class MobStateDriver
{
    private readonly StateMachineCore _stateMachine;
    private readonly MobBlackboard _blackboard;

    public MobStateDriver(StateMachineCore stateMachine, MobBlackboard blackboard)
    {
        _stateMachine = stateMachine;
        _blackboard = blackboard;
    }

    public void Update(List<MobEvent> events)
    {
        foreach (MobEvent mobEvent in events)
        {
            ApplyEvent(mobEvent);
            _blackboard.behaviour_state = _stateMachine.CurrentState;
            if ((MobBehaviourState)_stateMachine.CurrentState == MobBehaviourState.Dead)
            {
                return;
            }
        }
    }

    private void ApplyEvent(MobEvent mobEvent)
    {
        if ((MobBehaviourState)_stateMachine.CurrentState == MobBehaviourState.Dead)
        {
            return;
        }

        switch (mobEvent.Type)
        {
            case MobEvent.EventType.Died:
                _stateMachine.TransitionTo((int)MobBehaviourState.Dead);
                break;
            case MobEvent.EventType.Damaged:
                _blackboard.RequestTrigger("on_hit", 80);
                _stateMachine.TransitionTo((int)MobBehaviourState.Hurt);
                break;
            case MobEvent.EventType.Recovered:
                if ((MobBehaviourState)_stateMachine.CurrentState == MobBehaviourState.Hurt)
                {
                    TransitionAfterRecovery();
                }
                break;
            case MobEvent.EventType.TargetSeen:
                _blackboard.RequestTrigger("on_target_seen", 0);
                if ((MobBehaviourState)_stateMachine.CurrentState is MobBehaviourState.Idle or MobBehaviourState.Wander)
                {
                    _stateMachine.TransitionTo((int)MobBehaviourState.Chase);
                }
                break;
            case MobEvent.EventType.TargetLost:
                if ((MobBehaviourState)_stateMachine.CurrentState == MobBehaviourState.Chase)
                {
                    _stateMachine.TransitionTo((int)MobBehaviourState.Wander);
                }
                break;
            case MobEvent.EventType.Tick:
                if ((MobBehaviourState)_stateMachine.CurrentState == MobBehaviourState.Idle)
                {
                    _stateMachine.TransitionTo((int)MobBehaviourState.Wander);
                }
                break;
        }
    }

    private void TransitionAfterRecovery()
    {
        if (_blackboard.target_visible && _blackboard.HasValidTarget())
        {
            _stateMachine.TransitionTo((int)MobBehaviourState.Chase);
        }
        else
        {
            _stateMachine.TransitionTo((int)MobBehaviourState.Wander);
        }
    }
}

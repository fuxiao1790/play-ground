namespace PlayGround.Mob
{
    public sealed class MobStateDriver
    {
        private readonly MobBlackboard blackboard;

        public MobStateDriver(MobBlackboard blackboard)
        {
            this.blackboard = blackboard;
        }

        public MobBehaviourState CurrentState { get; private set; } = MobBehaviourState.Idle;

        public void Update(global::System.Collections.Generic.List<MobEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                ApplyEvent(events[i]);
                blackboard.BehaviourState = CurrentState;
                if (CurrentState == MobBehaviourState.Dead)
                {
                    return;
                }
            }
        }

        private void ApplyEvent(MobEvent mobEvent)
        {
            if (CurrentState == MobBehaviourState.Dead)
            {
                return;
            }

            switch (mobEvent.Type)
            {
                case MobEventType.Died:
                    CurrentState = MobBehaviourState.Dead;
                    break;
                case MobEventType.Damaged:
                case MobEventType.CritDamaged:
                    blackboard.RequestTrigger("on_hit", 80);
                    CurrentState = MobBehaviourState.Hurt;
                    break;
                case MobEventType.Recovered:
                    if (CurrentState == MobBehaviourState.Hurt)
                    {
                        CurrentState = blackboard.TargetVisible && blackboard.HasValidTarget()
                            ? MobBehaviourState.Chase
                            : MobBehaviourState.Wander;
                    }

                    break;
                case MobEventType.TargetSeen:
                    blackboard.RequestTrigger("on_target_seen");
                    if (CurrentState is MobBehaviourState.Idle or MobBehaviourState.Wander)
                    {
                        CurrentState = MobBehaviourState.Chase;
                    }

                    break;
                case MobEventType.TargetLost:
                    if (CurrentState == MobBehaviourState.Chase)
                    {
                        CurrentState = MobBehaviourState.Wander;
                    }

                    break;
                case MobEventType.Tick:
                    if (CurrentState == MobBehaviourState.Idle)
                    {
                        CurrentState = MobBehaviourState.Wander;
                    }

                    break;
            }
        }
    }
}

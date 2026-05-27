using Godot;
using System.Collections.Generic;

namespace PlayGround.Mob;

public sealed partial class MobBehaviourSelector
{
    private readonly Dictionary<StringName, MobBehaviour> _behaviours = new();
    private readonly MobBehaviour _noBehaviour = new NoBehaviour();
    private Godot.Collections.Dictionary _triggerBehaviourMap = new();

    public void Register(MobBehaviour behaviour)
    {
        if (behaviour.Key.ToString() == string.Empty)
        {
            GD.PushError("MobBehaviourSelector cannot register a behaviour with an empty key.");
            return;
        }

        _behaviours[behaviour.Key] = behaviour;
    }

    public void Configure(Godot.Collections.Dictionary triggerBehaviourMap)
    {
        _triggerBehaviourMap = triggerBehaviourMap.Duplicate();
    }

    public bool HasBehaviour(StringName key) => _behaviours.ContainsKey(key);

    public Vector2 Update(double delta, Mob mob, MobBlackboard blackboard, MobBehaviourState state)
    {
        MobBehaviour behaviour = SelectBehaviour(blackboard, state);
        blackboard.active_behaviour_key = behaviour.Key;
        return behaviour.Update(delta, mob, blackboard, state);
    }

    private MobBehaviour SelectBehaviour(MobBlackboard blackboard, MobBehaviourState state)
    {
        StringName behaviourKey = BehaviourKeyForTrigger(blackboard.requested_trigger_key);
        MobBehaviour requested = GetSupported(behaviourKey, state);
        if (!ReferenceEquals(requested, _noBehaviour))
        {
            blackboard.active_trigger_key = blackboard.requested_trigger_key;
            return requested;
        }

        blackboard.active_trigger_key = "none";
        return _noBehaviour;
    }

    private StringName BehaviourKeyForTrigger(StringName triggerKey)
    {
        if (triggerKey.ToString() == string.Empty)
        {
            return "";
        }

        if (_triggerBehaviourMap.ContainsKey(triggerKey))
        {
            return new StringName(_triggerBehaviourMap[triggerKey].AsString());
        }

        string stringKey = triggerKey.ToString();
        if (_triggerBehaviourMap.ContainsKey(stringKey))
        {
            return new StringName(_triggerBehaviourMap[stringKey].AsString());
        }

        return "";
    }

    private MobBehaviour GetSupported(StringName key, MobBehaviourState state)
    {
        if (key.ToString() == string.Empty || !_behaviours.TryGetValue(key, out MobBehaviour? behaviour))
        {
            return _noBehaviour;
        }

        return behaviour.SupportsState(state) ? behaviour : _noBehaviour;
    }

    private sealed partial class NoBehaviour : MobBehaviour
    {
        public override StringName Key => "none";
    }
}

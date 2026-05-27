using Godot;
using PlayGround.Common;

namespace PlayGround.Mob;

public sealed class MobBlackboard
{
    public Node2D? target;
    public bool target_visible;
    public int behaviour_state = (int)MobBehaviourState.Idle;
    public int health = 1;
    public int max_health = 1;
    public StringName requested_trigger_key = "";
    public StringName active_trigger_key = "none";
    public StringName active_behaviour_key = "none";

    private int _requestedTriggerPriority = -1;

    public void BeginFrame(StringName defaultTriggerKey)
    {
        requested_trigger_key = defaultTriggerKey;
        active_trigger_key = "none";
        _requestedTriggerPriority = defaultTriggerKey.ToString() == string.Empty ? -1 : -100;
    }

    public void RequestTrigger(StringName key, int priority = 0)
    {
        if (key.ToString() == string.Empty || priority < _requestedTriggerPriority)
        {
            return;
        }

        requested_trigger_key = key;
        _requestedTriggerPriority = priority;
    }

    public bool HasValidTarget()
    {
        return DamageableState.IsAlive(target);
    }

    public void ClearTarget()
    {
        target = null;
        target_visible = false;
    }

    public float HealthRatio()
    {
        if (max_health <= 0)
        {
            return 0.0f;
        }

        return Mathf.Clamp((float)health / max_health, 0.0f, 1.0f);
    }
}

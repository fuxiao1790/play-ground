using Godot;

namespace PlayGround.Player;

public sealed class PlayerMovement
{
    public float speed = 200.0f;
    public float stop_threshold = 4.0f;
    public float accel_multiplier = 8.0f;
    public float friction_multiplier = 6.0f;
    public float dash_speed = 560.0f;
    public float dash_duration = 0.16f;
    public float dash_cooldown = 0.45f;

    public Vector2 input_dir = Vector2.Zero;

    private readonly RigidBody2D _body;
    private readonly StringName _dashAction = "dash";
    private float _dashTimer;
    private float _dashCooldownTimer;
    private Vector2 _dashDir = Vector2.Zero;

    public PlayerMovement(RigidBody2D body)
    {
        _body = body;
        foreach (StringName action in new StringName[] { "move_up", "move_down", "move_left", "move_right", _dashAction })
        {
            if (!InputMap.HasAction(action))
            {
                GD.PushError($"PlayerMovement: missing required input action '{action}'. Define it in the Input Map.");
            }
        }
    }

    public void ConfigureDash(float newSpeed, float newDuration, float newCooldown)
    {
        dash_speed = Mathf.Max(newSpeed, speed);
        dash_duration = Mathf.Max(newDuration, 0.01f);
        dash_cooldown = Mathf.Max(newCooldown, dash_duration);
    }

    public void Update(double delta, Vector2 aimWorldPosition)
    {
        float deltaF = (float)delta;
        input_dir = GetInputDir();
        UpdateDashTimers(deltaF);
        if (CanStartDash())
        {
            StartDash(aimWorldPosition);
        }

        if (IsDashing())
        {
            ApplyDash();
        }
        else
        {
            ApplyMovement(input_dir, deltaF);
        }

    }

    public bool IsIdle() => _body.LinearVelocity.Length() <= stop_threshold;
    public bool IsDashing() => _dashTimer > 0.0f;
    public float DashCooldownRemaining() => _dashCooldownTimer;

    private static Vector2 GetInputDir()
    {
        Vector2 direction = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        return direction == Vector2.Zero ? Vector2.Zero : direction.Normalized();
    }

    private void ApplyMovement(Vector2 direction, float delta)
    {
        float acceleration = speed * accel_multiplier;
        float friction = speed * friction_multiplier;
        if (direction != Vector2.Zero)
        {
            _body.LinearVelocity = _body.LinearVelocity.MoveToward(direction * speed, acceleration * delta);
        }
        else
        {
            _body.LinearVelocity = _body.LinearVelocity.MoveToward(Vector2.Zero, friction * delta);
        }

        _body.LinearVelocity = _body.LinearVelocity.LimitLength(speed);
        if (_body.LinearVelocity.Length() < stop_threshold)
        {
            _body.LinearVelocity = Vector2.Zero;
        }
    }

    private void UpdateDashTimers(float delta)
    {
        _dashTimer = Mathf.Max(_dashTimer - delta, 0.0f);
        _dashCooldownTimer = Mathf.Max(_dashCooldownTimer - delta, 0.0f);
        if (!IsDashing())
        {
            _dashDir = Vector2.Zero;
        }
    }

    private bool CanStartDash()
    {
        return Input.IsActionJustPressed(_dashAction)
            && !IsDashing()
            && _dashCooldownTimer <= 0.0f;
    }

    private void StartDash(Vector2 aimWorldPosition)
    {
        _dashDir = GetDashDir(aimWorldPosition);
        _dashTimer = dash_duration;
        _dashCooldownTimer = dash_cooldown;
    }

    private void ApplyDash()
    {
        _body.LinearVelocity = _dashDir * dash_speed;
    }

    private Vector2 GetDashDir(Vector2 aimWorldPosition)
    {
        if (input_dir != Vector2.Zero)
        {
            return input_dir;
        }

        if (_body.LinearVelocity.Length() > stop_threshold)
        {
            return _body.LinearVelocity.Normalized();
        }

        Vector2 aimOffset = aimWorldPosition - _body.GlobalPosition;
        return aimOffset == Vector2.Zero ? Vector2.Right : aimOffset.Normalized();
    }
}

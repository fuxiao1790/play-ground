using Godot;
using System;

namespace PlayGround.Camera;

public partial class GameplayCamera : Camera2D
{
    private const int DebugLimitExtent = 10000000;
    private const int DebugOvalSegments = 64;

    [Export(PropertyHint.Range, "0.0,1.0,0.01")] public float follow_speed = 0.3f;
    [Export(PropertyHint.Range, "0.0,1.0,0.01")] public float mouse_bias = 0.3f;
    [Export(PropertyHint.Range, "0.0,1.0,0.01")] public float oval_width = 0.25f;
    [Export(PropertyHint.Range, "0.0,1.0,0.01")] public float oval_height = 0.25f;
    [Export] public Node2D player = null!;
    [Export] public bool debug_draw;
    [Export] public bool allow_beyond_limits_in_debug = true;
    [Export(PropertyHint.Range, "0.01,0.5,0.01")] public float zoom_step = 0.1f;
    [Export] public float zoom_min = 0.25f;
    [Export] public float zoom_max = 4.0f;
    [Export(PropertyHint.Range, "0.0,1.0,0.01")] public float zoom_speed = 0.2f;

    private float _targetZoom = 1.0f;

    public override void _Ready()
    {
        if (player == null)
        {
            throw new InvalidOperationException("GameplayCamera requires 'player' to be assigned in the scene.");
        }

        _targetZoom = Zoom.X;
        if (allow_beyond_limits_in_debug && OS.IsDebugBuild())
        {
            DisableLimitsForDebug();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouseButton || !mouseButton.Pressed)
        {
            return;
        }

        if (mouseButton.ButtonIndex == MouseButton.WheelUp)
        {
            _targetZoom = Mathf.Clamp(_targetZoom * (1.0f + zoom_step), zoom_min, zoom_max);
        }
        else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
        {
            _targetZoom = Mathf.Clamp(_targetZoom / (1.0f + zoom_step), zoom_min, zoom_max);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 bounds = OvalHalfExtentsWorld();
        float halfWidth = bounds.X;
        float halfHeight = bounds.Y;

        Vector2 worldOffset = player.GlobalPosition - GlobalPosition;
        float ellipse = Mathf.Pow(worldOffset.X / halfWidth, 2.0f)
            + Mathf.Pow(worldOffset.Y / halfHeight, 2.0f);

        if (ellipse > 1.0f)
        {
            Vector2 clamped = worldOffset / Mathf.Sqrt(ellipse);
            GlobalPosition = player.GlobalPosition - clamped;
        }
        else
        {
            GlobalPosition = GlobalPosition.Lerp(DesiredPosition(), follow_speed * (float)delta);
        }

        float newZoom = Zoom.X + ((_targetZoom - Zoom.X) * zoom_speed);
        Zoom = new Vector2(newZoom, newZoom);

        if (debug_draw)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (!debug_draw)
        {
            return;
        }

        Vector2 viewportSize = GetViewportRect().Size;
        float halfWidth = oval_width * viewportSize.X / Zoom.X;
        float halfHeight = oval_height * viewportSize.Y / Zoom.Y;

        Vector2 previous = new(halfWidth, 0.0f);
        for (int i = 1; i <= DebugOvalSegments; i++)
        {
            float angle = Mathf.Tau * i / DebugOvalSegments;
            Vector2 current = new(Mathf.Cos(angle) * halfWidth, Mathf.Sin(angle) * halfHeight);
            DrawLine(previous, current, new Color(1.0f, 1.0f, 0.0f, 0.7f), 1.5f / Zoom.X);
            previous = current;
        }
    }

    private Vector2 DesiredPosition()
    {
        Vector2 bounds = OvalHalfExtentsWorld();
        float halfWidth = bounds.X;
        float halfHeight = bounds.Y;
        Vector2 playerPosition = player.GlobalPosition;

        Vector2 mouseOffsetWorld = (GetGlobalMousePosition() - playerPosition) * mouse_bias;
        Vector2 desired = playerPosition + mouseOffsetWorld;
        Vector2 worldOffset = playerPosition - desired;

        float ellipse = Mathf.Pow(worldOffset.X / halfWidth, 2.0f)
            + Mathf.Pow(worldOffset.Y / halfHeight, 2.0f);

        if (ellipse > 1.0f)
        {
            Vector2 clamped = worldOffset / Mathf.Sqrt(ellipse);
            desired = playerPosition - clamped;
        }

        return desired;
    }

    private Vector2 OvalHalfExtentsWorld()
    {
        Vector2 viewportSize = GetViewportRect().Size;
        return new Vector2(
            oval_width * viewportSize.X / Zoom.X,
            oval_height * viewportSize.Y / Zoom.Y);
    }

    private void DisableLimitsForDebug()
    {
        LimitLeft = -DebugLimitExtent;
        LimitTop = -DebugLimitExtent;
        LimitRight = DebugLimitExtent;
        LimitBottom = DebugLimitExtent;
    }
}

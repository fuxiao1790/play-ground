using Godot;

namespace PlayGround.Player;

public sealed class PlayerFacing
{
    public bool face_mouse = true;
    public bool face_mouse_flip = true;
    public float face_mouse_smooth;

    private readonly AnimatedSprite2D _anim;
    private readonly Node2D _parent;

    public PlayerFacing(AnimatedSprite2D anim, Node2D parent)
    {
        _anim = anim;
        _parent = parent;
    }

    public void Update(double delta, Vector2 aimWorldPosition)
    {
        if (!face_mouse)
        {
            return;
        }

        Vector2 direction = aimWorldPosition - _parent.GlobalPosition;
        if (face_mouse_flip)
        {
            _anim.FlipH = direction.X < 0.0f;
            return;
        }

        float target = direction.Angle();
        if (face_mouse_smooth > 0.0f)
        {
            float t = Mathf.Clamp(face_mouse_smooth * (float)delta, 0.0f, 1.0f);
            _anim.Rotation = Mathf.LerpAngle(_anim.Rotation, target, t);
        }
        else
        {
            _anim.Rotation = target;
        }
    }
}

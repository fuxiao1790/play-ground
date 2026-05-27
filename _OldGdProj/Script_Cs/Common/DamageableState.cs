using Godot;

namespace PlayGround.Common;

public interface IDamageable
{
	bool IsAlive();

	void TakeDamage(int amount);
}

public static class DamageableState
{
    private static readonly StringName IsAliveSnake = "is_alive";
    private static readonly StringName IsAlivePascal = "IsAlive";
    private static readonly StringName TakeDamageSnake = "take_damage";
    private static readonly StringName TakeDamagePascal = "TakeDamage";

    public static bool IsAlive(Node? node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            return false;
        }

        if (node is IDamageable damageable)
        {
            return damageable.IsAlive();
        }

        if (node.HasMethod(IsAliveSnake))
        {
            return node.Call(IsAliveSnake).AsBool();
        }

        if (node.HasMethod(IsAlivePascal))
        {
            return node.Call(IsAlivePascal).AsBool();
        }

        return true;
    }

    public static bool CanTakeDamage(Node? node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            return false;
        }

        return node is IDamageable
            || node.HasMethod(TakeDamageSnake)
            || node.HasMethod(TakeDamagePascal);
    }

    public static void ApplyDamage(Node node, int amount)
    {
        if (node is IDamageable damageable)
        {
            damageable.TakeDamage(amount);
            return;
        }

        if (node.HasMethod(TakeDamageSnake))
        {
            node.Call(TakeDamageSnake, amount);
        }
        else if (node.HasMethod(TakeDamagePascal))
        {
            node.Call(TakeDamagePascal, amount);
        }
    }

    public static bool TryApplyDamage(Node? node, int amount)
    {
        if (node == null || !GodotObject.IsInstanceValid(node) || amount <= 0)
        {
            return false;
        }

        if (node is IDamageable damageable)
        {
            damageable.TakeDamage(amount);
            return true;
        }

        if (node.HasMethod(TakeDamageSnake))
        {
            node.Call(TakeDamageSnake, amount);
            return true;
        }

        if (node.HasMethod(TakeDamagePascal))
        {
            node.Call(TakeDamagePascal, amount);
            return true;
        }

        return false;
    }
}

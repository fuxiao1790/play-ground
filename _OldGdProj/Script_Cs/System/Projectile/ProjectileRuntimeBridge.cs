using Godot;

/// <summary>
/// Projectile callback bridge that forwards world hit events to gameplay listeners.
/// </summary>
internal sealed class GameplayProjectile : Projectile
{
	private readonly ProjectileEventReplayer _eventReplayer;

	public GameplayProjectile(ProjectileEventReplayer eventReplayer)
	{
		_eventReplayer = eventReplayer;
	}

	public void OnHit(in ProjectileHitContext hit)
	{
		_eventReplayer.ForwardHitToListener(in hit);
	}
}

/// <summary>
/// Target callback bridge that exposes live node state as TargetSnapshot data.
/// </summary>
internal sealed class GameplayTarget : Target
{
	public Node2D Node => _node;

	private readonly Node2D _node;
	private readonly int _targetTypeId;
	private readonly int _collisionLayer;
	private EntityHandle _handle = EntityHandle.Invalid;

	public GameplayTarget(Node2D node, int targetTypeId, int collisionLayer)
	{
		_node = node;
		_targetTypeId = targetTypeId;
		_collisionLayer = collisionLayer;
	}

	public void SetHandle(EntityHandle handle)
	{
		_handle = handle;
	}

	public void OnHit(in ProjectileHitContext hit)
	{
	}

	public bool TryGetSnapshot(out TargetSnapshot snapshot)
	{
		if (!GodotObject.IsInstanceValid(_node) || !_handle.IsValid)
		{
			snapshot = default;
			return false;
		}

		snapshot = new TargetSnapshot(
			_handle,
			_targetTypeId,
			_collisionLayer,
			_node.GlobalPosition);
		return true;
	}
}

/// <summary>
/// Adapter-owned target binding that pairs a runtime handle with a live target wrapper.
/// </summary>
internal readonly struct TrackedTarget
{
	public readonly EntityHandle Handle;
	public readonly GameplayTarget Target;

	public TrackedTarget(EntityHandle handle, GameplayTarget target)
	{
		Handle = handle;
		Target = target;
	}
}

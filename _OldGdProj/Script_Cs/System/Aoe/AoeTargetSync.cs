using Godot;
using PlayGround.Common;
using System;
using System.Collections.Generic;

/// <summary>
/// Synchronizes live Godot target nodes into plain AOE target snapshots.
/// </summary>
internal sealed class AoeTargetSync
{
	private readonly AoeWorld _world;
	private readonly EntityRegistry _registry;
	private readonly AoeTypeRegistry _typeRegistry;
	private readonly Dictionary<Node2D, TrackedTarget> _trackedTargets = new();
	private readonly HashSet<Node2D> _seenNodes = new();
	private readonly List<Node2D> _staleNodes = new(1);
	private readonly List<TargetSnapshot> _targetSnapshots = new(1);

	public AoeTargetSync(
		AoeWorld world,
		EntityRegistry registry,
		AoeTypeRegistry typeRegistry)
	{
		_world = world;
		_registry = registry;
		_typeRegistry = typeRegistry;
	}

	public void Sync(SceneTree tree, StringName targetGroup)
	{
		_seenNodes.Clear();
		_staleNodes.Clear();
		_targetSnapshots.Clear();

		foreach (Node node in tree.GetNodesInGroup(targetGroup))
		{
			if (node is not Node2D targetNode || !GodotObject.IsInstanceValid(targetNode))
			{
				continue;
			}

			_seenNodes.Add(targetNode);
			if (!_trackedTargets.TryGetValue(targetNode, out TrackedTarget trackedTarget))
			{
				trackedTarget = CreateTrackedTargetOrThrow(targetNode);
				_trackedTargets.Add(targetNode, trackedTarget);
			}

			if (trackedTarget.Target.TryGetSnapshot(out TargetSnapshot snapshot))
			{
				_targetSnapshots.Add(snapshot);
			}
		}

		foreach ((Node2D node, TrackedTarget trackedTarget) in _trackedTargets)
		{
			bool isAlive = GodotObject.IsInstanceValid(node) && DamageableState.IsAlive(node);
			if (isAlive && _seenNodes.Contains(node))
			{
				continue;
			}

			_staleNodes.Add(node);
			_registry.RemoveTarget(trackedTarget.Handle);
		}

		for (int i = 0; i < _staleNodes.Count; i++)
		{
			_trackedTargets.Remove(_staleNodes[i]);
		}

		_world.SubmitTargets(_targetSnapshots);
	}

	public void Clear()
	{
		foreach (TrackedTarget trackedTarget in _trackedTargets.Values)
		{
			_registry.RemoveTarget(trackedTarget.Handle);
		}

		_trackedTargets.Clear();
		_seenNodes.Clear();
		_staleNodes.Clear();
		_targetSnapshots.Clear();
	}

	private TrackedTarget CreateTrackedTargetOrThrow(Node2D node)
	{
		CollisionShape2D? hurtboxShape = node.GetNodeOrNull<CollisionShape2D>("Hurtbox/HurtboxShape");
		Area2D? hurtbox = node.GetNodeOrNull<Area2D>("Hurtbox");
		if (hurtboxShape == null || hurtbox == null)
		{
			throw new InvalidOperationException(
				$"AOE target '{node.Name}' requires Hurtbox/HurtboxShape before it can be registered.");
		}

		int targetTypeId = _typeRegistry.RegisterTargetType(hurtboxShape);
		GameplayTarget gameplayTarget = new(
			node,
			targetTypeId,
			unchecked((int)hurtbox.CollisionLayer));
		EntityHandle handle = _registry.RegisterTarget(gameplayTarget);
		gameplayTarget.SetHandle(handle);
		return new TrackedTarget(handle, gameplayTarget);
	}
}

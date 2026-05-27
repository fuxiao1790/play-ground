using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Converts public Godot AOE spawn calls into plain AoeWorld spawn commands and replays AOE events.
/// </summary>
internal sealed class AoeSpawnAdapter
{
	private readonly AoeWorld _world;
	private readonly EntityRegistry _registry;
	private readonly AoeTypeRegistry _typeRegistry;
	private readonly AoeRenderer _renderer;
	private readonly Dictionary<EntityHandle, Aoe> _hitListeners = new();
	private readonly List<AoeHitEvent> _hitEvents = new(1);
	private readonly List<AoeDespawnedEvent> _despawnedEvents = new(1);

	public AoeSpawnAdapter(
		AoeWorld world,
		EntityRegistry registry,
		AoeTypeRegistry typeRegistry,
		AoeRenderer renderer)
	{
		_world = world;
		_registry = registry;
		_typeRegistry = typeRegistry;
		_renderer = renderer;
	}

	public void SpawnAoe(in AoeSpawnRequest request)
	{
		if (request.Source == null)
		{
			throw new InvalidOperationException("AOE spawn request requires a source node.");
		}

		_typeRegistry.ValidateRegisteredAoeType(request.AoeTypeId);

		EntityHandle handle = _registry.RegisterProjectile(new GameplayAoe());
		if (request.HitListener != null)
		{
			_hitListeners[handle] = request.HitListener;
		}

		_renderer.OnSpawn(handle, request.AoeTypeId, request.Position);

		AoeSpawnCommand command = new(
			handle,
			request.AoeTypeId,
			request.Position,
			request.TargetMask,
			request.Damage,
			request.LifetimeSeconds,
			request.TickIntervalSeconds);
		_world.SubmitSpawn(in command);
	}

	public void DrainEvents()
	{
		_world.DrainEvents(_hitEvents, _despawnedEvents);
		for (int i = 0; i < _despawnedEvents.Count; i++)
		{
			EntityHandle source = _despawnedEvents[i].Source;
			_renderer.OnDespawn(source);
			_hitListeners.Remove(source);
			_registry.RemoveProjectile(source);
		}

		_despawnedEvents.Clear();
	}

	public void ReplayHits()
	{
		for (int i = 0; i < _hitEvents.Count; i++)
		{
			AoeHitEvent hitEvent = _hitEvents[i];
			if (!_hitListeners.TryGetValue(hitEvent.Source, out Aoe? listener))
			{
				continue;
			}

			if (!_registry.TryGetTarget(hitEvent.Target, out Target target)
				|| target is not GameplayTarget gameplayTarget)
			{
				continue;
			}

			AoeHitContext context = new(
				hitEvent.Source,
				hitEvent.Target,
				hitEvent.AoeTypeId,
				gameplayTarget.Node,
				hitEvent.Position,
				hitEvent.Damage);
			listener.OnHit(in context);
		}

		_hitEvents.Clear();
	}

	public void Clear()
	{
		_hitListeners.Clear();
		_hitEvents.Clear();
		_despawnedEvents.Clear();
	}

	private sealed class GameplayAoe : Projectile
	{
		public void OnHit(in ProjectileHitContext hit)
		{
		}
	}
}

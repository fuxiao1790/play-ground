using System.Collections.Generic;

/// <summary>
/// Drains world events and replays gameplay callbacks on the adapter side.
/// </summary>
internal sealed class ProjectileEventReplayer
{
	private readonly ProjectileWorld _world;
	private readonly Renderer _renderer;
	private readonly EntityRegistry _registry;
	private readonly Dictionary<EntityHandle, Projectile> _hitListeners = new();
	private readonly List<ProjectileSpawnedEvent> _spawnedEvents = new(1);
	private readonly List<ProjectileDespawnedEvent> _despawnedEvents = new(1);
	private readonly List<TargetDespawnedEvent> _targetDespawnedEvents = new(1);
	private readonly List<HitEvent> _hitEvents = new(1);
	private readonly List<ChildProjectileSpawnRequestedEvent> _childSpawnRequestedEvents = new(1);

	public ProjectileEventReplayer(
		ProjectileWorld world,
		Renderer renderer,
		EntityRegistry registry)
	{
		_world = world;
		_renderer = renderer;
		_registry = registry;
	}

	public void RegisterHitListener(EntityHandle handle, Projectile? hitListener)
	{
		if (hitListener == null)
		{
			return;
		}

		_hitListeners[handle] = hitListener;
	}

	public void Drain(
		ChildSpawnRequestHandler? childSpawnHandler = null,
		ProjectileDespawnHandler? projectileDespawnHandler = null)
	{
		_world.DrainEvents(
			_spawnedEvents,
			_despawnedEvents,
			_targetDespawnedEvents,
			_hitEvents,
			_childSpawnRequestedEvents);

		foreach (ProjectileSpawnedEvent spawnedEvent in _spawnedEvents)
		{
			if (_world.TryGetActiveProjectileIndex(spawnedEvent.Projectile, spawnedEvent.ActiveIndex, out int projectileIndex))
			{
				_renderer.OnSpawn(_world.ActiveProjectiles, projectileIndex);
			}
		}

		foreach (ProjectileDespawnedEvent despawnedEvent in _despawnedEvents)
		{
			projectileDespawnHandler?.Invoke(in despawnedEvent.Data);
			_renderer.OnDespawn(in despawnedEvent.Data);
			_registry.RemoveProjectile(despawnedEvent.Projectile);
			_hitListeners.Remove(despawnedEvent.Projectile);
		}

		foreach (TargetDespawnedEvent targetDespawnedEvent in _targetDespawnedEvents)
		{
			_registry.RemoveTarget(targetDespawnedEvent.Target);
		}

		foreach (ChildProjectileSpawnRequestedEvent childSpawnRequestedEvent in _childSpawnRequestedEvents)
		{
			childSpawnHandler?.Invoke(in childSpawnRequestedEvent);
		}

		_spawnedEvents.Clear();
		_despawnedEvents.Clear();
		_targetDespawnedEvents.Clear();
		_childSpawnRequestedEvents.Clear();
	}

	public void ReplayHits()
	{
		foreach (HitEvent hitEvent in _hitEvents)
		{
			if (!_registry.TryGetProjectile(hitEvent.source, out Projectile source))
			{
				continue;
			}

			if (!_registry.TryGetTarget(hitEvent.target, out Target target))
			{
				continue;
			}

			if (target is not GameplayTarget gameplayTarget)
			{
				continue;
			}

			ProjectileHitContext context = new(
				hitEvent.source,
				hitEvent.target,
				hitEvent.projectileTypeId,
				gameplayTarget.Node,
				hitEvent.position,
				hitEvent.damage);
			source.OnHit(in context);
			target.OnHit(in context);
		}

		_hitEvents.Clear();
	}

	public void ForwardHitToListener(in ProjectileHitContext hit)
	{
		if (_hitListeners.TryGetValue(hit.Source, out Projectile? hitListener))
		{
			hitListener.OnHit(in hit);
		}
	}

	public void ClearProjectiles()
	{
		ProjectileStore activeProjectiles = _world.ActiveProjectiles;
		for (int i = 0; i < activeProjectiles.Count; i++)
		{
			ProjectileSnapshot projectile = activeProjectiles.Snapshot(i);
			_renderer.OnDespawn(in projectile);
			_registry.RemoveProjectile(projectile.Entity);
			_hitListeners.Remove(projectile.Entity);
		}

		ProjectileStore pendingProjectiles = _world.PendingProjectiles;
		for (int i = 0; i < pendingProjectiles.Count; i++)
		{
			EntityHandle projectile = pendingProjectiles.Entity[i];
			_registry.RemoveProjectile(projectile);
			_hitListeners.Remove(projectile);
		}
	}

	public void ClearEventsAndListeners()
	{
		_hitEvents.Clear();
		_spawnedEvents.Clear();
		_despawnedEvents.Clear();
		_targetDespawnedEvents.Clear();
		_childSpawnRequestedEvents.Clear();
		_hitListeners.Clear();
	}

	public delegate void ChildSpawnRequestHandler(in ChildProjectileSpawnRequestedEvent request);
	public delegate void ProjectileDespawnHandler(in ProjectileSnapshot data);
}

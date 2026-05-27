using System.Collections.Generic;

/// <summary>
/// Adapter-side handle registry that maps runtime entity handles to projectile and target callbacks.
/// </summary>
public sealed class EntityRegistry
{
	private int nextId = 1;
	private readonly Dictionary<EntityHandle, Projectile> projectiles = new();
	private readonly Dictionary<EntityHandle, Target> targets = new();

	public EntityHandle RegisterProjectile(Projectile projectile)
	{
		EntityHandle handle = NextHandle();
		projectiles.Add(handle, projectile);
		return handle;
	}

	public EntityHandle RegisterTarget(Target target)
	{
		EntityHandle handle = NextHandle();
		targets.Add(handle, target);
		return handle;
	}

	public bool TryGetProjectile(EntityHandle handle, out Projectile projectile)
	{
		return projectiles.TryGetValue(handle, out projectile!);
	}

	public bool ContainsProjectile(EntityHandle handle)
	{
		return projectiles.ContainsKey(handle);
	}

	public bool TryGetTarget(EntityHandle handle, out Target target)
	{
		return targets.TryGetValue(handle, out target!);
	}

	public bool ContainsTarget(EntityHandle handle)
	{
		return targets.ContainsKey(handle);
	}

	public void RemoveProjectile(EntityHandle handle)
	{
		projectiles.Remove(handle);
	}

	public void RemoveTarget(EntityHandle handle)
	{
		targets.Remove(handle);
	}

	private EntityHandle NextHandle()
	{
		return new EntityHandle(nextId++);
	}
}

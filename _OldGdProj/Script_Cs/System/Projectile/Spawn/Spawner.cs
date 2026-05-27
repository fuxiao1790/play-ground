/// <summary>
/// Legacy spawn promotion helper kept for older projectile root paths and tests.
/// </summary>
public class Spawner
{
	public void FlushPendingProjectiles(
		ProjectileStore pendingSpawn,
		ProjectileStore activeProjectiles,
		int maximumProjectileListSize,
		Renderer? renderer)
	{
		if (pendingSpawn.Count == 0)
		{
			return;
		}

		int availableSlots = maximumProjectileListSize - activeProjectiles.Count;
		if (availableSlots <= 0)
		{
			pendingSpawn.Clear();
			return;
		}

		int spawnCount = pendingSpawn.Count;
		if (spawnCount > availableSlots)
		{
			spawnCount = availableSlots;
		}

		activeProjectiles.EnsureCapacity(activeProjectiles.Count + spawnCount);
		for (int i = 0; i < spawnCount; i++)
		{
			int activeIndex = activeProjectiles.AppendFrom(pendingSpawn, i);
			renderer?.OnSpawn(activeProjectiles, activeIndex);
		}

		pendingSpawn.RemoveRange(0, spawnCount);
	}
}
